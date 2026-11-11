# PoE2 Smoother — a PoE2 effect-suppression tool for SteamOS handhelds

A Path of Exile 2 effect remover for SteamOS / Linux handheld players.
It strips out the flashy skill effects, league effects and scene particles in your client,
and gives the framerate and the thermals back to what actually matters — the gameplay.

[中文](README.md) · **English (current)** · these two files are translations of each other; edit both

## Features

- Locates the **Steam** version of PoE2 automatically (multiple Steam libraries, SD-card installs)
- Effects are grouped by category — **skills / appearance / monsters / leagues (Abyss, Delirium, Splinter… 9 groups) / environment / other** — searchable by name, ID or path, whole groups toggle in one click
- Four presets **Safe / Clean / Performance / Full**, plus free category combinations, an editable ignore list, and saved profiles
- Apply, status check, game-update detection, one-click restore; the original index is backed up before every apply
- Bilingual UI (中文 / English), large text, gamepad/keyboard operable, tuned for the Steam Deck / Ally 1280×800
- Touches local client files only: no memory injection, no reading or writing the game process

## Download

Prebuilt AppImages are attached to the [Releases](https://github.com/pujie147/mySmoother2/releases) page;
the [latest release](https://github.com/pujie147/mySmoother2/releases/latest) always points at the newest
stable tag. Every release is built automatically by GitHub Actions from a `v*` tag
(`.github/workflows/release.yml`: run the `scripts/test-all.sh` gate → publish GUI/CLI → pack the AppImage →
attach it to the release). The asset is always named **`PoE2Smoother-x86_64.AppImage`**, so this command
stays valid across versions:

```bash
curl -LO https://github.com/pujie147/mySmoother2/releases/latest/download/PoE2Smoother-x86_64.AppImage
```

Check the download against the `SHA-256:` line in the release body (GitHub drops the executable bit on
download, so you still need `chmod +x` — see the next section).

## Quick start: suppressing effects in the GUI (recommended)

The **Steam** version of PoE2 only (the standalone client is not supported yet). All of this happens in
SteamOS **desktop mode**.

1. **Start the tool** — make the AppImage executable and run it:

   ```bash
   chmod +x PoE2Smoother-x86_64.AppImage
   ./PoE2Smoother-x86_64.AppImage
   ```

   If it fails to start because FUSE2 is missing, run it in extract-and-run mode instead:

   ```bash
   APPIMAGE_EXTRACT_AND_RUN=1 ./PoE2Smoother-x86_64.AppImage
   ```

2. **Home → Detect**: finds the game directory automatically and shows where `Bundles2/_.index.bin` lives. If it can't, point it there with the `POE2_INDEX` / `POE2_ROOT` environment variables (never with sudo).
3. **Build rules**: scans the index of **your own current client** to produce the list of suppressible effects — the rule pack does not depend on anyone's fixed list, so regenerating it once after a game update is enough.
4. **Effects tab → pick a preset**: newcomers should use `safe` (the default), which only touches player skill and appearance effects and keeps every monster, boss and dangerous-mechanic effect.
5. **(Optional tuning)**: search which group an effect belongs to, tick or untick whole groups, add paths you never want touched to the ignore list, and save the selection as a profile to load next time.
6. **Home → Apply**: backs up first, then writes (the button requires a double-click to confirm). The matching effects disappear in game. If it reports "0 files changed", the tool tells you why (stale rule pack, everything ignored, experimental strategy off…) and what to do next.
7. **Verify in game**. To get the full visuals back: **Home → Restore** (also double-click to confirm) — everything comes back from the backup, and you can re-apply at any time.

## Equivalent command-line workflow

```bash
./pscli detect                      # locate the game
./pscli build-rules                 # build a rule pack from the current index
./pscli plan   --preset safe        # preview how many files would change (writes nothing)
./pscli apply  --preset safe --yes  # apply (backs up first)
./pscli status                      # current status
./pscli restore --yes               # restore everything
```

If auto-detection fails, append `--index <.../Bundles2/_.index.bin>` or `--game <game root>` to any command.

## How it works: what actually changes

- The tool only rewrites the Steam client's `Bundles2/_.index.bin` and appends the replaced assets to its own custom bundle directory (`Bundles2/LibGGPK3/`, built on LibGGPK3). **Not a byte of the original game files is touched.** Restore: if nothing external changed the index, it simply swaps the backup back and deletes the custom content; if the index has changed (a game update), it decides **file by file** — only files whose content still equals what this tool wrote get their records pointed back at the original bundle, and files changed by the update are skipped, so the game is never downgraded (`--force` still restores the whole backup over it).
- `.ao` files are PoE2's effect-attaching scripts: the tool removes the `AddAttached` / `AddEffectPack` / `RemoveEffectPack` calls inside them, so effects stop attaching to skills and objects. It's "script surgery" — the lowest-risk strategy, and the bulk of what `safe` does.
- `.dds` textures: replaced with a 1×1 transparent texture.
- Emptying `.pet` / `.epk` / `.mat` / `.fxgraph` outright is an **experimental** strategy, off by default (needs an explicit tick / `--include-experimental`).
- The default ignore list protects monsters, bosses and dangerous-mechanic assets, so the picture actually becomes *easier* to read once suppressed: you can see which ground effects you are standing in.

## Preset cheat sheet

| Preset | Covers |
|---|---|
| `safe` (default) | player skills + appearance effects; monsters, leagues and environment all kept |
| `clean` | skills + appearance + environment + league effects |
| `performance` | league + environment effects (all skill effects kept) |
| `full` | everything in scope (except the ignore list) |

## Notes / FAQ

- **Close the game before applying or restoring**: the tool refuses to run while a PoE2 process is alive (`--force` if you really must).
- **After a game update**, pressing "Restore" directly is safe: the tool compares file by file, reverts only what it suppressed itself, keeps updated files at their new version and never overwrites them with an old backup. Then just "build rules → apply" again.
- Steam's "verify integrity of game files" wipes the patch without harming the game — apply once more and you're done.
- **Will I get banned?** The tool only modifies local client files; it does not inject memory or read process data. Client modification still carries inherent risk, so judge for yourself on an account you care about — `safe` plus a kept backup is the sensible choice.
- Suppression granularity is per category ("fire skills", "Delirium league"…) plus path-prefix tweaks in the ignore list; per-skill selection is not supported yet.

> You use this tool at your own risk. This project does not reuse PoeSmoother's licensed rule data.

## Sponsorship & thanks

Thanks to everyone who sponsors, reports bugs and helps test — your support is the entire reason this tool keeps getting updated.

If it saved your framerate in a late-game map, coffee via Alipay is welcome, and it keeps the version adaptations and new features coming:

![Alipay QR code](docs/1794293378652.jpg)

## Source & building

```text
src/PsCore/     game location, index session, backup/status, apply/restore
src/PsRules/    rule pack model, classifier, presets, ignore list, replacement strategies
src/PsCli/      command-line pscli
src/PsApp/      Avalonia GUI
tests/          core tests + headless GUI smoke test
packaging/      .desktop entry, icon, fallback runtime libs
scripts/        build, AppImage, combined test runner
```

Requires .NET SDK 8 (`net8.0`, self-contained Linux x64 publish):

```bash
bash scripts/build-linux.sh      # GUI + CLI + tar.gz
bash scripts/build-appimage.sh   # AppImage (run under Linux or WSL)
bash scripts/test-all.sh         # build + tests + CLI end-to-end
```

The version has exactly one source: **the git tag**. On release, CI strips the leading `v` from `v1.1` and
passes it in with `-p:Version=`, which is what both the GUI title and `pscli version` display; a local
build with no tag reports `0.0.0-dev`. To pin a version by hand: `PS_VERSION=1.2.3 bash scripts/build-linux.sh`.

Known limitations: Steam-version `Bundles2/_.index.bin` only (the standalone client's `Content.ggpk` is not
wired up); the experimental empty-out strategy is off by default. Check LibGGPK3's license terms before
redistributing or commercial use.
