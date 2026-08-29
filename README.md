<p align="center">
    <img src="https://github.com/lacedsc3ne/Phasma/raw/main/Images/PhasmaStrap-full-dark.png#gh-dark-mode-only" width="380">
    <img src="https://github.com/lacedsc3ne/Phasma/raw/main/Images/PhasmaStrap-full-light.png#gh-light-mode-only" width="380">
</p>

<div align="center">

[![License][shield-repo-license]][repo-license]
[![GitHub Workflow Status][shield-repo-workflow]][repo-actions]
[![Downloads][shield-repo-releases]][repo-releases]
[![Version][shield-repo-latest]][repo-latest]

</div>

----

PhasmaStrap is a third-party replacement for the standard Roblox bootstrapper, built as a fork of [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap) with a large number of additional features ported in and built from scratch - server matchmaking, an expanded engine settings panel, NVIDIA driver profile control, GPU overlays (RiShade shader post-processing, anti-aliasing, frame generation), an in-game chat overlay, classic client hosting, and more.

PhasmaStrap is only supported for PCs running Windows.

## Frequently Asked Questions

**Q: Is this malware?**

**A:** No. The source code here is viewable to all. Only download PhasmaStrap from this GitHub repository's [Releases page][repo-releases] - nowhere else.

**Q: Can using this get me banned?**

**A:** Most of PhasmaStrap's features (Discord Rich Presence, modding, FastFlags, appearance/performance tweaks) work the same way Bloxstrap's do and carry the same low risk. A few features in this fork are more involved - server matchmaking makes real join-attempt calls using your Roblox session, and the classic client / private server feature redirects `roblox.com` itself via your hosts file. Both are off by default and clearly labelled in Settings; read what a feature does before turning it on.

## Features

Ported/inherited from Bloxstrap:
- Hassle-free Discord Rich Presence to let your friends know what you're playing at a glance
- Simple support for modding of content files for customizability (death sound, mouse cursor, etc)
- See where your server is geographically located
- Ability to configure graphics fidelity and UI experience via an expanded FastFlags panel

Built for PhasmaStrap:
- **Server matchmaker** - probes a game's public servers and joins whichever is estimated to have the lowest ping, with per-datacenter exclusion
- **NVIDIA panel** - driver-level frame limiting, DLSS/Reflex, anti-aliasing and image-quality controls for Roblox specifically, via NVAPI
- **GPU overlays** - RiShade (screen-space shader post-processing: color grade, tonemap, bloom, sharpen, and more), Anti-Aliasing (FXAA/SMAA/DLAA/TSAA and others), and Frame Generation (optical-flow frame interpolation), all sharing one compositor
- **In-game chat overlay** - an optional overlay chat window with local commands, gated behind an explicit opt-in since it uses a system-wide keyboard hook
- **Classic client hosting** - launches legacy Roblox client builds against a locally-hosted server, off by default
- **Settings search**, pinnable nav shortcuts, controller navigation for the settings window
- **Play history**, a live output console, Roblox Studio theme sync, and a Rojo installer/launcher built into Extensions
- Custom cursor packs, per-game Discord Rich Presence templates, and a Roblox update-day heatmap

## Installing

Download the [latest release][repo-latest] and run it. Configure your preferences if needed, and install.

You will also need the [.NET 6 Desktop Runtime](https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=x64&rid=win11-x64&apphost_version=6.0.36&gui=true). If you don't already have it installed, you'll be prompted to install it anyway.

It's not unlikely that Windows SmartScreen will show a popup when you run PhasmaStrap for the first time. This happens because it's an unknown program, not because it's actually detected as being malicious. To dismiss it, click "More info" and then "Run anyway".

Once installed, PhasmaStrap is added to your Start Menu, where you can access the menu and reconfigure your preferences if needed.

## Code

PhasmaStrap is built on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap), and a number of features in this fork are ported from [Voidstrap](https://github.com/void-hq/voidstrap). Credit to both projects and their contributors.

PhasmaStrap uses the [WPF UI](https://github.com/lepoco/wpfui) library for the user interface design.

[shield-repo-license]:  https://img.shields.io/github/license/lacedsc3ne/Phasma
[shield-repo-workflow]: https://img.shields.io/github/actions/workflow/status/lacedsc3ne/Phasma/ci-release.yml?branch=main&label=builds
[shield-repo-releases]: https://img.shields.io/github/downloads/lacedsc3ne/Phasma/latest/total?color=981bfe
[shield-repo-latest]:   https://img.shields.io/github/v/release/lacedsc3ne/Phasma?color=7a39fb

[repo-license]:  https://github.com/lacedsc3ne/Phasma/blob/main/LICENSE
[repo-actions]:  https://github.com/lacedsc3ne/Phasma/actions
[repo-releases]: https://github.com/lacedsc3ne/Phasma/releases
[repo-latest]:   https://github.com/lacedsc3ne/Phasma/releases/latest
