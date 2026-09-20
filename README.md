<p align="center">
    <img src="https://github.com/lacedsc3ne/PhasmaStrap/raw/main/Images/PhasmaStrap-full-dark.png#gh-dark-mode-only" width="380">
    <img src="https://github.com/lacedsc3ne/PhasmaStrap/raw/main/Images/PhasmaStrap-full-light.png#gh-light-mode-only" width="380">
</p>

<div align="center">

[![Version][shield-repo-latest]][repo-latest]
[![Downloads][shield-repo-releases]][repo-releases]
[![License][shield-repo-license]][repo-license]

**[phasmastrap.com][website]** · [Download][website-download] · [Changelog][website-changelog] · [Discord][discord]

</div>

---

A replacement launcher for Roblox on Windows, built on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap). It starts Roblox for you and applies your settings on the way: FastFlags per game, overlays, capture, mods and more.

## Features

**Capture**
- Instant Replay: one key saves the last minutes of your game, up to five, in 1080p or 1440p
- Screenshots of the whole game or just an area you drag over
- Editors for clips and screenshots, plus a GIF maker
- Every capture in one window with thumbnails, search and rename

**In game**
- FPS overlay you can place and style, with ping and system stats
- Crosshair editor with presets
- Stream-safe mode: OBS gets its own view with chat and names hidden
- In-game chat overlay

**Setup per game**
- FastFlag profiles that switch when you join
- In-game resolution per monitor or per game
- Low-end mode for weaker PCs, with an undo

**Around the game**
- Server browser sorted by ping, private-server filters, and a matchmaker
- Discord Rich Presence with a Join button
- Account switching, play history and playtime per game
- NVIDIA driver settings for Roblox, RiShade post-processing, anti-aliasing and frame generation
- Classic client hosting for legacy Roblox builds

## Installing

1. Install the [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0) if you don't have it.
2. Download the [latest release][repo-latest] and run it.
3. Press Play on the Roblox website. It opens through PhasmaStrap from then on.

Windows SmartScreen shows a warning the first time, because these builds aren't code-signed. Click "More info", then "Run anyway". Every release lists its SHA-256 on the [download page][website-download] if you want to check the file.

PhasmaStrap keeps itself up to date after that. Questions are answered in the [FAQ][website-faq], and anything else on [Discord][discord].

## Building

```
git clone --recursive https://github.com/lacedsc3ne/PhasmaStrap.git
cd Phasma
dotnet build PhasmaStrap/PhasmaStrap.csproj -c Release
```

Needs the .NET 6 SDK on Windows. `PhasmaStrap.Server` is a separate program that hosts legacy Roblox clients locally and is only used by the classic client feature.

## Credits

Built on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap), with features ported from Voidstrap. The interface uses [WPF UI](https://github.com/lepoco/wpfui). Thanks to everyone who worked on them.

PhasmaStrap is not affiliated with Roblox Corporation. See [LICENSE][repo-license].

[shield-repo-license]:  https://img.shields.io/github/license/lacedsc3ne/PhasmaStrap
[shield-repo-releases]: https://img.shields.io/github/downloads/lacedsc3ne/PhasmaStrap/total?color=981bfe
[shield-repo-latest]:   https://img.shields.io/github/v/release/lacedsc3ne/PhasmaStrap?color=7a39fb

[repo-license]:  https://github.com/lacedsc3ne/PhasmaStrap/blob/main/LICENSE
[repo-releases]: https://github.com/lacedsc3ne/PhasmaStrap/releases
[repo-latest]:   https://github.com/lacedsc3ne/PhasmaStrap/releases/latest

[website]:           https://phasmastrap.com
[website-download]:  https://phasmastrap.com/download
[website-changelog]: https://phasmastrap.com/changelog
[website-faq]:       https://phasmastrap.com/faq
[discord]:           https://discord.gg/x4M4cZS4p7
