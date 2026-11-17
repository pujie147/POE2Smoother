#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# ///
"""Mine official Chinese names + terminology out of the PoE2 client, for `Labels/*.json`.

Why this exists: the effects tab's category ids come from GGPK *folder* names
(`metadata/effects/spells/crossbow_stormblast/…`), and the client carries official display names in
`data/balance/<table>.datc64` plus a per-language overlay (`data/balance/traditional chinese/…`, and
`simplified chinese/…` once that language is downloaded). Copying those beats inventing translations —
inventing them produced 68 names that the official tables contradicted.

Dev-time tool only: the app never reads `.datc64`. Output is a review TSV; a human merges accepted rows
into `src/PsRules/Labels/labels-zh.json` (official) or `labels-zh-named.json` (ours). Never wire this
into a release path without that review — a wrong label makes people tick the wrong row.

Format notes (each of these cost a wasted pass once):
  * `.datc64` is NOT compressed. It is a columnar container whose string pool is UTF-16LE, so plain
    `strings` finds nothing — use `strings -el`, or read raw bytes as this script does.
  * pool layout per row: Id, Name, Description, Icon, … A row with an empty Name column puts the
    *Description* in the next slot, so the name is picked by shape, not by position alone.
  * strings are padded out to even byte offsets: "adjacent" means a gap of 0/2/4/6 bytes. An equality
    test on the gap silently drops every row.
  * the overlay keeps the base file's row order and its ASCII Ids, so aligning by Id text works.

Usage:
    python3 -I scripts/mine-zh-names.py --index "<…>/Bundles2/_.index.bin" all
    python3 -I scripts/mine-zh-names.py … names --language "traditional chinese"
    python3 -I scripts/mine-zh-names.py … terms areas
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import re
import subprocess
import sys

# Tables the client actually carries Chinese in. Overlay dir name is the language folder.
BASE_TABLES = ("activeskills", "acts", "worldareas", "leaguenames")
TERM_SOURCES = ("activeskills", "keywordpopups", "buffdefinitions", "clientstrings2")

IDENT = re.compile(r"[a-z0-9_]{3,}")
CJK = re.compile(r"[㐀-鿿豈-﫿]")
SENTENCE = re.compile(r"[。！？!?]|\.\s")
DNT = re.compile(r"^\s*(\[DNT[^\]]*\]|TODO|N/?A)\b", re.I)
PLACEHOLDER = re.compile(r"[${}\[\]|]")
TERM_PAIR = re.compile(r"\[([A-Za-z][A-Za-z0-9 '_-]{1,32})\|([^\]\|]{1,14})\]")
STOP_TOKENS = {"of", "the", "to", "in", "on", "and", "an", "a", "an", "with"}
ASSET_EXT = re.compile(r"\.(ao|dds|pet|epk|mat|fxgraph|aoc)$", re.I)


def read_table(index: str, pscli: str, work: str, game_path: str) -> bytes:
    """Pull one file out of the index. Uses --out into our own scratch dir (never the repo)."""
    name = re.sub(r"[^a-z0-9]+", "_", game_path.lower()) + ".bin"
    dest = os.path.join(work, name)
    if not os.path.exists(dest):
        subprocess.run([pscli, "read-file", "--index", index, "--path", game_path, "--out", dest],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    with open(dest, "rb") as f:
        return f.read()


def utf16_pool(buf: bytes) -> list[tuple[int, str]]:
    """NUL-terminated UTF-16LE runs in file order, skipping the numeric offset columns."""
    out, i, n = [], 0, len(buf)
    while i < n - 1:
        cu = buf[i] | (buf[i + 1] << 8)
        if cu in (0, 0xFFFE, 0xFEFF) or 0xD800 <= cu <= 0xDFFF or cu < 0x20:
            i += 2
            continue
        j, ok = i, True
        while j < n - 1:
            c = buf[j] | (buf[j + 1] << 8)
            if c == 0:
                break
            if c in (0xFFFE, 0xFEFF) or 0xD800 <= c <= 0xDFFF or c < 0x20:
                ok = False
                break
            j += 2
        if ok and j > i:
            out.append((i, buf[i:j].decode("utf-16-le")))
            i = j + 2
        else:
            i += 2
    return out


def looks_like_name(text: str, want_cjk: bool) -> bool:
    """Display-name shape test — a Name column is short and sentence-free; an empty Name column
    leaves the Description in the next slot and must not be mistaken for a name."""
    if not text or len(text) > 48 or SENTENCE.search(text) or DNT.match(text):
        return False
    has_cjk = bool(CJK.search(text))
    if want_cjk:
        return has_cjk
    return not has_cjk and not IDENT.fullmatch(text) and bool(re.search(r"[A-Za-z]{2,}", text))


def rows_of(buf: bytes, want_cjk: bool, key: re.Pattern = IDENT) -> dict[str, str]:
    """identifier -> display name, taken from the adjacent pool slot when it looks like a name.

    `key` differs per table: ActiveSkills ids are lowercase snake (`frost_bolt`), while WorldAreas/
    Acts/LeagueNames key by CamelCase with digits (`G3_5`, `HideoutDarkwood`, `DawnOfTheHunt`) — using
    only the snake pattern makes those tables read as having no rows at all.
    """
    pool = utf16_pool(buf)
    out: dict[str, str] = {}
    for k, (off, text) in enumerate(pool):
        if not key.fullmatch(text.strip()) or k + 1 >= len(pool):
            continue
        if pool[k + 1][0] - (off + len(text) * 2 + 2) not in (0, 2, 4, 6):
            continue
        name = re.sub(r"\s+", " ", pool[k + 1][1]).strip()
        if not looks_like_name(name, want_cjk):
            continue
        out.setdefault(text.strip(), name)
    return out


LOOSE_KEY = re.compile(r"[A-Za-z][A-Za-z0-9 .'_-]{1,33}")


def tokens(ident: str) -> list[str]:
    return [t for t in ident.split("_") if t and not t.isdigit() and t not in STOP_TOKENS]


class Joiner:
    """Category id -> table row, by token-set equality after ≤2 leading category prefixes."""

    def __init__(self, base: dict[str, str], our_ids: list[str]):
        self.base = base
        self.vocab = {t for ident in base for t in tokens(ident)}
        self.by_set: dict[frozenset[str], list[str]] = {}
        for ident in base:
            ts = frozenset(tokens(ident))
            if ts:
                self.by_set.setdefault(ts, []).append(ident)
        lead = collections.Counter(tokens(i.split(".", 1)[1])[0]
                                   for i in our_ids if tokens(i.split(".", 1)[1]))
        self.prefixes = {t for t, n in lead.items() if n >= 5}

    def unfuse(self, token: str) -> list[str]:
        if token in self.vocab:
            return [token]
        for i in range(1, len(token)):
            head, tail = token[:i], token[i:]
            if head in self.vocab and tail in self.vocab:
                return [head, tail]
        return [token]

    def find(self, seg: str) -> tuple[str | None, list[str]]:
        seq = tokens(seg)
        if not seq:
            return None, []
        hits: set[str] = set()
        for j in range(0, 3):
            if j > 0 and (j - 1 >= len(seq) or seq[j - 1] not in self.prefixes):
                break
            expanded: set[str] = set()
            for t in seq[j:]:
                expanded.update(self.unfuse(t))
            hits.update(self.by_set.get(frozenset(expanded), []))
        if len(hits) == 1:
            return next(iter(hits)), []
        return None, sorted(hits)


def cmd_names(args, index, pscli, work, our_ids, groups):
    lang = args.language
    base = rows_of(read_table(index, pscli, work, f"data/balance/activeskills.datc64"), False)
    over = rows_of(read_table(index, pscli, work,
                              f"data/balance/{lang}/activeskills.datc64"), True)
    shared = [i for i in over if i in base]
    print(f"activeskills: base {len(base)} 行, overlay {len(over)} 行, 可对齐 {len(shared)}",
          file=sys.stderr)

    join = Joiner(base, our_ids)
    existing = set(json.load(open(args.table))["labels"]) if os.path.exists(args.table) else set()
    named, ambiguous, missed = [], [], []
    for cid in our_ids:
        seg = ASSET_EXT.sub("", cid.split(".", 1)[1])
        ident, cands = join.find(seg)
        if cands:
            ambiguous.append((cid, "|".join(cands)[:90]))
            continue
        if not ident:
            missed.append(cid)
            continue
        name = (over.get(ident) or "").strip()
        if not name or PLACEHOLDER.search(name) or len(name) > 20:
            missed.append(cid)
            continue
        named.append((cid, name, base.get(ident, ""), ident, "already" if cid in existing else "new"))

    # Environment/terrain/league ids are keyed by WorldAreas/Acts/LeagueNames rather than
    # ActiveSkills: `env.g3_5` really is a named zone (「龍蜥濕地」). Assuming those were meaningless
    # developer codes — and leaving them English — was wrong for ~78 rows.
    zone_keys: dict[str, tuple[str, str]] = {}
    for table in ("worldareas", "acts", "leaguenames"):
        try:
            zbase = rows_of(read_table(index, pscli, work, f"data/balance/{table}.datc64"), False, LOOSE_KEY)
            zover = rows_of(read_table(index, pscli, work,
                                       f"data/balance/{lang}/{table}.datc64"), True, LOOSE_KEY)
        except subprocess.CalledProcessError:
            continue
        for ident, name in zover.items():
            zone_keys.setdefault(ident.strip().lower(), (name, zbase.get(ident, "")))

    zone_hits, approx, still = [], [], []
    for cid in missed:
        if cid.split(".", 1)[0] not in ("env", "terrain", "league"):
            still.append(cid)
            continue
        seg = ASSET_EXT.sub("", cid.split(".", 1)[1])
        exact = (seg, seg.replace("_", ""), f"{seg[:1].upper()}{seg[1:]}", seg.replace("_", " "))
        done = False
        for cand in exact:
            got = zone_keys.get(cand.lower())
            if got:
                zone_hits.append((cid, got[0], got[1], cand, "zone"))
                done = True
                break
        if done:
            continue
        # A suffix like `_four`/`_new`/`_02` marks a variant of a *named* area. The area name is
        # official, the variant reading is ours — so these go to a separate file and into the
        # "named by us" table, never claimed as verbatim official.
        stripped = re.sub(r"(_four|_new|_alt|_basic|_nowind|_large|_small|_v\d+|_\d+)$", "", seg)
        if stripped and stripped != seg:
            for cand in (stripped, f"{stripped[:1].upper()}{stripped[1:]}"):
                got = zone_keys.get(cand.lower())
                if got:
                    approx.append((cid, got[0], got[1], cand))
                    break
            else:
                still.append(cid)
        else:
            still.append(cid)
    missed = still

    out = os.path.join(args.out, "names.tsv")
    with open(out, "w", encoding="utf-8") as f:
        f.write("id\tofficial\tenglish_row\trow\tnote\n")
        for row in sorted(named + zone_hits):
            f.write("\t".join(row) + "\n")
    with open(os.path.join(args.out, "zone-approx.tsv"), "w", encoding="utf-8") as f:
        f.write("id\tofficial_area\tenglish_area\tstripped_key\n")
        for row in sorted(approx):
            f.write("\t".join(row) + "\n")
    print(f"技能命中 {len(named)},区域/幕/联赛命中 {len(zone_hits)},变体近似 {len(approx)},"
          f"新增 {sum(1 for r in named + zone_hits if r[4] in ('new', 'zone'))},"
          f"改写现有 {sum(1 for r in named if r[4] == 'already')},"
          f"歧义 {len(ambiguous)},未命中 {len(missed)}",
          file=sys.stderr)
    print(f"-> {out}", file=sys.stderr)
    with open(os.path.join(args.out, "ambiguous.tsv"), "w", encoding="utf-8") as f:
        for cid, cands in sorted(ambiguous):
            f.write(f"{cid}\t{cands}\n")
    with open(os.path.join(args.out, "missed.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(sorted(missed)) + "\n")


def cmd_areas(args, index, pscli, work):
    """Official zone/act names — the only part of the environment group with an authority to copy."""
    lang = args.language
    out = {}
    for table in ("acts", "worldareas", "leaguenames"):
        try:
            base = rows_of(read_table(index, pscli, work, f"data/balance/{table}.datc64"), False, LOOSE_KEY)
            over = rows_of(read_table(index, pscli, work,
                                      f"data/balance/{lang}/{table}.datc64"), True, LOOSE_KEY)
        except subprocess.CalledProcessError:
            print(f"{table}: 客户端没有这张表,跳过", file=sys.stderr)
            continue
        pairs = [(i, over[i], base.get(i, "")) for i in sorted(over) if i in base]
        out[table] = pairs
        print(f"{table}: 对齐 {len(pairs)} 行", file=sys.stderr)
        for i, zh, en in pairs[:14]:
            print(f"   {i:28} {zh}   [{en}]", file=sys.stderr)
    path = os.path.join(args.out, "areas.json")
    json.dump(out, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"-> {path}", file=sys.stderr)


def cmd_terms(args, index, pscli, work):
    """Official [English|繁體] keyword pairs — the glossary our own translations must agree with."""
    counts: collections.Counter = collections.Counter()
    pairs: dict[tuple[str, str], int] = collections.Counter()
    lang = args.language
    for table in TERM_SOURCES:
        for sub in (f"data/balance/{lang}/{table}.datc64",):
            try:
                buf = read_table(index, pscli, work, sub)
            except subprocess.CalledProcessError:
                print(f"{table}: 缺表,跳过", file=sys.stderr)
                continue
            for _, text in utf16_pool(buf):
                for en, zh in TERM_PAIR.findall(text):
                    if CJK.search(zh):
                        pairs[(en.strip().lower(), zh.strip())] += 1
    for (en, zh), n in pairs.items():
        counts[en] += n
    # one official term per english keyword: the most frequent rendering wins, ties are reported
    best: dict[str, list[tuple[str, int]]] = {}
    for (en, zh), n in pairs.items():
        best.setdefault(en, []).append((zh, n))
    lines, contested = [], []
    for en, opts in sorted(best.items(), key=lambda kv: -sum(n for _, n in kv[1])):
        opts.sort(key=lambda o: -o[1])
        if len(opts) > 1 and opts[0][1] == opts[1][1]:
            contested.append((en, opts))
        lines.append((en, opts[0][0], opts[0][1], len(opts)))
    path = os.path.join(args.out, "terms.tsv")
    with open(path, "w", encoding="utf-8") as f:
        f.write("english\tofficial_zh\tseen\tvariants\n")
        for en, zh, n, v in lines:
            f.write(f"{en}\t{zh}\t{n}\t{v}\n")
    print(f"抽到 {len(lines)} 个官方术语对(多次出现的前 {25}):", file=sys.stderr)
    for en, zh, n, v in lines[:25]:
        print(f"   {en:22} {zh}   ({n} 次,{v} 种写法)", file=sys.stderr)
    if contested:
        print(f"写法有并列、需要人工定的 {len(contested)} 个:"
              f" {[e for e, _ in contested][:12]}", file=sys.stderr)
    print(f"-> {path}", file=sys.stderr)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--index", required=True, help="…/Bundles2/_.index.bin 或 Content.ggpk 的 index")
    ap.add_argument("--pscli", default="dist/cli-linux-x64/pscli", help="pscli 可执行文件")
    ap.add_argument("--work", default="/tmp/pszh/data", help="第三方数据的独立目录")
    ap.add_argument("--out", default="/tmp/pszh/out", help="产出目录(不进仓库)")
    ap.add_argument("--pack", default=os.path.expanduser("~/.local/share/poe2-smoother/rulepacks/default.json"),
                    help="规则包,用来取实际存在的类别 Id 与前缀频率")
    ap.add_argument("--table", default="src/PsRules/Labels/labels-zh.json",
                    help="现有官方词表,用来标记新增/改写")
    ap.add_argument("--language", default="traditional chinese",
                    help="覆盖表目录名;装了简体中文语言包后写 'simplified chinese'")
    ap.add_argument("commands", nargs="*", default=["all"],
                    help="names / terms / areas / all")
    args = ap.parse_args()

    os.makedirs(args.work, exist_ok=True)
    os.makedirs(args.out, exist_ok=True)
    if not os.path.exists(args.pack):
        print(f"error: 规则包不存在:{args.pack}(先跑 build-rules,或 --pack 指一份)", file=sys.stderr)
        return 2
    pack = json.load(open(args.pack, encoding="utf-8"))
    our_ids = [c["Id"] for c in pack["Categories"] if "." in c["Id"]]
    groups = collections.Counter(cid.split(".", 1)[0] for cid in our_ids)

    todo = args.commands or ["all"]
    if "all" in todo:
        todo = ["names", "terms", "areas"]
    for cmd in todo:
        if cmd == "names":
            cmd_names(args, args.index, args.pscli, args.work, our_ids, groups)
        elif cmd == "areas":
            cmd_areas(args, args.index, args.pscli, args.work)
        elif cmd == "terms":
            cmd_terms(args, args.index, args.pscli, args.work)
        else:
            print(f"error: 未知命令 {cmd}", file=sys.stderr)
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
