# PoE2 Smoother — a PoE2 effect-suppression tool for SteamOS and Windows

A Path of Exile 2 effect remover for SteamOS / Linux handheld *and* Windows players.
It strips out the flashy skill effects, league effects and scene particles in your client,
and gives the framerate and the thermals back to what actually matters — the gameplay.

[中文](README.md) · **English (current)** · these two files are translations of each other; edit both

## Features

- Locates PoE2 automatically: multiple Steam libraries, SD-card installs, the Windows registry and conventional install folders — both the **Steam** version and the **standalone client's `Content.ggpk`** (experimental)
- Effects are grouped by category — **skills / appearance / monsters / leagues (Abyss, Delirium, Splinter… 9 groups) / environment / other** — each row labelled in both Chinese and English, searchable by name (either language), ID or path, whole groups toggle in one click
  - Chinese names come from **your own client's official name tables** (ActiveSkills etc., copied verbatim — see [`docs/zh-labels.md`](docs/zh-labels.md)). This machine's client only has the Traditional overlay, so the column currently mixes Traditional (official) and Simplified (hand-written); downloading the Simplified Chinese language for PoE2 in Steam and re-running the extraction unifies it. Skills with no table entry show as 「技能: original name」 — a row is never English-only
- Four presets **Safe / Clean / Performance / Full**, plus free category combinations, an editable ignore list, and saved profiles
- Apply, status check, game-update detection, one-click restore; the original index is backed up before every apply
- **Render compression (experimental, off by default)**: another path that keeps visuals but lowers render load — same-resolution BC7→BC1 textures, official mesh LOD swap, shader simplification, mesh decimation (strength slider), plus an aggressive downscale option; mutually exclusive with blanking, touches only `.dds`/`.fxgraph`/`.fmt`, never `.pet` configs
- Bilingual UI (中文 / English), large text, gamepad/keyboard operable, tuned for the Steam Deck / Ally 1280×800
- Touches local client files only: no memory injection, no reading or writing the game process

## Download

Prebuilt AppImages and portable Windows ZIPs are attached to the
[Releases](https://github.com/pujie147/POE2Smoother/releases) page; the
[latest release](https://github.com/pujie147/POE2Smoother/releases/latest) always points at the newest
stable tag. Every release is built automatically by GitHub Actions from a `v*` tag
(`.github/workflows/release.yml`: run the `scripts/test-all.sh` gate → publish GUI/CLI → pack the AppImage
and the Windows ZIPs → attach everything to the release). The assets are always named
**`PoE2Smoother-x86_64.AppImage`**, **`PoE2Smoother-win-x64.zip`** and **`pscli-win-x64.zip`**, so these
links stay valid across versions:

```bash
curl -LO https://github.com/pujie147/POE2Smoother/releases/latest/download/PoE2Smoother-x86_64.AppImage
```

```powershell
# Windows: unzip, then start poe2smoother.exe
curl.exe -LO https://github.com/pujie147/POE2Smoother/releases/latest/download/PoE2Smoother-win-x64.zip
Expand-Archive .\PoE2Smoother-win-x64.zip .
.\PoE2Smoother\poe2smoother.exe
```

Check the download against the `SHA-256:` line in the release body (GitHub drops the executable bit on
download, so on Linux you still need `chmod +x` — see the next section).

## Quick start: suppressing effects in the GUI (recommended)

Both the **Steam** version and the **standalone client (`Content.ggpk`, experimental)** are supported. On
Linux all of this happens in SteamOS **desktop mode**; on Windows, unzip the ZIP and run `poe2smoother.exe`
(if SmartScreen blocks the first run, choose "More info → Run anyway"). When the game lives under
`Program Files`, **apply/restore** need administrator rights — the UI says so when a write is denied and
offers "Run as admin" in the top-right corner.

1. **Start the tool** — make the AppImage executable and run it:

   ```bash
   chmod +x PoE2Smoother-x86_64.AppImage
   ./PoE2Smoother-x86_64.AppImage
   ```

   If it fails to start because FUSE2 is missing, run it in extract-and-run mode instead:

   ```bash
   APPIMAGE_EXTRACT_AND_RUN=1 ./PoE2Smoother-x86_64.AppImage
   ```

2. **Home → Detect**: finds the game directory automatically and shows where `Bundles2/_.index.bin` or `Content.ggpk` lives, labelled with its type (Steam / standalone). If it can't, pick the file with **Browse**, or point it there with the `POE2_INDEX` / `POE2_GGPK` / `POE2_ROOT` environment variables (never run detection as sudo / administrator).
3. **Build rules**: scans the index of **your own current client** to produce the list of suppressible effects — the rule pack does not depend on anyone's fixed list, so regenerating it once after a game update is enough.
4. **Effects tab → pick a preset**: the preset selects **categories** (which skills, leagues, environment), so newcomers should use `safe` (the default), which only touches player skill and appearance effects and keeps every monster, boss and dangerous-mechanic effect.
   Going further on render load is what the two drop-downs in the same row do — **Risk tier** and **Write strategy**. Two recommended combinations:

   - **`safe` + Risk tier "+ blank effects (.pet/.epk/.fxgraph)" + Write strategy "valid empty resource"** — the first choice. What gets blanked is the effect's own
     `.pet` / `.epk` / `.fxgraph`, and effect drawing is where the render overhead actually is: once those files become a minimal valid empty resource they stop producing draws,
     which should save more frames than script surgery alone. Ground, characters and UI still render normally, so the **gain has a ceiling**, and it has not been verified in game yet.
     Note the Risk tier is a **ceiling, not a switch** — at this tier the `.ao` surgery from "Safe: script surgery only" is already included, there is nothing to enable twice.
   - **`safe` + Risk tier "+ blank materials (.mat)" + Write strategy "valid empty resource"** — on top of the above, this also blanks over 50,000 `.mat` files,
     so the volume is larger. Materials are shared with ordinary rendering (ground, characters, objects), so **normal geometry can black out too**; the frame-rate delta is usually
     smaller than the previous option while the risk is genuinely one tier higher: the UI asks for a second confirmation and lists the original bytes that will be discarded.
   - Keep "valid empty resource" for both (it is already the default) and don't go back to "0-byte file": the legal empty shapes are taken from the client's own same-shaped
     files, so "the blanked file is harmless" does not depend on the one thing we cannot know — who else references it.
   - The way back is **Home → Restore scope**: "Experimental only" or "Materials only" keeps the script surgery in place.

   > ⚠ **Neither combination gets you render compression.** While the write strategy is "valid empty resource" / "0-byte file", the "Render compression" panel does not appear
   > on the Effects tab at all; while the risk tier is "Safe: script surgery only", every compression target is skipped by design even with the panel open and all switches on (it counts under `skippedExp` in `plan`).
   > To take the compression path instead, switch the write strategy to "Compress, else valid empty" or "Render-compress only" and raise the risk tier to match
   > (texture compression is material-tier, mesh and shader compression is fx-tier); until you do, the two combinations above are the best available.
5. **(Optional tuning)**: search which group an effect belongs to, tick or untick whole groups, add paths you never want touched to the ignore list, and save the selection as a profile to load next time.
6. **Home → Apply**: backs up first, then writes (the button requires a double-click to confirm). The matching effects disappear in game. If it reports "0 files changed", the tool tells you why (stale rule pack, everything ignored, current risk tier excludes those assets…) and what to do next.
7. **Verify in game**. To get the full visuals back: **Home → Restore** (also double-click to confirm) — everything comes back from the backup, and you can re-apply at any time.

## Equivalent command-line workflow

```bash
./pscli detect                      # locate the game
./pscli build-rules                 # build a rule pack from the current index
./pscli plan   --preset safe        # preview how many files would change (writes nothing)
./pscli apply  --preset safe --yes  # apply (backs up first; = risk tier "Safe: script surgery only")
./pscli status                      # current status
./pscli restore --yes               # restore everything

# the two recommended combinations above (--blank-style template is already the default; spelled out for clarity)
./pscli apply  --preset safe --experimental fx       --blank-style template --yes
./pscli apply  --preset safe --experimental material --blank-style template --yes
```

If auto-detection fails, append `--index <.../Bundles2/_.index.bin>`, `--game <game root>` or
`--ggpk <.../Content.ggpk>` to any command (on Windows, use `pscli.exe`).

## How it works: what actually changes

- Steam client: the tool only rewrites `Bundles2/_.index.bin` and appends the replaced assets to its own custom bundle directory (`Bundles2/LibGGPK3/`, built on LibGGPK3). **Not a byte of the original game files is touched.** Restore: if nothing external changed the index, it simply swaps the backup back and deletes the custom content; if the index has changed (a game update), it decides **file by file** — only files whose content still equals what this tool wrote get their records pointed back at the original bundle, and files changed by the update are skipped, so the game is never downgraded (`--force` still restores the whole backup over it).
- Standalone client (experimental): the index lives *inside* `Content.ggpk`, so writes modify the container in place with no atomic commit. The backup therefore exports only the inner index (tens of MB, not the whole ~40 GB container). Don't kill the process or cut power while applying; if it does get interrupted, run "Restore" again to write the backed-up index back. The per-file restore logic is identical to the Steam path.
- `.ao` files are PoE2's effect-attaching scripts: the tool removes the `AddAttached` / `AddEffectPack` / `RemoveEffectPack` calls inside them, so effects stop attaching to skills and objects. It's "script surgery" — the lowest-risk strategy, and the bulk of what `safe` does.
- `.dds` textures: replaced with a 1×1 transparent texture.
- Emptying `.pet` / `.epk` / `.fxgraph` / `.aoc` (effect resources) and `.mat` (materials) outright is an
  **experimental** strategy, off by default, and split into two separate tiers: `fx` blanks only effect
  resources, `material` is the one that touches materials — those are shared with ordinary rendering
  (ground, characters, objects), so blanking them can black out normal geometry too. Keep the scale in mind:
  one measured `safe` + all-experimental run on this machine touched **73,581 files**
  (`.mat` 50,399 / `.pet` 15,556 / `.epk` 5,054 / `.fxgraph` 2,572). A blanked file is replaced with the
  **minimal valid empty resource of its format** (2–42 bytes, shaped after the client's own
  `empty.epk` / `00_null.pet` / `empty.mat`, not invented), rather than 0 bytes — that way "the blanked file
  is harmless" does not depend on the one thing we cannot know: who else references it. The old 0-byte
  behaviour is still reachable via the "write strategy" box / `--blank-style empty`. In the GUI the tier is a
  drop-down (choosing material asks for a second, in-window confirmation listing the original bytes that
  will be discarded); on the CLI it is `--experimental safe|fx|material`, and the old `--include-experimental` is
  exactly `--experimental material`, so its meaning did not narrow. If a tier turns out to be too much, you
  can undo just the materials and keep the script surgery: the "Restore scope" box on the Home tab, or
  `pscli restore --undo-tiers material`; `pscli health` (read-only) crosses asset-path warnings in the
  client log against the files this run wrote. Details, plus the alternatives that are *not* implemented yet,
  are in [`docs/experimental-risk-tiers.md`](docs/experimental-risk-tiers.md).
- The default ignore list protects monsters, bosses and dangerous-mechanic assets, so the picture actually becomes *easier* to read once suppressed: you can see which ground effects you are standing in.

## Render compression (experimental, off by default)

Besides blanking, the tool offers a path that **keeps visuals while lowering render load**: render
compression. In the Effects tab these are independent toggles (plus a decimation-strength slider and an
aggressive downscale option), **all off by default**; a change requires both the risk tier and the switch.
Once a class is compressed it is no longer blanked (the two are mutually exclusive).

| Toggle | Effect | Tier | Notes |
|---|---|---|---|
| Texture same-res | `.dds` BC7→BC1 | material | Resolution & UV unchanged, ~-50%; only opaque/cutout, **smooth alpha (soft particles) skipped** |
| Downscale (aggressive) | Halve resolution on top of texture compression | material | Soft particles go to BC3 keeping soft edges (-75%), opaque to BC1 (-87%); **changes atlas UV cropping — in-game A/B first** |
| Mesh LOD | `.fmt` official LOD00→LOD1/2 | fx | Replaced with the official lower-detail sibling, safest |
| Mesh decimate | `.fmt` vertex-clustering decimation | fx | Strength slider 0–100; billboards/quads skipped |
| Shader simplify | `.fxgraph` whitelisted graphs → minimal template | fx | Still renders a recognizable effect, highest risk |

CLI equivalents: `--compress-tex` / `--tex-downscale` / `--compress-shader` / `--mesh-lod` / `--mesh-decimate <0-100>`.

Only effect meshes / particle textures / whitelisted shaders are processed; **monsters and gameplay telegraphs
are skipped** (silhouette and motion are themselves direction information), and **`.pet` configs are never
modified** (they affect skill range/hit detection). All writes reuse the existing backup/verify/restore chain
with zero-change restore. The texture codecs are verified byte-for-byte offline (BC7 decode against an
independent Python port matches on 26 real samples / 246 mips), but **"the client accepts it + visuals stay
recognizable + skill range is unchanged" still needs in-game A/B before promotion** — steps in
[`docs/experimental-risk-tiers.md`](docs/experimental-risk-tiers.md) §7.3.

## Preset cheat sheet

| Preset | Covers |
|---|---|
| `safe` (default) | player skills + appearance effects; monsters, leagues and environment all kept |
| `clean` | skills + appearance + environment + league effects |
| `performance` | league + environment effects (all skill effects kept) |
| `full` | everything in scope (except the ignore list) |

## Notes / FAQ

- **Close the game before applying or restoring**: the tool refuses to run while a PoE2 process is alive (`--force` if you really must).
- **A denied write on Windows**: Steam and the standalone client install under `Program Files` by default, which a normal user can't write to. Click "Run as admin" in the top-right corner, or run `pscli.exe` from an elevated terminal. Read-only steps (detect, build rules) never need elevation.
- **After a game update**, pressing "Restore" directly is safe: the tool compares file by file, reverts only what it suppressed itself, keeps updated files at their new version and never overwrites them with an old backup. Then just "build rules → apply" again.
- Steam's "verify integrity of game files" wipes the patch without harming the game — apply once more and you're done.
- **The standalone client (`Content.ggpk`) is still experimental**: writes are in-place with no atomic commit (see the previous section). Try it on a copy of `Content.ggpk` first.
- **Will I get banned?** The tool only modifies local client files; it does not inject memory or read process data. Client modification still carries inherent risk, so judge for yourself on an account you care about — `safe` plus a kept backup is the sensible choice.
- Suppression granularity is per category ("fire skills", "Delirium league"…) plus path-prefix tweaks in the ignore list; per-skill selection is not supported yet.

> You use this tool at your own risk. This project does not reuse PoeSmoother's licensed rule data.

## Sponsorship & thanks

Thanks to everyone who sponsors, reports bugs and helps test — your support is the entire reason this tool keeps getting updated.

If it saved your framerate in a late-game map, coffee via Alipay is welcome, and it keeps the version adaptations and new features coming:

![Alipay QR code](docs/1794293378652.jpg)

## Source & building

```text
src/PsCore/     game location, index session (Steam / Content.ggpk), backup/status, apply/restore
src/PsRules/    rule pack model, classifier, presets, ignore list, replacement strategies
src/PsCli/      command-line pscli
src/PsApp/      Avalonia GUI
tests/          core tests + headless GUI smoke test
packaging/      .desktop entry, icon, fallback runtime libs
scripts/        build, AppImage, Windows ZIPs, combined test runner
```

Requires .NET SDK 8 (`net8.0`, self-contained Linux / Windows x64 publish):

```bash
bash scripts/build-linux.sh        # GUI + CLI + tar.gz
bash scripts/build-appimage.sh     # AppImage (run under Linux or WSL)
pwsh scripts/build-windows.ps1     # portable Windows ZIPs (native on Windows, cross-published in Linux CI)
bash scripts/test-all.sh           # build + tests + CLI end-to-end (on Windows: powershell -File scripts/test-all.ps1)
```

The version has exactly one source: **the git tag**. On release, CI strips the leading `v` from `v1.1` and
passes it in with `-p:Version=`, which is what both the GUI title and `pscli version` display; a local
build with no tag reports `0.0.0-dev`. To pin a version by hand: `PS_VERSION=1.2.3 bash scripts/build-linux.sh`.

Known limitations: two client layouts are supported — the Steam version and the standalone `Content.ggpk`
(experimental); the experimental empty-out strategy is off by default (the material tier is the riskiest of
them), and "a valid empty resource really is harmless to rendering and loading" currently rests on the
client shipping same-shaped files, not on a verified in-game run — `pscli health` exists for exactly that
(see `docs/experimental-risk-tiers.md`); neither Windows nor Linux artifacts
are code-signed. Check the license terms of LibGGPK3 / LibBundledGGPK3 (AGPL-3.0-or-later) before
redistributing or commercial use.
