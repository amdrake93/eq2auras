# Installing eq2auras

eq2auras is an overlay plugin for **ACT (Advanced Combat Tracker)** on **EverQuest 2**. This guide covers installing it, keeping it updated, and fixing the common snags. It takes a couple of minutes.

## Prerequisites

eq2auras sits on top of ACT — it reads the spell timers ACT already tracks and draws them. So before installing it you need:

- **ACT installed and parsing EverQuest 2.** If you're not there yet, set that up first with ACT's own documentation — start at [advancedcombattracker.com](https://advancedcombattracker.com/) and install the EverQuest 2 parsing plugin. eq2auras does not replace or re-document ACT; it rides alongside it.
- **EverQuest 2 running in borderless-windowed mode.** Overlays cannot draw over exclusive-fullscreen — this is a documented ACT limitation, not specific to eq2auras. Set EQ2 to *Windowed (Fullscreen)* / borderless.
- **Windows with .NET Framework 4.x** — already present on any modern Windows install; nothing to do here.

You do **not** need a GitHub account, a login, or an access token. Everything downloads publicly.

## Install

### 1. Download the plugin into ACT's plugins folder

Go to the [latest stable release](https://github.com/amdrake93/eq2auras/releases/tag/stable) and download the **`eq2auras.dll`** asset. Save it into **ACT's plugins folder**:

```
%APPDATA%\Advanced Combat Tracker\Plugins
```

(Paste that path into the Windows Explorer address bar — or press **Win+R**, paste it, and hit Enter — to open the folder.)

**This location matters.** When the plugin updates itself it writes the new build into this folder, so a copy installed anywhere else (like your Downloads folder) will keep loading the *old* build and updates won't take effect. Put it here and updates just work. (See [Troubleshooting](#troubleshooting) if you already added it from elsewhere.)

### 2. Enable it in ACT

In ACT's main window, open the **Plugins** tab, then the **Plugin Listing** sub-tab. Because the DLL is in the plugins folder, **eq2auras** may already be listed — if so, just tick its checkbox. If it isn't listed, use the **Browse…** button near the bottom to select the `eq2auras.dll` in the plugins folder, then click **Add/Enable**.

eq2auras appears in the plugin list with its checkbox ticked, and a new **eq2auras** tab appears in ACT.

> **If Windows asks you to unblock the file — accept it.** Windows marks freshly-downloaded files as "blocked," and ACT will prompt you to unblock the DLL the first time it loads it. Say yes. (If you're never prompted and the plugin won't load, see [Troubleshooting](#troubleshooting) for how to unblock it by hand.)

### 3. Verify it's working

- The **eq2auras** tab is present in ACT and its checkbox is ticked in the Plugin Listing.
- The next time one of your ACT spell timers fires, it shows up in the overlay.

Nothing appears until a timer actually fires — that's expected. To place the overlay before combat, use the **unlock / move** mode on the eq2auras tab: unlock, drag the panels against the on-screen placement grid, then re-lock. Positions persist.

## Updating

eq2auras updates itself — no reinstalling.

- Click **Check for updates** on the eq2auras tab. If a newer build exists on your channel, it downloads and reloads live — **no ACT restart needed**.
- On startup the plugin also checks quietly and tells you (on the tab and in ACT's plugin status line) when an update is available. It never installs on its own — you click *Check for updates* to apply it.

### Channels: stable vs beta

- **Stable** (default) — curated, playtested builds. Leave it here unless you want early features.
- **Beta** (`dev-latest`) — the latest development build, updated continuously. Tick the **Beta channel (bleeding edge)** checkbox on the eq2auras tab to opt in. Un-ticking it moves you back to stable on the next check, even if that means moving to a lower version number — that's normal and installs cleanly.

## Troubleshooting

**The plugin won't load, or no eq2auras tab appears.**
The DLL is probably still blocked by Windows. Close ACT, find `eq2auras.dll`, right-click it → **Properties** → tick **Unblock** (bottom of the *General* tab) → **OK**, then re-add it in ACT. Also confirm you downloaded the `.dll` asset itself, not the source zip.

**I clicked *Check for updates*, it said installed, but nothing changed.**
The plugin must live in ACT's plugins folder — `%APPDATA%\Advanced Combat Tracker\Plugins`. Updates are written there; if you originally added the DLL from somewhere else (like Downloads), ACT keeps reloading that older copy and the update never takes hold. To fix it: remove eq2auras from ACT's Plugin Listing, move `eq2auras.dll` into the plugins folder, and add it again from there.

**The overlay doesn't show up.**
- Make sure EverQuest 2 is in **borderless-windowed** mode — over exclusive-fullscreen the overlay can't draw.
- Confirm the plugin's checkbox is ticked in the Plugin Listing.
- The overlay may be positioned off-screen or behind you. Use **unlock / move** mode on the eq2auras tab to bring the panels back into view.
- Remember nothing renders until a timer fires.

**Timers I expect aren't showing.**
eq2auras only shows timers **ACT itself is tracking**. If a timer isn't firing in eq2auras, it isn't firing in ACT either — check your ACT triggers/timers for EQ2. eq2auras adds no triggers of its own; it displays what ACT already has.

**Check for updates does nothing.**
It needs to reach GitHub. Confirm you have a working internet connection and that nothing is blocking ACT's network access, then try again.

---

Deeper design and engine details live in the [SPEC](SPEC.md); queued work is in the [backlog](backlog.md).
