# eq2auras

[![build](https://img.shields.io/github/actions/workflow/status/amdrake93/eq2auras/build.yml?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) [![stable](https://img.shields.io/github/v/release/amdrake93/eq2auras?label=stable&color=009E73)](https://github.com/amdrake93/eq2auras/releases/tag/stable) [![license](https://img.shields.io/github/license/amdrake93/eq2auras?color=E69F00)](LICENSE)

[install guide](docs/install.md) · [stable release](https://github.com/amdrake93/eq2auras/releases/tag/stable) · [beta (dev-latest)](https://github.com/amdrake93/eq2auras/releases/tag/dev-latest) · [all releases](https://github.com/amdrake93/eq2auras/releases) · [SPEC](docs/SPEC.md) · [backlog](docs/backlog.md)

**eq2auras** is an [ACT (Advanced Combat Tracker)](https://advancedcombattracker.com/) overlay for **EverQuest 2**. It takes the spell timers you already track in ACT and draws them as a clean, glanceable overlay that quietly counts down — then escalates each ability into view as it comes due, so you catch the recast without staring at a wall of bars. The north star is *"WeakAuras for EQ2."*

It reads ACT's data only. Your triggers and timers stay in ACT's native framework, so a teammate who **doesn't** run eq2auras still shares timers with you through ACT exactly as before.

## What you get

- **A calm list that escalates.** Upcoming timers sit quiet in a compact list; as an ability nears its recast it escalates — either into a large radial countdown in the center of your screen or highlighted in place — driven by each timer's own ACT warning value.
- **Colour that means something.** Timers draw from a hand-picked palette, assigned in the order abilities first fire and stable for the rest of your session, so a given ability keeps its colour across pulls and wipes. Greyscale and "use ACT's own colour" modes are a click away, and you can supply your own palette.
- **Put it where you want it.** Two independent panels, each freely draggable into place and sized with simple width/height knobs, with its own font. Unlock, drag against an on-screen placement grid, re-lock — positions persist.
- **No re-authoring.** It surfaces the timers ACT already knows about; there's nothing to re-enter. Point it at your existing setup and it just shows up.
- **Self-updating.** A built-in *Check for updates* pulls the latest build and reloads live — no ACT restart. Runs a **stable** channel by default, with an opt-in **beta** channel for early builds.

## Getting Started

You need ACT already installed and parsing EverQuest 2 (see the [install guide](docs/install.md) for the prerequisite links). Then:

1. **Download** `eq2auras.dll` from the [latest stable release](https://github.com/amdrake93/eq2auras/releases/tag/stable).
2. **Add it** to ACT: *Plugins* tab → *Plugin Listing* → browse to the file → **Add/Enable**.
3. **Unblock if asked** — Windows marks freshly-downloaded files; if ACT prompts to unblock the DLL, accept it.
4. **Check for updates** from the eq2auras tab to stay current — it also notifies you on startup when a new build is out.

**→ Full step-by-step, updating, and troubleshooting: [docs/install.md](docs/install.md).**

## How it works

eq2auras ships as a **single ACT plugin** — one `eq2auras.dll` you drop into ACT. Inside, a reusable overlay core (the transparent, top-most, click-through window; the render loop; bars/text/radial rendering; the escalation engine) sits under feature modules that read ACT through thin adapters. The shipped module is the **Timer Overlay**; a **Parse Meter** (a nicer replacement for ACT's mini parse) is planned. Features are individually toggleable.

**Requirements:** Windows with ACT running and parsing EQ2; EverQuest 2 in **borderless-windowed** mode (overlays can't draw over exclusive-fullscreen — a documented ACT limitation); .NET Framework 4.x (already present on modern Windows).

## Contributing & internals

The architecture, engine rules, and roadmap live in [docs/SPEC.md](docs/SPEC.md); queued work and field feedback are in [docs/backlog.md](docs/backlog.md). Start there.

## License

© 2026 Alex Drake. Licensed under the [GNU General Public License v3.0](LICENSE).

You're free to use, modify, and share eq2auras — but any version you distribute must stay open-source under the GPL and keep attribution. (Same copyleft spirit as WeakAuras.) The bundled `Advanced Combat Tracker` reference is EQAditu's freeware, fetched from its own public release at build time and not covered by this license.
