# Styling / Theme System — Increment 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the `Theme` chrome-token module (the one source of truth for dark-chrome colors), route the durable row/header text through it, and land the persisted `BackdropOpacity` data field — the foundation the later styling increments build on.

**Architecture:** A C# static `Theme` module of frozen `SolidColorBrush`es named by semantic role (SPEC Part I §The theme system), consumed by the WPF overlay code-behind (no XAML ResourceDictionary). Timer colors stay in `OverlayTheme`, which aliases the shared value. One new nullable `MeterWindowConfig.BackdropOpacity` field with a clamp, landed unused (its rendering + knob arrive in Increment 3).

**Tech Stack:** C# (Core = netstandard2.0, Mac-testable via `dotnet test`; Plugin = net472/WPF, **not** Mac-testable — verified by CI compile + on-box field check), `System.Windows.Media`, `DataContractJsonSerializer` (DCJS), xUnit for Core tests.

## Global Constraints

Copied verbatim from the spec / CLAUDE.md; every task's requirements implicitly include these.

- **Single-assembly packaging.** Core sources are `<Compile Include>`d into the plugin. Never reference a second DLL; no non-GAC types in fields unless compiled in. No careless `async` in the plugin project.
- **Never reference `System.Web.Extensions`** (breaks the WPF XAML markup compiler in CI). JSON = `DataContractJsonSerializer`. DCJS skips field initializers on deserialize → **enum/bool knob defaults must be the 0-value**; nullable numeric `null` (never `0`) means "unset, use default".
- **Never `Assembly.LoadFrom`.**
- **Only `eq2auras.Core` builds/tests on the Mac:** `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. Never build the Plugin/solution locally. Plugin changes are transcribe-only, gated by the branch's CI (Core tests + WPF compile + artifact), then field-verified on the Windows box.
- **Semantic tokens, not literals** (SPEC §The theme system): names carry the role (`TextMuted`), never the value.
- **Retain elements, animate properties** — unaffected here (no per-tick work in this increment).

**Branch:** work continues on `styling-theme-system` (the spec branch; reviewer-approved). Present at the owner's merge gate when the plan is implemented and its review closes — never merge without the owner's call.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `src/eq2auras.Core/Config/MeterWindowConfig.cs` | one meter window's persisted config | **modify** — add `BackdropOpacity` field |
| `src/eq2auras.Core/Config/MeterSettings.cs` | meter settings + Normalize/clamp | **modify** — backdrop-opacity constants + clamp |
| `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` | Core config tests (xUnit) | **modify** — add backdrop-opacity tests |
| `src/eq2auras.Plugin/Overlay/Theme.cs` | the chrome-token module | **create** |
| `src/eq2auras.Plugin/Overlay/OverlayTheme.cs` | timer color constants | **modify** — alias `Text` into `Theme` |
| `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` | meter data-row visual | **modify** — muted/percent → tokens |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | meter window (header text) | **modify** — dim header text → token |

---

### Task 1: Core — `BackdropOpacity` config field + clamp (TDD)

**Files:**
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs:29-30` (after the existing `Opacity` member)
- Modify: `src/eq2auras.Core/Config/MeterSettings.cs:37-39` (constants) and `:77-87` (the per-window clamp loop in `Normalize`)
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig.BackdropOpacity` (`double?`, DataMember `"backdropOpacity"`); `MeterSettings.MinBackdropOpacity = 0.0`, `MaxBackdropOpacity = 1.0`, `DefaultBackdropOpacity = 1.0` (`const double`). `null` means "unset → host resolves to `DefaultBackdropOpacity`". `0.0` is a legal value (fully transparent backdrop), never coerced to null.
- Consumes: nothing (foundation).

- [ ] **Step 1: Write the failing tests**

Add to `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` (mirrors `Window_opacity_clamps_to_range` / `Null_opacity_stays_null_meaning_default`):

```csharp
    [Theory]
    [InlineData(-0.2, 0.0)]   // below floor -> clamped up to MinBackdropOpacity
    [InlineData(1.5, 1.0)]    // above ceiling -> clamped down to MaxBackdropOpacity
    [InlineData(0.4, 0.4)]    // in range -> unchanged
    [InlineData(0.0, 0.0)]    // zero is a REAL value (fully transparent backdrop), must survive
    public void Window_backdrop_opacity_clamps_to_range(double stored, double expected)
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { BackdropOpacity = stored },
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(expected, parsed.Meter.Windows[0].BackdropOpacity);
    }

    [Fact]
    public void Null_backdrop_opacity_stays_null_meaning_default()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        Assert.Null(parsed.Meter.Windows[0].BackdropOpacity);   // null -> host resolves to DefaultBackdropOpacity (1.0)
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "backdrop_opacity"`
Expected: FAIL — compile error, `MeterWindowConfig` has no `BackdropOpacity`.

- [ ] **Step 3: Add the config field**

In `MeterWindowConfig.cs`, immediately after the `Opacity` member (line 30):

```csharp
        [DataMember(Name = "backdropOpacity")]
        public double? BackdropOpacity { get; set; }   // 0.0..1.0; null = DefaultBackdropOpacity (1.0). Rendering + knob land in increment 3.
```

- [ ] **Step 4: Add the constants**

In `MeterSettings.cs`, after `MaxVisibleRows` (line 41):

```csharp
        public const double MinBackdropOpacity = 0.0;   // fully transparent backdrop is allowed
        public const double MaxBackdropOpacity = 1.0;
        public const double DefaultBackdropOpacity = 1.0;   // null resolves here (increment 3 may retune the shipped default)
```

- [ ] **Step 5: Add the clamp**

In `MeterSettings.Normalize`, inside the `foreach (var window in Windows)` loop, after the `Opacity` clamp (after line 80):

```csharp
                if (window.BackdropOpacity.HasValue && (window.BackdropOpacity.Value < MinBackdropOpacity || window.BackdropOpacity.Value > MaxBackdropOpacity))
                    window.BackdropOpacity = Math.Min(MaxBackdropOpacity, Math.Max(MinBackdropOpacity, window.BackdropOpacity.Value));
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — the two new tests plus the full existing suite green (no regressions).

- [ ] **Step 7: Commit**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs src/eq2auras.Core/Config/MeterSettings.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Meter: add persisted BackdropOpacity config field + clamp (increment 1)"
```

---

### Task 2: Plugin — the `Theme` chrome-token module (transcribe)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/Theme.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayTheme.cs:17` (alias `Text`)

**Interfaces:**
- Produces: `Theme.SurfaceTint` (`Color`); frozen `SolidColorBrush` tokens `Theme.TextPrimary`, `Theme.TextLabel`, `Theme.TextMuted`, `Theme.Divider`, `Theme.AccentAmber`, `Theme.AccentCrimson`, `Theme.AccentBlue`; factory `Theme.Surface(byte alpha) → SolidColorBrush` (a frozen backdrop brush over `SurfaceTint` at the given alpha, for later increments).
- Consumes: nothing.

**Verification note:** WPF — not Mac-testable. Verified by CI compile + the on-box field check (Testing strategy). No unit test.

- [ ] **Step 1: Create `Theme.cs`**

```csharp
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// The overlay's chrome vocabulary — one source of truth for dark-chrome colors
    /// (SPEC Part I §The theme system). Semantic tokens named by ROLE, not value;
    /// frozen SolidColorBrushes so callers share one instance rather than allocating a
    /// brush per element. Timer-only colors stay in OverlayTheme, which aliases the
    /// shared value. Increment 1 lands the surface, text, accent, and divider tokens; the
    /// interactive-state tokens (LinkNormal/LinkHover/ItemSelected, SPEC §The theme system)
    /// land with the control that consumes them — links with the button (increment 2),
    /// ItemSelected with the selectable list-item (increment 4), a hover/selected form being
    /// kit-coupled — as do the font-weight, spacing, and radius scales.
    internal static class Theme
    {
        // The single dark blue-grey backdrop tint; opacity is applied per surface
        // (translucent timer / knob-driven meter / solid chrome, §The theme system).
        public static readonly Color SurfaceTint = Color.FromRgb(0x14, 0x17, 0x1D);   // 20,23,29

        public static readonly SolidColorBrush TextPrimary = Frozen(Color.FromRgb(0xF5, 0xF5, 0xF5));   // values, titles (== WhiteSmoke)
        public static readonly SolidColorBrush TextLabel   = Frozen(Color.FromRgb(0xC4, 0xCA, 0xD6));   // field labels, percent column
        public static readonly SolidColorBrush TextMuted   = Frozen(Color.FromRgb(0x8B, 0x93, 0xA3));   // dim/subordinate text, links

        public static readonly SolidColorBrush Divider     = Frozen(Color.FromRgb(0x33, 0x40, 0x4F));   // 51,64,79 (== OverlayTheme.CalmBorder rgb)
        public static readonly SolidColorBrush AccentAmber = Frozen(Colors.Gold);
        public static readonly SolidColorBrush AccentCrimson = Frozen(Colors.Crimson);
        public static readonly SolidColorBrush AccentBlue  = Frozen(Color.FromRgb(0x56, 0xB4, 0xE9));

        /// A frozen backdrop brush at a given alpha over SurfaceTint. Increment 1 paints
        /// no backdrop with it — it is the surface-brush factory the settings window and
        /// the persistent backdrop consume in later increments.
        public static SolidColorBrush Surface(byte alpha)
            => Frozen(Color.FromArgb(alpha, SurfaceTint.R, SurfaceTint.G, SurfaceTint.B));

        private static SolidColorBrush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
```

- [ ] **Step 2: Alias `OverlayTheme.Text` into `Theme`**

`OverlayTheme.Text` is `Colors.WhiteSmoke` (`OverlayTheme.cs:17`), byte-identical to `Theme.TextPrimary`. Point it at the shared token so there is one source (SPEC §The theme system: "aliases into `Theme` where byte-identical"), with **no value change** and no timer call-site touched:

Replace `OverlayTheme.cs:17`:
```csharp
        public static readonly Color Text = Colors.WhiteSmoke;
```
with:
```csharp
        public static readonly Color Text = Theme.TextPrimary.Color;   // one source of truth (SPEC §The theme system); WhiteSmoke, unchanged
```

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/Theme.cs src/eq2auras.Plugin/Overlay/OverlayTheme.cs
git commit -m "Theme: chrome-token module (semantic frozen brushes); OverlayTheme.Text aliases it (increment 1)"
```

---

### Task 3: Plugin — retag the durable row/header text to `Theme` (transcribe)

Only surfaces **not** rewritten by a later increment are retagged now (the settings window and the right-click menu are replaced in increments 2 and 4, so they consume `Theme` when rebuilt — no throwaway retag). The meter row internals are deferred (thread C), so the row's muted/percent text is a durable consumer that proves the module.

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs:28` (drop `MutedText` const), `:50` (percent), `:60` (secondary)
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs:188-190` (dim header text)

**Interfaces:**
- Consumes: `Theme.TextMuted`, `Theme.TextLabel`, `Theme.TextPrimary` (Task 2).
- Produces: nothing.

**Deliberate minor color unification** (Alex-approved, field-verify "looks the same"): the row's muted grey `0x9AA0AD` (`MeterRowVisual.cs:28`) collapses into `Theme.TextMuted` (`0x8B93A3`) — the same role, one value now. Percent (`0xC4CAD6`) and header dim (`0x8B93A3`) already equal their tokens, so those are exact.

**Verification note:** WPF — CI compile + on-box field check. No unit test.

- [ ] **Step 1: Retag the percent column**

In `MeterRowVisual.cs`, the `_percent` `TextBlock` foreground (line 50):
```csharp
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
```
becomes:
```csharp
                Foreground = Theme.TextLabel,
```

- [ ] **Step 2: Retag the secondary (muted) text and drop the local const**

In `MeterRowVisual.cs`, delete the `MutedText` field (line 28):
```csharp
        private static readonly Color MutedText = Color.FromArgb(255, 0x9A, 0xA0, 0xAD);   // subordinate to the value
```
and change its one use (line 60) from:
```csharp
                Foreground = new SolidColorBrush(MutedText),
```
to:
```csharp
                Foreground = Theme.TextMuted,
```

- [ ] **Step 3: Retag the dim header text**

In `MeterWindow.cs` (lines 188-190), the `block` foreground:
```csharp
                Foreground = new SolidColorBrush(dim
                    ? Color.FromArgb(255, 0x8B, 0x93, 0xA3)
                    : OverlayTheme.Text),
```
becomes (both branches now shared frozen brushes):
```csharp
                Foreground = dim ? Theme.TextMuted : Theme.TextPrimary,
```

- [ ] **Step 4: Confirm no other consumer of the removed `MutedText` const**

Run: `grep -rn "MutedText" src/eq2auras.Plugin/`
Expected: no matches (the only reference was the one retagged in Step 2).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter: retag durable row/header text to Theme tokens (increment 1)"
```

---

## Testing strategy

**Core (Mac, gating):** `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` — the two new `BackdropOpacity` tests plus the full suite green. This is the only automated gate that runs locally.

**Plugin (CI):** the branch push runs verify-only CI (Core tests + WPF compile + artifact). Task 2/3 are transcribe-only; their correctness gate is the compile plus the on-box check below. A green compile proves the `Theme` references resolve and the retag edits are well-formed.

**On-box field check (merge-gate script — Increment 1 is a near-invisible foundation, so the check is "nothing regressed" + "the field persists"):**
1. Update on `dev-latest`, open a meter with a secondary metric selected and combat running.
2. **Row text unchanged:** the secondary column reads muted-grey and the percent column reads its light-grey, both legible — no black-on-dark, no wrong color. (The row muted grey shifts `0x9AA0AD`→`0x8B93A3`; expect *imperceptible*, confirm it still reads muted.)
3. **Header dim text unchanged:** the `(duration)`/metric dim parts of the header read the same muted grey as before.
4. **Backdrop-opacity persistence:** with the plugin able to write settings, hand-edit `settings.json` to add `"backdropOpacity": 0.4` to a window (or leave it for increment 3's knob), reload the plugin, confirm the value survives a round-trip and an out-of-range value (e.g. `1.5`) is clamped to `1.0` on the next save. (No visible effect yet — rendering lands in increment 3.)
5. **Timer overlay unregressed:** timers render with their usual colors (the `OverlayTheme.Text` alias is byte-identical; a light sanity glance suffices — increment 1 re-extracts nothing from the shared bar primitive).

## Self-review

**Spec coverage (increment 1 scope only):** §The theme system's token-module + semantic-naming + `OverlayTheme`-aliasing claims → Task 2, which lands the surface/text/accent/divider tokens. The interactive-state tokens `LinkNormal`/`LinkHover`/`ItemSelected` (SPEC §The theme system) are **deferred to their consuming controls** — links to the button (increment 2), `ItemSelected` to the selectable list-item (increment 4) — because a hover/selected color's form is kit-coupled and no increment-1 surface consumes one; the `Theme` module grows across increments, exactly as it does for the font/spacing/radius scales. The `BackdropOpacity` field (§Settings persistence, plan-watch 3's data half) → Task 1. Retag of scattered chrome literals (§The theme system "ad-hoc chrome colors had scattered") → Task 3 (durable consumers only; settings-window/menu literals retag when rewritten in increments 2/4). The kit, popup, backdrop rendering, header cog/total, and `MetricRegistry` split are **out of increment 1** by design (roadmap above) — not gaps.

**Placeholder scan:** none — every step carries exact code, paths, and commands.

**Type consistency:** `Theme.TextPrimary/TextLabel/TextMuted/Divider/AccentAmber/AccentCrimson/AccentBlue` are `SolidColorBrush`; `SurfaceTint` is `Color`; `Surface(byte)` returns `SolidColorBrush`. Task 3 assigns `TextBlock.Foreground` (a `Brush`) directly from the brush tokens — type-correct. `BackdropOpacity` is `double?` everywhere; constants are `double`. `OverlayTheme.Text` stays a `Color` (`Theme.TextPrimary.Color`), so its existing `new SolidColorBrush(OverlayTheme.Text)` call sites still compile.

## Plan-watch items carried forward (spec review 2026-07-18)

Increment 1 lands **half of plan-watch #3** (the `BackdropOpacity` config field + clamp; the two sliders + independent feed are increment 3) and starts **#6** (the `Theme` module + `OverlayTheme` alias; the six-primitive kit is increment 2). Items **#1, #2, #4, #5** belong to later increments and are unaddressed here by design — their increment's plan carries them.
