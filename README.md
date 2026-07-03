# eq2auras

<!-- status:begin -->
[![build](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) ![version](https://img.shields.io/badge/version-0.1.72-56B4E9) ![released](https://img.shields.io/badge/released-2026--07--03-E69F00)

`dev-latest` · [release page](https://github.com/amdrake93/eq2auras/releases/tag/dev-latest) · [CI runs](https://github.com/amdrake93/eq2auras/actions) · [SPEC](docs/SPEC.md) · [backlog](docs/backlog.md)
<!-- status:end -->

A personal **ACT (Advanced Combat Tracker) overlay suite for EverQuest 2** — configurable, good-looking overlays that source their data from ACT's existing systems and render their own UI on top.

The north star is *"WeakAuras for EQ2"*: a reusable overlay framework that hosts many overlay features over time. It is built as a **shared core + feature modules**, so each new overlay is a module on common plumbing rather than a fresh project.

## Architecture

- **Core (reusable):** the transparent, top-most, click-through overlay window; the render loop; rendering primitives (bars / text / radial / positioning) and the escalation/conditions engine; configuration; and diagnostic logging.
- **ACT data adapters:** thin, feature-specific layers that read from ACT — a *timer adapter* (`FormSpellTimers.GetTimerFrames`) today, an *encounter adapter* (combatant/DPS) later.
- **Feature modules:** built on the core.
  - **Timer Overlay** — *live (shipped through slice 4: escalation, knobs, palette colors, dual panels, movable windows).* A calm glanceable list of upcoming ACT spell timers that escalates each timer as it approaches, driven by the timer's own ACT `WarningValue`.
  - **Parse Meter** — *future.* A replacement for ACT's "mini parse" (names / DPS) window.

Ships as a single ACT plugin (one `.dll`) that anyone can drop into their ACT `Plugins` folder; individual features are toggleable. It reads ACT's data only — triggers and timers stay in ACT's native framework, so a teammate who does **not** use eq2auras still shares timers through ACT as normal.

## Platform

- **.NET Framework 4.x** class library (ACT is a .NET Framework host — .NET Core / 5+ will not load).
- Overlay renders over **borderless-windowed** EQ2 (not exclusive-fullscreen — a documented ACT overlay limitation).
