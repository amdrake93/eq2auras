# Slice 3: Knob Model + Session-Stable Palette Colors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce the thin knob model (persisted `Settings`) with its first two knobs — `ColorSource` (session-stable name-keyed palette, default) and `EscalationStyle` (center-radial vs highlight-in-place) — configurable from the plugin's ACT tab.

**Architecture:** `Settings` is a plain DataContract in Core (string-serialization TDD'd on the Mac; enum defaults are the 0-values because DCJS skips initializers); a thin `SettingsStore` in the plugin does file I/O. Color resolution moves fully into Core: `PaletteAssigner` (stateful, normalized-name-keyed, first-fired order) + `ColorPolicy` (palette/grey/soften math) resolve each reading's final ARGB inside the tracker, so renderers just paint `FillArgb` and lose their soften logic. `EscalationStyle=HighlightInPlace` suppresses the center zone entirely — imminents stay as highlighted rows, linger-configured overdue timers render as LATE-styled rows.

**Tech Stack:** existing (netstandard2.0 Core / net472 WPF / xUnit / GitHub Actions). DCJS only — never `System.Web.Extensions`.

---

## File Structure

```
src/eq2auras.Core/
├── Config/Settings.cs               # NEW: DataContract + enums + Parse/ToJson
└── Timers/
    ├── ColorPolicy.cs               # NEW: palette/grey ARGB tables, Soften, Resolve
    ├── PaletteAssigner.cs           # NEW: normalized-name -> first-fired palette index
    ├── EscalationTracker.cs         # ctor(Settings); resolves colors; style branch
    └── TimerListBuilder.cs          # Build(..., bool includeOverdue = false)
src/eq2auras.Plugin/
├── SelfUpdate/SettingsStore.cs      # NEW: load/save settings.json in %APPDATA%
├── Eq2AurasPlugin.cs                # settings field, two ComboBoxes on the tab
└── Overlay/
    ├── OverlayTheme.cs              # Palette derives from ColorPolicy; Soften/SoftTimerColor deleted
    ├── TimerRowVisual.cs            # paints FillArgb as-is; LATE text for Overdue rows
    ├── CenterVisuals.cs             # paints FillArgb as-is
    └── TimerListWindow.xaml.cs      # ShowPalettePreview -> false
tests/eq2auras.Core.Tests/
├── SettingsTests.cs                 # NEW
├── ColorPolicyTests.cs              # NEW (includes PaletteAssigner)
└── EscalationTrackerTests.cs        # style + color-resolution cases
```

---

## Task 1: Settings (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Config/Settings.cs`
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

- [ ] **Step 1: Failing tests**

Create `tests/eq2auras.Core.Tests/SettingsTests.cs`:

```csharp
using Eq2Auras.Core.Config;
using Xunit;

public class SettingsTests
{
    [Fact]
    public void Roundtrips_all_fields()
    {
        var settings = new Settings
        {
            ColorSource = ColorSource.Greyscale,
            EscalationStyle = EscalationStyle.HighlightInPlace
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(ColorSource.Greyscale, parsed.ColorSource);
        Assert.Equal(EscalationStyle.HighlightInPlace, parsed.EscalationStyle);
    }

    [Theory]
    [InlineData("")]                       // empty file
    [InlineData("not json at all {{{")]    // corrupt file
    [InlineData("{}")]                     // old file missing every field
    [InlineData("{\"someFutureKnob\":7}")] // file from a NEWER version
    public void Bad_or_partial_json_yields_defaults(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.Equal(ColorSource.Palette, parsed.ColorSource);
        Assert.Equal(EscalationStyle.CenterRadial, parsed.EscalationStyle);
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` → FAIL (`Eq2Auras.Core.Config` missing).

- [ ] **Step 3: Implement**

Create `src/eq2auras.Core/Config/Settings.cs`:

```csharp
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Eq2Auras.Core.Config
{
    // ⚠ Knob enums: the DEFAULT must be the 0-value. DCJS creates objects without
    // running initializers, so a field missing from an old settings.json comes back
    // as 0 — which must mean "the default".
    public enum ColorSource { Palette = 0, Greyscale = 1, ActColor = 2 }
    public enum EscalationStyle { CenterRadial = 0, HighlightInPlace = 1 }

    /// The knob store (SPEC §Configuration): one plain object, every tunable a typed
    /// member with a baked-in default. Serialized with DCJS (never System.Web.Extensions
    /// — it breaks the WPF markup compiler). Unknown fields in the file are ignored;
    /// missing fields fall back to defaults — settings files survive version skew both ways.
    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "colorSource")]
        public ColorSource ColorSource { get; set; } = ColorSource.Palette;

        [DataMember(Name = "escalationStyle")]
        public EscalationStyle EscalationStyle { get; set; } = EscalationStyle.CenterRadial;

        public static Settings Parse(string json)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Settings));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (Settings)serializer.ReadObject(stream) ?? new Settings();
                }
            }
            catch
            {
                return new Settings();   // empty/corrupt/foreign file -> defaults
            }
        }

        public string ToJson()
        {
            var serializer = new DataContractJsonSerializer(typeof(Settings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
```

- [ ] **Step 4: Run green** → PASS (39 old + 5 new).
- [ ] **Step 5: Commit** — `git add … && git commit -m "Core: Settings knob store (DCJS, 0-value defaults, version-skew tolerant)"`

---

## Task 2: ColorPolicy + PaletteAssigner (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Timers/ColorPolicy.cs`
- Create: `src/eq2auras.Core/Timers/PaletteAssigner.cs`
- Test: `tests/eq2auras.Core.Tests/ColorPolicyTests.cs`

- [ ] **Step 1: Failing tests**

Create `tests/eq2auras.Core.Tests/ColorPolicyTests.cs`:

```csharp
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class ColorPolicyTests
{
    [Fact]
    public void Assigner_hands_out_slots_in_first_fired_order_and_keeps_them()
    {
        var assigner = new PaletteAssigner();

        Assert.Equal(0, assigner.IndexFor("Blanket of Eternal Night"));
        Assert.Equal(1, assigner.IndexFor("Holy Shield"));
        Assert.Equal(0, assigner.IndexFor("Blanket of Eternal Night"));   // stable
    }

    [Fact]
    public void Assigner_normalizes_names()
    {
        var assigner = new PaletteAssigner();

        Assert.Equal(0, assigner.IndexFor("Blanket of Eternal Night"));
        Assert.Equal(0, assigner.IndexFor("  blanket of eternal night "));
    }

    [Fact]
    public void Palette_cycles_past_its_length()
    {
        Assert.Equal(ColorPolicy.PaletteArgb[0], ColorPolicy.Resolve(ColorSource.Palette, 5, 0));
        Assert.Equal(ColorPolicy.PaletteArgb[1], ColorPolicy.Resolve(ColorSource.Palette, 6, 0));
    }

    [Fact]
    public void Resolve_maps_each_source()
    {
        Assert.Equal(ColorPolicy.PaletteArgb[2], ColorPolicy.Resolve(ColorSource.Palette, 2, 123));
        Assert.Equal(ColorPolicy.GreyArgb[2], ColorPolicy.Resolve(ColorSource.Greyscale, 2, 123));
        Assert.Equal(ColorPolicy.Soften(123), ColorPolicy.Resolve(ColorSource.ActColor, 2, 123));
    }

    [Fact]
    public void Soften_blends_toward_slate()
    {
        // Pure ACT-default blue #FF0000FF: r=0*.65+110*.35=38, g=0+41, b=165+45=211
        Assert.Equal(unchecked((int)0xFF2629D3), ColorPolicy.Soften(unchecked((int)0xFF0000FF)));
    }
}
```

- [ ] **Step 2: Run red** → FAIL (types missing).

- [ ] **Step 3: Implement**

Create `src/eq2auras.Core/Timers/ColorPolicy.cs`:

```csharp
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// Final display-color resolution (SPEC §Timer colors). Palette/greyscale are
    /// designed colors rendered as-is; ActColor is user data softened toward slate.
    public static class ColorPolicy
    {
        // Guild-approved palette v2: sky, amber, teal, rose, indigo.
        public static readonly int[] PaletteArgb =
        {
            unchecked((int)0xFF56B4E9),
            unchecked((int)0xFFE69F00),
            unchecked((int)0xFF009E73),
            unchecked((int)0xFFE37DA4),
            unchecked((int)0xFF5E6BD8),
        };

        // Light-to-dark grey ramp, legible over the dark backplate at fill alpha.
        public static readonly int[] GreyArgb =
        {
            unchecked((int)0xFFF2F2F2),
            unchecked((int)0xFFC4C4C4),
            unchecked((int)0xFF999999),
            unchecked((int)0xFF787878),
            unchecked((int)0xFF5A5A5A),
        };

        public static int Resolve(ColorSource source, int paletteIndex, int actArgb)
        {
            switch (source)
            {
                case ColorSource.Greyscale: return GreyArgb[paletteIndex % GreyArgb.Length];
                case ColorSource.ActColor: return Soften(actArgb);
                default: return PaletteArgb[paletteIndex % PaletteArgb.Length];
            }
        }

        public static int Soften(int argb)
        {
            const double keep = 0.65;
            const int slateR = 110, slateG = 118, slateB = 130;
            int r = (byte)(((argb >> 16) & 0xFF) * keep + slateR * (1 - keep));
            int g = (byte)(((argb >> 8) & 0xFF) * keep + slateG * (1 - keep));
            int b = (byte)((argb & 0xFF) * keep + slateB * (1 - keep));
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
    }
}
```

Create `src/eq2auras.Core/Timers/PaletteAssigner.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Timers
{
    /// Session-stable color identity (SPEC §Timer colors): keyed by NORMALIZED TIMER
    /// NAME ONLY — the ability as players think of it. Same ability from different boss
    /// variants / zone-categorized triggers keeps one color. First-fired order; stable
    /// for the plugin-instance lifetime (consistency is a repeated-attempts feature —
    /// a reload resetting assignments is accepted).
    public sealed class PaletteAssigner
    {
        private readonly Dictionary<string, int> _slotByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _nextSlot;

        public int IndexFor(string timerName)
        {
            var key = (timerName ?? "").Trim().ToLowerInvariant();
            if (!_slotByName.TryGetValue(key, out var slot))
            {
                slot = _nextSlot++;
                _slotByName[key] = slot;
            }
            return slot;
        }
    }
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `"Core: ColorPolicy (palette/grey/soften) + name-keyed PaletteAssigner"`

---

## Task 3: Thread the knobs through tracker + builder (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Timers/EscalationTracker.cs`
- Modify: `src/eq2auras.Core/Timers/TimerListBuilder.cs`
- Test: `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs` (additions)

- [ ] **Step 1: Failing tests** — append to `EscalationTrackerTests.cs`:

```csharp
    [Fact]
    public void Palette_mode_assigns_stable_name_keyed_colors()
    {
        var tracker = new EscalationTracker();   // defaults: ColorSource.Palette
        var first = tracker.Tick(R(Reading("Blanket", 25), Reading("Shield", 20)));
        var again = tracker.Tick(R(Reading("Shield", 19), Reading("Blanket", 24)));

        Assert.Equal(ColorPolicy.PaletteArgb[0], first.ListRows.Single(r => r.Name == "Blanket").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], first.ListRows.Single(r => r.Name == "Shield").FillArgb);
        // stable across ticks regardless of this tick's order
        Assert.Equal(ColorPolicy.PaletteArgb[0], again.ListRows.Single(r => r.Name == "Blanket").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], again.ListRows.Single(r => r.Name == "Shield").FillArgb);
    }

    [Fact]
    public void ActColor_mode_keeps_the_timers_own_color_softened()
    {
        var tracker = new EscalationTracker(new Settings { ColorSource = ColorSource.ActColor });
        var frame = tracker.Tick(R(Reading("t", 25)));

        Assert.Equal(ColorPolicy.Soften(-16776961), frame.ListRows[0].FillArgb);
    }

    [Fact]
    public void HighlightInPlace_keeps_imminents_in_the_list_and_center_empty()
    {
        var tracker = new EscalationTracker(new Settings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", 5), Reading("calm", 25)));

        Assert.Empty(frame.CenterElements);
        Assert.Equal(2, frame.ListRows.Count);
        Assert.Equal(TimerUrgency.Imminent, frame.ListRows.Single(r => r.Name == "boss").Urgency);
    }

    [Fact]
    public void HighlightInPlace_renders_linger_overdue_as_LATE_rows()
    {
        var tracker = new EscalationTracker(new Settings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", -2, removeValue: -15), Reading("calm", 25)));

        Assert.Empty(frame.CenterElements);
        var lateRow = frame.ListRows.Single(r => r.Name == "boss");
        Assert.Equal(TimerUrgency.Overdue, lateRow.Urgency);
        Assert.Equal(-2, lateRow.TimeLeft);
        Assert.Equal("boss", frame.ListRows[0].Name);   // overdue sorts first (most urgent)
    }

    [Fact]
    public void HighlightInPlace_still_hides_remove_at_zero_timers_past_zero()
    {
        var tracker = new EscalationTracker(new Settings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", -1, removeValue: 0)));

        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }
```

Add `using Eq2Auras.Core.Config;` and `using System.Linq;` (already present) to the test file's usings.

- [ ] **Step 2: Run red** → FAIL (ctor + behavior missing).

- [ ] **Step 3: Implement**

`TimerListBuilder.Build` gains the overdue option — signature and filter change:

```csharp
        public static List<TimerRow> Build(IEnumerable<TimerReading> readings, bool includeOverdue = false)
        {
            // TimeLeft <= 0 excluded by default (CenterRadial shows overdue as center
            // LATE cards). HighlightInPlace mode includes linger-configured overdue
            // timers as rows; ascending sort naturally puts them (negative) first.
            return readings
                .Where(r => r.TimeLeft > 0 || (includeOverdue && r.RemoveValueSeconds < 0))
                .Select(ToRow)
                .OrderBy(r => r.TimeLeft)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
```

`EscalationTracker` — ctor, color resolution, style branch (full new `Tick` shape):

```csharp
using Eq2Auras.Core.Config;
// (existing usings)

    public sealed class EscalationTracker
    {
        private const int CenterSlots = 3;          // future config knob

        private readonly Settings _settings;
        private readonly PaletteAssigner _palette = new PaletteAssigner();

        public EscalationTracker(Settings settings = null)
        {
            _settings = settings ?? new Settings();
        }

        public OverlayFrame Tick(IReadOnlyList<TimerReading> readings)
        {
            var governing = readings
                .GroupBy(KeyOf)
                .Select(g => g.OrderBy(TimerMath.PreciseOf).First())
                .ToList();

            // Resolve every reading's final display color here — renderers just paint.
            foreach (var reading in governing)
            {
                reading.FillArgb = ColorPolicy.Resolve(
                    _settings.ColorSource, _palette.IndexFor(reading.Name), reading.FillArgb);
            }

            var live = governing.Where(r => r.TimeLeft > 0).ToList();
            bool inPlace = _settings.EscalationStyle == EscalationStyle.HighlightInPlace;

            var lates = inPlace
                ? new List<CenterElement>()
                : governing
                    .Where(r => r.TimeLeft <= 0 && r.RemoveValueSeconds < 0)
                    .OrderBy(r => -r.TimeLeft)
                    .Select(r => new CenterElement
                    {
                        Kind = CenterElementKind.Late,
                        Name = r.Name,
                        Combatant = r.Combatant,
                        LateSeconds = -r.TimeLeft,
                        FillArgb = r.FillArgb
                    })
                    .ToList();

            var centered = new List<TimerReading>();
            if (!inPlace)
            {
                var imminent = live
                    .Where(r => r.TimeLeft <= TimerMath.EffectiveWarning(r))
                    .OrderBy(TimerMath.PreciseOf)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                centered = imminent.Take(Math.Max(0, CenterSlots - lates.Count)).ToList();
            }

            var pies = centered.Select(r => new CenterElement
            {
                Kind = CenterElementKind.Pie,
                Name = r.Name,
                Combatant = r.Combatant,
                SecondsLeft = (int)Math.Max(0, Math.Ceiling(TimerMath.PreciseOf(r))),
                PreciseSecondsLeft = Math.Max(0, TimerMath.PreciseOf(r)),
                PieFraction = Math.Max(0, Math.Min(1.0, TimerMath.PreciseOf(r) / TimerMath.EffectiveWarning(r))),
                WarningSeconds = TimerMath.EffectiveWarning(r),
                FillArgb = r.FillArgb
            });

            var listSource = inPlace ? governing : live.Except(centered);
            return new OverlayFrame
            {
                ListRows = TimerListBuilder.Build(listSource, includeOverdue: inPlace),
                CenterElements = lates.Concat(pies).ToList()
            };
        }

        private static string KeyOf(TimerReading r) => r.Name + "|" + r.Combatant;
    }
```

(Existing comments about governing-instance selection and config-driven overdue stay with their blocks.)

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `"Core: knobs threaded — palette resolution in tracker, HighlightInPlace style (LATE rows), builder includeOverdue"`

---

## Task 4: Plugin — SettingsStore, tab controls, renderer cleanup

**Files:**
- Create: `src/eq2auras.Plugin/SelfUpdate/SettingsStore.cs` *(sits with TokenStore; both are %APPDATA% persistence)*
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayTheme.cs`, `TimerRowVisual.cs`, `CenterVisuals.cs`, `TimerListWindow.xaml.cs`

- [ ] **Step 1: SettingsStore**

Create `src/eq2auras.Plugin/SelfUpdate/SettingsStore.cs`:

```csharp
using System.IO;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.SelfUpdate
{
    public static class SettingsStore
    {
        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "settings.json");

        public static Settings Load()
        {
            if (!File.Exists(PathOnDisk)) return new Settings();
            return Settings.Parse(File.ReadAllText(PathOnDisk));
        }

        public static void Save(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
            File.WriteAllText(PathOnDisk, settings.ToJson());
        }
    }
}
```

- [ ] **Step 2: Plugin wiring + tab controls**

In `Eq2AurasPlugin.cs`: add `using Eq2Auras.Core.Config;`, a field `private Settings _settings;`. In `InitPlugin` before creating the tracker: `_settings = SettingsStore.Load();` and change tracker construction to `new EscalationTracker(_settings)`. In `BuildConfigTab`, after the update button, add:

```csharp
            var colorLabel = new Label { Text = "Colors:", Left = 10, Top = 82, Width = 70 };
            var colorBox = new ComboBox
            {
                Left = 85, Top = 78, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            colorBox.Items.AddRange(new object[] { "Palette", "Greyscale", "ACT colors" });
            colorBox.SelectedIndex = (int)_settings.ColorSource;
            colorBox.SelectedIndexChanged += (s, e) =>
            {
                _settings.ColorSource = (ColorSource)colorBox.SelectedIndex;
                SettingsStore.Save(_settings);
            };

            var styleLabel = new Label { Text = "Escalation:", Left = 10, Top = 112, Width = 70 };
            var styleBox = new ComboBox
            {
                Left = 85, Top = 108, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            styleBox.Items.AddRange(new object[] { "Center radial", "Highlight in place" });
            styleBox.SelectedIndex = (int)_settings.EscalationStyle;
            styleBox.SelectedIndexChanged += (s, e) =>
            {
                _settings.EscalationStyle = (EscalationStyle)styleBox.SelectedIndex;
                SettingsStore.Save(_settings);
            };

            tab.Controls.Add(colorLabel);
            tab.Controls.Add(colorBox);
            tab.Controls.Add(styleLabel);
            tab.Controls.Add(styleBox);
```

(The tracker holds the same `_settings` instance and reads it per tick; the ComboBox events and the poll both run on ACT's UI thread — no locking. Changes apply within one tick, live.)

- [ ] **Step 3: Renderers paint resolved colors as-is**

- `OverlayTheme.cs`: delete `Soften` and `SoftTimerColor`; `Palette` now derives from Core:

```csharp
        /// WPF mirror of the Core palette (used by the preview strip).
        public static readonly Color[] Palette = BuildPalette();

        private static Color[] BuildPalette()
        {
            var colors = new Color[Eq2Auras.Core.Timers.ColorPolicy.PaletteArgb.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = FromArgbInt(Eq2Auras.Core.Timers.ColorPolicy.PaletteArgb[i]);
            return colors;
        }
```

- `TimerRowVisual.Update`: replace `OverlayTheme.SoftTimerColor(row.FillArgb)` with `OverlayTheme.FromArgbInt(row.FillArgb)`, and make the time text overdue-aware:

```csharp
            _time.Text = row.Urgency == TimerUrgency.Overdue
                ? "LATE +" + (-row.TimeLeft) + "s"
                : (int)Math.Max(0, Math.Ceiling(row.PreciseTimeLeft)) + "s";
```

- `CenterVisuals.cs` (`PieVisual.Update`): replace `OverlayTheme.SoftTimerColor(element.FillArgb)` with `OverlayTheme.FromArgbInt(element.FillArgb)`.
- `TimerListWindow.xaml.cs`: `ShowPalettePreview = false` (palette now visible on real timers).

- [ ] **Step 4: Verify, commit, push, CI**

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj   # PASS
git add -A && git commit -m "Plugin: settings.json store, ColorSource/EscalationStyle tab controls, renderers paint resolved colors; preview off"
git push && gh run watch --exit-status
```

---

## Task 5: Live verification **[WIN]** (guild as jury)

- [ ] **Check for updates** → reload. Tab now shows two dropdowns; preview swatches gone; `settings.json` appears in `%APPDATA%\Advanced Combat Tracker\eq2auras\` after first change.
- [ ] **Palette identity:** fire 2–3 different triggers → sky/amber/teal in fired order (at fill alpha they read more muted than the swatches did — expected). Let one expire, re-fire it → **same color** (wipe-and-retry consistency, the guild's ask).
- [ ] **Greyscale:** flip the dropdown → colors change to the grey ramp **live, within a tick** (assignments keep the same slots).
- [ ] **ACT colors:** flip again → back to the old softened-blue look.
- [ ] **Highlight in place:** flip escalation style → escalating timer **stays in the list** with the gold outline, no center pie; a linger-configured timer (set one to −10 remove) shows a red `LATE +Ns` **row**. Flip back → center pies return.
- [ ] **Persistence:** restart ACT → both dropdowns remember their values.
- [ ] Report guild verdicts (palette-at-alpha, in-place feel) → backlog.

---

## Notes for the executor
- Enum defaults MUST stay the 0-values (DCJS skips initializers on deserialize).
- Renderers must not soften — Core resolves final colors now (soften only inside `ColorPolicy` for `ActColor`).
- All `[MAC]` except Task 5.
