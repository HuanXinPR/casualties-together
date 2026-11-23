<div align="center">

English | [简体中文](README.zh-CN.md)

<a href="https://www.nexusmods.com/scavprototype/mods/67">
<img alt="Casualties: Together" src="https://github.com/creaturefeaturelarry/casualties-together/raw/refs/heads/main/.github/ct6.png">
</a>

[![Latest Release](https://img.shields.io/github/v/release/HuanXinPR/casualties-together?style=flat-square&include_prereleases&logo=github&label=Release)](https://github.com/HuanXinPR/casualties-together/releases)
[![Build and Release](https://img.shields.io/github/actions/workflow/status/HuanXinPR/casualties-together/build-and-release.yml?branch=main&style=flat-square&logo=github&label=Build)](https://github.com/HuanXinPR/casualties-together/actions/workflows/build-and-release.yml)
[![Upstream](https://img.shields.io/badge/Upstream-creaturefeaturelarry%2Fcasualties--together-blue?style=flat-square&logo=github)](https://github.com/creaturefeaturelarry/casualties-together)
[![Game Version](https://img.shields.io/badge/Game_Version-v1.0.2-darkgreen?style=flat-square&logo=steam)](https://store.steampowered.com/app/4576510)

</div>

### Online Multiplayer Co-op Mod for ["Casualties: Unknown"](https://store.steampowered.com/app/4576510) (Patched Build)

[![Casualties: Together](https://img.shields.io/badge/Casualties%3A%20Together%20Mod%20Discord-%235865F2.svg?logo=discord&logoColor=white)](https://discord.gg/SA6H7mA6De)
[![Target Planet](https://img.shields.io/badge/Official%20Casualties%3A%20Unknown%20Discord-%235865F2.svg?logo=discord&logoColor=white)](https://discord.com/channels/955738554129063947/1470206122844618802)

> **About This Repository**: This patched fork tracks upstream [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together) (`v5.5.5` wire protocol compatible), decouples hard dependencies on `CUCoreLib` / `Skins`, integrates stability, sync, Unicode nickname/font fallback, and localization fixes, and provides zero-secret GitHub Actions automated builds.
>
> Support the upstream developer: [Buy Me A Coffee](https://buymeacoffee.com/casualtiestogether)

---

## Install Guide

### Requirements

- Game: [Casualties: Unknown](https://store.steampowered.com/app/4576510) (`v1.0.2`)
- Mod Loader: [BepInEx 5.4.23.5 (win_x64)](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip)
- Mod Package: Download `CasualtiesTogether-v*.zip` (e.g. `CasualtiesTogether-v5.5.5.zip`) from [GitHub Releases](https://github.com/HuanXinPR/casualties-together/releases)

### Steps

1. Open the game root directory in Steam (`Right-click Casualties: Unknown > Manage > Browse local files`).
2. Extract `BepInEx_win_x64_5.4.23.5.zip` directly into the game root directory.
3. Extract `CasualtiesTogether-v*.zip` directly into the game root directory (the archive top-level path is already `BepInEx/plugins/Together/`, so it merges straight into `BepInEx`).
4. Verify your directory layout looks like this:

```text
Casualties Unknown/
├── CasualtiesUnknown.exe
├── CasualtiesUnknown_Data/
├── winhttp.dll                       <- from BepInEx zip
├── doorstop_config.ini               <- from BepInEx zip
└── BepInEx/
    ├── core/                         <- from BepInEx zip
    └── plugins/
        └── Together/                 <- from CasualtiesTogether-v*.zip
            ├── CasualtiesMP.dll
            ├── K4os.Compression.LZ4.dll
            ├── LiteEntitySystem.dll
            ├── LiteNetLib.dll
            ├── OpusSharp.Core.dll
            ├── opus.dll
            ├── RefMagic.dll
            └── System.Runtime.CompilerServices.Unsafe.dll
```

5. Launch the game **through Steam**. Multiplayer buttons will appear on the main menu.
   - To play singleplayer temporarily without uninstalling files, click `Deactivate MP Mod` under `Settings > General` (or launch with `--mp-disable`).
   - **Linux / Proton**: Add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to Steam Launch Options so Proton loads BepInEx.

---

## Release Assets & Nightly Builds

Each release on [GitHub Releases](https://github.com/HuanXinPR/casualties-together/releases) provides four build artifacts plus a `SHA256SUMS.txt` checksum manifest:

| Asset | Purpose |
| --- | --- |
| `CasualtiesTogether-v<Version>.zip` | **Standard Full Package** (`BepInEx/plugins/Together/` with Release `CasualtiesMP.dll` + 7 runtime dependency DLLs). Extract directly into the game root for fresh installs or full updates. |
| `CasualtiesMP.dll` | **Standalone Release DLL**. If you already installed the full package and runtime libraries, replace `BepInEx/plugins/Together/CasualtiesMP.dll` for quick updates. |
| `CasualtiesTogether-v<Version>+debug.zip` | **Debug Full Package**. Includes `DEBUG`/`TESTING` diagnostics and portable `CasualtiesMP.pdb` symbols (9 files total) for crash stack traces and sync troubleshooting. |
| `CasualtiesMP+debug.dll` | **Standalone Debug DLL**. Rename to `CasualtiesMP.dll` inside `BepInEx/plugins/Together/` when switching an existing install to a debug build. |
| `v<Version>-nightly.<YYYYMMDD>` *(Pre-release)* | **Daily Nightly Build**. Automatically published once per day when `main` receives new commits since the last release or nightly tag. |

---

## Lobby File Hash Check (`Verify Game Hash`)

During connection handshake, the host checks `Verify Game Hash` (enabled by default in server settings), which compares a combined binary hash of `Assembly-CSharp.dll` and `CasualtiesMP.dll`:

- Builds from this repository stay wire-compatible with upstream `v5.5.5` (`1.0.2_MPv5.5.5`), so packet formats and game rules match.
- Because `CasualtiesMP.dll` binaries differ between upstream releases, this patched fork, `+debug` builds, and `nightly` builds, their file hashes will not match.
- **When all players install the exact same release package**, you can leave `Verify Game Hash` enabled.
- **When playing across different builds of the same protocol version** (for example mixing this patched build with upstream `v5.5.5`, or mixing Release with `+debug`), the host should uncheck **`Verify Game Hash`** in the server configuration panel so clients are not rejected with `Game file hashes do not match`.

---

## Warnings

- Launch the game from your Steam Library if Steam Lobbies fail to initialize.
- A stable network connection is recommended; the mod has no client-side prediction, network ownership, deterministic RNG, or anti-cheat.
- If you encounter glitches or crashes while modded, report them to the mod repository/Discord rather than the base game developer.

---

## Features

- Steam Lobbies and direct IPv4/IPv6 hosting, plus Lobby Host Transfer
- Proximity and radio voice chat / text chat with in-game speech & hearing distortions
- Middle mouse button location ping (double-click for alert)
- Right-click player interaction menu: inspect wounds/health, perform medical actions & CPR, trade/take inventory items, feed/use items, push, carry, and piggyback
- Shared body warmth and mood buffs near teammates
- Spectator mode on death, trader revival, and layer-transition respawn (unless `Permadeath` is enabled)
- Individual `Unchipped` setting, MP3 player boombox streaming, and dedicated server mode (`FOR-NERDS.txt`)

### Console Commands

Host can unlock cheat/rule commands in the console:

```text
rule sv_cheats 1
```

Host modifies rules with `rule <rule_name> <value>`. Clients can be granted remote server console access via `adminpriv <player>` or `adminpriv-password`.

---

## Building from Source

### Zero-Secret Cloud / Stub Build (No Local Game Required)

The repository tracks stripped metadata reference stubs under `references/*.dll` (all method body IL implementations and embedded resources removed via `BepInEx.AssemblyPublicizer`, keeping type/member signatures intact) alongside the 7 runtime libraries under `libs/dlls/`. No Steam installation or CI secret is needed to compile or package:

```powershell
# Compile Fullgame Release configuration
dotnet build CasualtiesTogether.sln -c ReleaseFullgame

# Compile and package all 5 release assets into build/release-assets
pwsh -NoProfile -File ./scripts/package-release.ps1
```

### Local Game Development & Refreshing Reference Stubs

- If a local game installation exists (configured via `CASUALTIESTOGETHER_GAME_PATH`, `CasualtiesTogether.Local.props`, `-p:BaseGamePath=...`, or a sibling `CU_Fullgame` folder), `Directory.Build.props` resolves the real `CasualtiesUnknown_Data/Managed` directory and automatically copies build outputs into `<GameDir>/BepInEx/plugins/Together/` on post-build.
- You can also run `python scripts/prepare_workspace.py` to set up a local test copy with BepInEx.
- After a base game update, regenerate the stripped metadata stubs from your local Steam installation using PowerShell 7:

```powershell
pwsh -NoProfile -File ./scripts/generate-ref-stubs.ps1 -GamePath "C:\Path\To\Casualties Unknown"
```

---

## Credits

Credits are listed in the "About" menu inside the mod.

- [Creature](https://github.com/creaturefeaturelarry) — Upstream maintainer
- [Krokosha666](https://github.com/Krokosha666) — Original developer (retired)
- [Dune](https://github.com/Jyan-sei) — Sync, optimizations
- [VtLoader](https://github.com/vtloader) — Bug fixes, locale
- [Gary the Cat](https://github.com/Garythecat89), [Dannad](https://github.com/danxnader), [Meow](https://github.com/SprightlyHawk8), [Jimmyking](https://github.com/jimmyking9999999), [Kidihh](https://github.com/Kidihh), [Dawn](https://github.com/hehdawn), [mayzmae](https://github.com/mayzmae), ItsVoidSK, and all playtesters & supporters

## License

LGPL-2.1, see [LICENSE](LICENSE).
