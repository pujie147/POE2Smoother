# PoE2 Smoother — a PoE2 effect-suppression tool for SteamOS and Windows

A Path of Exile 2 effect remover for SteamOS / Linux handheld *and* Windows players.
It strips out the flashy skill effects, league effects and scene particles in your client,
and gives the framerate and the thermals back to what actually matters — the gameplay.

[中文](README.md) · **English (current)** · these two files are translations of each other; edit both

## Features

- Locates PoE2 automatically: multiple Steam libraries, SD-card installs, the Windows registry and conventional install folders — both the **Steam** version and the **standalone client's `Content.ggpk`** (experimental)
- Effects are grouped by category — **skills / appearance / monsters / leagues (Abyss, Delirium, Splinter… 9 groups) / environment / other** — searchable by name, ID or path, whole groups toggle in one click
- Four presets **Safe / Clean / Performance / Full**, plus free category combinations, an editable ignore list, and saved profiles
- Apply, status check, game-update detection, one-click restore; the original index is backed up before every apply
- Bilingual UI (中文 / English), large text, gamepad/keyboard operable, tuned for the Steam Deck / Ally 1280×800
- Touches local client files only: no memory injection, no reading or writing the game process

## Download

Prebuilt AppImages and portable Windows ZIPs are attached to the
[Releases](https://github.com/pujie147/mySmoother2/releases) page; the
[latest release](https://github.com/pujie147/mySmoother2/releases/latest) always points at the newest
stable tag. Every release is built automatically by GitHub Actions from a `v*` tag
(`.github/workflows/release.yml`: run the `scripts/test-all.sh` gate → publish GUI/CLI → pack the AppImage
and the Windows ZIPs → attach everything to the release). The assets are always named
**`PoE2Smoother-x86_64.AppImage`**, **`PoE2Smoother-win-x64.zip`** and **`pscli-win-x64.zip`**, so these
links stay valid across versions:

```bash
curl -LO https://github.com/pujie147/mySmoother2/releases/latest/download/PoE2Smoother-x86_64.AppImage
```

```powershell
# Windows: unzip, then start poe2smoother.exe
curl.exe -LO https://github.com/pujie147/mySmoother2/releases/latest/download/PoE2Smoother-win-x64.zip
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

If auto-detection fails, append `--index <.../Bundles2/_.index.bin>`, `--game <game root>` or
`--ggpk <.../Content.ggpk>` to any command (on Windows, use `pscli.exe`).

## How it works: what actually changes

- Steam client: the tool only rewrites `Bundles2/_.index.bin` and appends the replaced assets to its own custom bundle directory (`Bundles2/LibGGPK3/`, built on LibGGPK3). **Not a byte of the original game files is touched.** Restore: if nothing external changed the index, it simply swaps the backup back and deletes the custom content; if the index has changed (a game update), it decides **file by file** — only files whose content still equals what this tool wrote get their records pointed back at the original bundle, and files changed by the update are skipped, so the game is never downgraded (`--force` still restores the whole backup over it).
- Standalone client (experimental): the index lives *inside* `Content.ggpk`, so writes modify the container in place with no atomic commit. The backup therefore exports only the inner index (tens of MB, not the whole ~40 GB container). Don't kill the process or cut power while applying; if it does get interrupted, run "Restore" again to write the backed-up index back. The per-file restore logic is identical to the Steam path.
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
(experimental); the experimental empty-out strategy is off by default; neither Windows nor Linux artifacts
are code-signed. Check the license terms of LibGGPK3 / LibBundledGGPK3 (AGPL-3.0-or-later) before
redistributing or commercial use.
