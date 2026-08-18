# eq2auras

[![build](https://img.shields.io/github/actions/workflow/status/amdrake93/eq2auras/build.yml?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) [![stable](https://img.shields.io/github/v/release/amdrake93/eq2auras?display_name=release&label=stable&color=009E73)](https://github.com/amdrake93/eq2auras/releases/tag/stable) [![beta](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fapi.github.com%2Frepos%2Famdrake93%2Feq2auras%2Freleases%2Ftags%2Fdev-latest&query=%24.name&label=beta&color=0072B2)](https://github.com/amdrake93/eq2auras/releases/tag/dev-latest) [![license](https://img.shields.io/github/license/amdrake93/eq2auras?color=E69F00)](LICENSE)

[install guide](docs/install.md) · [feature docs (wiki)](https://github.com/amdrake93/eq2auras/wiki) · [stable release](https://github.com/amdrake93/eq2auras/releases/tag/stable) · [beta (dev-latest)](https://github.com/amdrake93/eq2auras/releases/tag/dev-latest) · [all releases](https://github.com/amdrake93/eq2auras/releases) · [SPEC](docs/SPEC.md) · [backlog](docs/backlog.md)

**eq2auras** is an [ACT (Advanced Combat Tracker)](https://advancedcombattracker.com/) overlay *suite* for **EverQuest 2** — a single plugin that draws clean, glanceable overlays on top of the combat data ACT already tracks. Under the hood it's **one UI framework fed by different data sources**: spell-timer data drives the **Timer Overlay**, live parse data drives the **Parse Meter**. Each new overlay is a new module on the same framework, not a new plugin.

It reads ACT's data only — presentation, never parsing. Your triggers and spell timers stay in ACT's native framework, so a teammate who **doesn't** run eq2auras still shares timers with you through ACT exactly as before; the meter simply re-draws the same live parse ACT is already computing.

## What you get

Two overlays today, each taking its cue from a WoW addon you may recognise — an inspiration to aim at, not a claim to have rebuilt it in EQ2:

- **[Timer Overlay](https://github.com/amdrake93/eq2auras/wiki/Timer-Overlay)** — a calm, glanceable list of your spell timers that escalates each ability into view as it comes due, so urgency reads at a glance instead of everything looking the same. *Inspired by [WeakAuras](https://github.com/WeakAuras/WeakAuras2).*
- **[Parse Meter](https://github.com/amdrake93/eq2auras/wiki/Parse-Meter)** — a clean replacement for ACT's cramped mini-parse: who's doing what damage or healing, class-coloured and readable mid-fight. *Inspired by [Details!](https://github.com/Tercioo/Details-Damage-Meter).*

Both drop into place where you want them, keep themselves updated, and surface what ACT already tracks — nothing to re-author.

## Getting Started

You need ACT already installed and parsing EverQuest 2 (see the [install guide](docs/install.md) for the prerequisite links). Then:

1. **Download** `eq2auras.dll` from the [latest stable release](https://github.com/amdrake93/eq2auras/releases/tag/stable) and put it in ACT's plugins folder: `%APPDATA%\Advanced Combat Tracker\Plugins`.
2. **Enable it** in ACT: *Plugins* tab → *Plugin Listing* → tick **eq2auras** (or **Browse…** to it → **Add/Enable**).
3. **Unblock if asked** — Windows marks freshly-downloaded files; if ACT prompts to unblock the DLL, accept it.
4. **Check for updates** from the eq2auras tab to stay current — it also notifies you on startup when a new build is out.

**→ Full step-by-step, updating, and troubleshooting: [docs/install.md](docs/install.md).**

## How it works

eq2auras ships as a **single ACT plugin** — one `eq2auras.dll` you drop into ACT. Inside it is a reusable **Core** — the overlay framework every feature is built on: transparent, top-most, click-through windows; the render loop; the shared row/bar, text, and radial rendering; the escalation and theming engines. Each feature is a thin module that reads ACT through its own data adapter and renders through the Core — the **Timer Overlay** feeds on ACT's spell-timer data, the **Parse Meter** on ACT's live encounter parse. Both ship today and are individually toggleable. New overlays are new modules on the same Core — one framework, one file, rather than a pile of separate plugins.

**Requirements:** Windows with ACT running and parsing EQ2; EverQuest 2 in **borderless-windowed** mode (overlays can't draw over exclusive-fullscreen — a documented ACT limitation); .NET Framework 4.x (already present on modern Windows).

## Contributing & internals

The suite is a reusable **Core** with feature modules layered on top. The `Core` project (`src/eq2auras.Core`) is a `netstandard2.0` library whose sources compile directly into the plugin assembly (`src/eq2auras.Plugin`) — so there's one shipped DLL and no second binary to keep in sync, yet the same sources build and unit-test standalone (`dotnet test tests/eq2auras.Core.Tests`). The architecture, engine rules, and roadmap live in [docs/SPEC.md](docs/SPEC.md); queued work and field feedback are in [docs/backlog.md](docs/backlog.md). Start there.

## License

© 2026 Alex Drake. Licensed under the [GNU General Public License v3.0](LICENSE).

You're free to use, modify, and share eq2auras — but any version you distribute must stay open-source under the GPL and keep attribution. (Same copyleft spirit as WeakAuras.) The bundled `Advanced Combat Tracker` reference is EQAditu's freeware, fetched from its own public release at build time and not covered by this license.
