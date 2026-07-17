# Parse Meter Slice 2b — Single Secondary Data Point — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one optional, per-window secondary data point to each meter row (a muted right-aligned column), on a jitter-proof fixed-column layout, with the header total capping the primary-value column and the cog moved to the header's left — and move the K/M/B number formatter to three significant figures.

**Architecture:** Core (netstandard2.0, TDD on Mac) gains the sig-figs formatter, a null-returning `MetricRegistry.Find`, a `secondaryKey` input to `MeterEngine.Tick` that computes one `SecondaryValue` per row, and a `MeterWindowConfig.SecondaryKey`. Plugin (net472/WPF, no Mac build — CI compile + on-box live-verify) gains a `MeterColumns` font-measurement helper, fixed-width right-aligned row columns with the secondary column, a restructured header (cog-left, total over the value column), host wiring that threads the key through `Tick`, and a settings-window dropdown.

**Tech Stack:** C# (netstandard2.0 Core + net472/WPF Plugin, single-assembly `<Compile Include>`), xUnit (`[Fact]`/`[Theory]`/`[InlineData]`), `DataContractJsonSerializer` (DCJS) for settings, WPF for the overlay.

## Global Constraints

- **Core is the only Mac-buildable project.** Run Core tests with `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. **Never build the Plugin or the solution on the Mac** (net472+WPF). Plugin tasks are verified by the branch's verify-only CI (WPF compile + artifact) and the on-box merge-gate live script at the end — they have no local test step.
- **Single-assembly packaging:** Core sources are `<Compile Include>`d into the Plugin. No second DLL, **no `async` in the Plugin project**, no non-GAC types in fields.
- **Never reference `System.Web.Extensions`.** JSON = `DataContractJsonSerializer`. DCJS skips field initializers on deserialize — a missing numeric materializes as `0`, so **nullable numerics** mean "unset, use default" (null, never zero); missing string → `null`.
- **WPF: retain elements, animate properties.** Column widths are **measured from the current font** (no hardcoded pixel widths) and **recomputed on font change** via the existing `SetFont` rebuild path. Every value **right-aligns within its fixed cell**; a value wider than the reserve **clips** (the meter's "configured size wins, oversized text clips" rule — SPEC §Element dimensions), so columns never move.
- **The sig-figs change is meter-wide** — it re-formats the primary value, the header total, and the secondary alike.
- **Secondary semantics:** exactly one, per-window, `None` by default; a new window resets it to `None` (data choice, not inherited); the same metric as primary is **allowed** and renders twice (no suppression); an unknown/missing key resolves to **off** (via `MetricRegistry.Find`, which returns `null` — **not** `Resolve`, which defaults to DPS).
- **Sort stays primary-only** — the secondary never enters the comparator.
- Spec: `docs/SPEC.md` Part III (§The metric registry, §Header, §Configuration, §Multiple windows, §Rows, §Settings). Branch: `meter-slice2b-secondary` (spec amendment already committed there).
- **Commit format:** `<Prefix>: <description>` (e.g. `Core:`, `Plugin:`), matching the repo's descriptive-prefix history — no ticket numbers.

## Plan-watch items to land (from the spec review)

1. **Count-column width** — number columns reserve at the uniform 5-char worst case (font-measured wider of `9.99M` and `99999`), **not only** `9.99M` (Task 4; a `cures` primary and a `cures` secondary both fit that reserve).
2. **`MeterEngine.Tick` gains a secondary-key input**, threaded through every caller (Task 2 defines it; Task 6 threads the host caller).
3. **Sort stays primary-only** with a secondary present (Task 2, asserted by test).

## File structure

**Core (modify):**
- `src/eq2auras.Core/Meter/NumberFormat.cs` — `Scaled` becomes magnitude-aware (3 sig figs).
- `src/eq2auras.Core/Meter/MetricRegistry.cs` — add `Find` (null-returning lookup).
- `src/eq2auras.Core/Meter/MeterEngine.cs` — `Tick` gains `secondaryKey`; populate `Secondaries`.
- `src/eq2auras.Core/Config/MeterWindowConfig.cs` — add `SecondaryKey`.

**Core tests (modify):**
- `tests/eq2auras.Core.Tests/MetricRegistryTests.cs` — update one abbreviation case, add sig-figs + `Find` cases.
- `tests/eq2auras.Core.Tests/MeterEngineTests.cs` — secondary-population cases.
- `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` — `SecondaryKey` round-trip.

**Plugin (create):**
- `src/eq2auras.Plugin/Overlay/MeterColumns.cs` — font-measured column-width helper.

**Plugin (modify):**
- `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` — fixed columns + secondary column.
- `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — header restructure; ctor `secondaryKey`; settings wiring.
- `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` — add `SecondaryPicked`.
- `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` — secondary dropdown.
- `src/eq2auras.Plugin/Overlay/OverlayHost.cs` — thread `SecondaryKey` into ctor + `Tick`.
- `src/eq2auras.Plugin/eq2auras.Plugin.csproj` — add the new `MeterColumns.cs` compile include if the csproj enumerates files (verify; skip if it globs).

---

### Task 1: Three-significant-figures number formatter (Core, TDD)

**Files:**
- Modify: `src/eq2auras.Core/Meter/NumberFormat.cs:21-22` (the `Scaled` helper)
- Test: `tests/eq2auras.Core.Tests/MetricRegistryTests.cs:6-15` (the `Abbreviates_with_kmb_family` theory)

**Interfaces:**
- Consumes: nothing new.
- Produces: `NumberFormat.Abbreviate(double)` now emits 3 sig figs in the K/M/B bands (`1.24M`, `12.4M`, `124M`) and integer rounding below 1K (unchanged there). `NumberFormat.Integer(double)` is unchanged.

- [ ] **Step 1: Update + extend the failing test.** In `MetricRegistryTests.cs`, replace the `Abbreviates_with_kmb_family` theory's `[InlineData]` rows (lines 7-13) with the sig-figs expectations:

```csharp
    [Theory]
    [InlineData(0, "0")]
    [InlineData(7.5, "8")]              // sub-1K is integer-rounded, NOT 3-sig-figs "7.50"
    [InlineData(950, "950")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1K")]           // 1.00 -> trailing zeros dropped
    [InlineData(1_240, "1.24K")]        // <10 mantissa -> 2 decimals (3 sig figs)
    [InlineData(1_460, "1.46K")]        // was "1.5K" under the old one-decimal format
    [InlineData(12_400, "12.4K")]       // 10..100 mantissa -> 1 decimal
    [InlineData(124_000, "124K")]       // >=100 mantissa -> 0 decimals
    [InlineData(890_000, "890K")]
    [InlineData(1_240_000, "1.24M")]
    [InlineData(9_990_000, "9.99M")]    // the 5-char worst case
    [InlineData(12_400_000, "12.4M")]
    [InlineData(124_000_000, "124M")]
    [InlineData(1_400_000, "1.4M")]     // 1.40 -> trailing zero dropped
    [InlineData(4_200_000_000, "4.2B")]
    public void Abbreviates_with_three_sig_figs(double value, string expected)
        => Assert.Equal(expected, NumberFormat.Abbreviate(value));
```

- [ ] **Step 2: Run it to verify it fails.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter Abbreviates_with_three_sig_figs`
Expected: FAIL — e.g. `1_240` yields `"1.2K"` (old one-decimal), expected `"1.24K"`.

- [ ] **Step 3: Implement the magnitude-aware `Scaled`.** In `NumberFormat.cs`, replace the `Scaled` method (lines 21-22):

```csharp
        // Three significant figures: the decimal count falls as the leading part grows
        // (1.24 -> 12.4 -> 124), so the band string caps at four characters before the
        // suffix. Trailing zeros drop (0.## / 0.#), matching the abbreviation house style.
        private static string Scaled(double value)
        {
            double abs = Math.Abs(value);
            string format = abs < 10 ? "0.##" : abs < 100 ? "0.#" : "0";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
```

(The sub-1K branch of `Abbreviate` — `Math.Round(value).ToString(...)` at line 15 — is unchanged: integer rounding below 1K.)

- [ ] **Step 4: Run to verify it passes.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter Abbreviates_with_three_sig_figs`
Expected: PASS (16 cases).

- [ ] **Step 5: Fix the collateral assertion + run the whole formatter-touching set.** `MetricRegistryTests.Rates_are_rates_and_counts_are_counts` (line 48) asserts `Format(1_400_000) == "1.4M"` — still true (`1.40` → `1.4M`), no change needed. Run the full file to confirm nothing else regressed:

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter MetricRegistryTests`
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/eq2auras.Core/Meter/NumberFormat.cs tests/eq2auras.Core.Tests/MetricRegistryTests.cs
git commit -m "Core: K/M/B formatter to three significant figures (five-char cap)"
```

---

### Task 2: Secondary computation in MeterEngine (Core, TDD)

**Files:**
- Modify: `src/eq2auras.Core/Meter/MetricRegistry.cs` (add `Find`)
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs:17-82` (`Tick` signature + row loop)
- Test: `tests/eq2auras.Core.Tests/MetricRegistryTests.cs`, `tests/eq2auras.Core.Tests/MeterEngineTests.cs`

**Interfaces:**
- Consumes: `MetricDef.Select`, `MetricDef.IsRate`, `MetricDef.Format`; `MeterRow.Secondaries` (`List<SecondaryValue>` with `Key`, `FormattedValue`).
- Produces:
  - `MetricRegistry.Find(string key) → MetricDef` — the matching def, or `null` for null/unknown (**no** DPS fallback; distinct from `Resolve`).
  - `MeterEngine.Tick(EncounterReading, List<CombatantReading>, string metricKey, IReadOnlyList<int> paletteArgb, string secondaryKey = null) → MeterFrame` — each row's `Secondaries` holds **one** entry when `secondaryKey` resolves (via `Find`), else an empty list.

- [ ] **Step 1: Write the failing `Find` test.** In `MetricRegistryTests.cs`, add:

```csharp
    [Fact]
    public void Find_returns_the_metric_or_null_without_a_default()
    {
        Assert.Equal("enchps", MetricRegistry.Find("enchps").Key);
        Assert.Null(MetricRegistry.Find(null));            // no secondary
        Assert.Null(MetricRegistry.Find("no-such-metric")); // unknown -> off, NOT DPS
    }
```

- [ ] **Step 2: Run to verify it fails.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter Find_returns_the_metric_or_null_without_a_default`
Expected: FAIL — `MetricRegistry` has no `Find`.

- [ ] **Step 3: Add `Find`.** In `MetricRegistry.cs`, after `Resolve` (line 22):

```csharp
        /// The secondary's resolver: the matching def, or null for null/unknown — no
        /// DPS fallback, because an unresolved secondary means "off", not "show DPS"
        /// (SPEC Part III §Settings — the secondary key's forward-compat guard).
        public static MetricDef Find(string key)
            => All.FirstOrDefault(m => m.Key == key);
```

- [ ] **Step 4: Run to verify it passes.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter Find_returns_the_metric_or_null_without_a_default`
Expected: PASS.

- [ ] **Step 5: Write the failing engine tests.** In `MeterEngineTests.cs`, replace the `Rows_carry_the_secondaries_shape_empty_in_slice_1` test (lines 144-152) with these five (the first keeps the no-secondary assertion, renamed for accuracy):

```csharp
    [Fact]
    public void No_secondary_key_leaves_the_secondaries_list_empty()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 100) }, "encdps", Palette);   // no secondaryKey arg

        Assert.NotNull(frame.Rows[0].Secondaries);
        Assert.Empty(frame.Rows[0].Secondaries);
    }

    [Fact]
    public void A_selected_secondary_rides_each_row_computed_like_the_primary()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000, healed: 30_000) },
            "encdps", Palette, "enchps");

        var secondary = Assert.Single(frame.Rows[0].Secondaries);
        Assert.Equal("enchps", secondary.Key);
        Assert.Equal("300", secondary.FormattedValue);   // 30_000 / 100s, HPS is a rate
    }

    [Fact]
    public void A_count_secondary_is_not_divided_and_formats_as_an_integer()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000, cures: 7) },
            "encdps", Palette, "cures");

        Assert.Equal("7", Assert.Single(frame.Rows[0].Secondaries).FormattedValue);   // NOT 0.07
    }

    [Fact]
    public void The_secondary_may_equal_the_primary_and_simply_renders_twice()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) },
            "encdps", Palette, "encdps");

        Assert.Equal(frame.Rows[0].FormattedValue,
            Assert.Single(frame.Rows[0].Secondaries).FormattedValue);   // same DPS, twice — by design
    }

    [Fact]
    public void An_unknown_secondary_key_leaves_the_list_empty()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) },
            "encdps", Palette, "no-such-metric");

        Assert.Empty(frame.Rows[0].Secondaries);   // Find -> null -> off
    }

    [Fact]
    public void A_secondary_does_not_change_the_primary_sort_order()
    {
        // B has less DPS but more HPS; sort must stay by DPS (primary) regardless of the secondary.
        var allies = new List<CombatantReading>
        {
            Ally("A", damage: 900, healed: 100),
            Ally("B", damage: 100, healed: 900),
        };

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette, "enchps");

        Assert.Equal("A", frame.Rows[0].Name);   // DPS leader first, not the HPS leader
        Assert.Equal("B", frame.Rows[1].Name);
    }
```

- [ ] **Step 6: Run to verify they fail.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter MeterEngineTests`
Expected: FAIL to compile — `Tick` has no 5th parameter.

- [ ] **Step 7: Implement the secondary in `Tick`.** In `MeterEngine.cs`:

(a) Change the signature (line 17-18) to add the optional param:

```csharp
        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants,
            string metricKey, IReadOnlyList<int> paletteArgb, string secondaryKey = null)
        {
            var metric = MetricRegistry.Resolve(metricKey);
            var secondary = MetricRegistry.Find(secondaryKey);   // null -> no secondary
```

(b) Add a local rate/count helper just after `duration` is finalized (after line 32), so the primary and secondary share one duration-policy site (DRY):

```csharp
            double Compute(MetricDef def, CombatantReading c)
            {
                double raw = def.Select(c);
                return def.IsRate ? (duration > 0 ? raw / duration : 0) : raw;
            }
```

(c) In the first loop (lines 44-55), replace the primary-value computation and row construction to use `Compute` and to populate `Secondaries` inline (the combatant is in scope here — the second loop is not):

```csharp
            foreach (var combatant in all)
            {
                if (combatant.Name == "Unknown") continue;
                if (anyAlly && !combatant.IsAlly) continue;

                double value = Compute(metric, combatant);
                total += value;
                rows.Add(new MeterRow
                {
                    Name = combatant.Name ?? "",
                    Value = value,
                    Secondaries = secondary != null
                        ? new List<SecondaryValue> { new SecondaryValue { Key = secondary.Key, FormattedValue = secondary.Format(Compute(secondary, combatant)) } }
                        : new List<SecondaryValue>(),
                });
            }
```

(d) In the second loop, **remove** the line that reset `Secondaries` to an empty list (was line 71: `row.Secondaries = new List<SecondaryValue>();`) — it would clobber the entry set above. Leave the other per-row assignments (Percent, FormattedPercent, BarFraction, FormattedValue, FillArgb) intact.

- [ ] **Step 8: Run to verify all pass.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter MeterEngineTests`
Expected: PASS (all, including the untouched primary-behavior tests — they call the 4-arg overload, `secondaryKey` defaults to null).

- [ ] **Step 9: Run the full Core suite** (the formatter + engine changes ripple widely):

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 10: Commit.**

```bash
git add src/eq2auras.Core/Meter/MetricRegistry.cs src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/MetricRegistryTests.cs tests/eq2auras.Core.Tests/MeterEngineTests.cs
git commit -m "Core: MeterEngine computes one optional secondary per row (MetricRegistry.Find)"
```

---

### Task 3: `MeterWindowConfig.SecondaryKey` (Core, TDD)

**Files:**
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs`
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig.SecondaryKey` (`string`, DCJS name `secondaryKey`) — null/missing = no secondary; a plain string round-trip (no clamp; unknown resolves to off at the engine).

- [ ] **Step 1: Write the failing round-trip test.** In `MeterSettingsTests.cs`, after `Window_font_roundtrips` (line 165), add:

```csharp
    [Fact]
    public void Window_secondary_key_roundtrips_and_defaults_null()
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { MetricKey = "encdps", SecondaryKey = "enchps" },
            new MeterWindowConfig { MetricKey = "encdps" },   // no secondary
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal("enchps", parsed.Meter.Windows[0].SecondaryKey);
        Assert.Null(parsed.Meter.Windows[1].SecondaryKey);    // missing -> null -> off
    }
```

- [ ] **Step 2: Run to verify it fails.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter Window_secondary_key_roundtrips_and_defaults_null`
Expected: FAIL — `MeterWindowConfig` has no `SecondaryKey`.

- [ ] **Step 3: Add the property.** In `MeterWindowConfig.cs`, after the `MetricKey` member (lines 14-15):

```csharp
        [DataMember(Name = "secondaryKey")]
        public string SecondaryKey { get; set; }   // null/unknown -> no secondary (off), resolved at the engine via MetricRegistry.Find
```

- [ ] **Step 4: Run to verify it passes.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter Window_secondary_key_roundtrips_and_defaults_null`
Expected: PASS.

- [ ] **Step 5: Run the full Core suite.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Core: MeterWindowConfig.SecondaryKey (per-window secondary metric, null = off)"
```

---

### Task 4: Font-measured columns + secondary column in the row visual (Plugin — no Mac build)

> Plugin tasks compile only in CI. After the code steps, the "verify" is a green branch CI run and the on-box live script (final section). Do not run `dotnet build` on the Plugin/solution locally.

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/MeterColumns.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`
- Verify: `src/eq2auras.Plugin/eq2auras.Plugin.csproj` picks up the new file (see Step 5)

**Interfaces:**
- Produces:
  - `MeterColumns.NumberWidth(VisualStyle style, double fontSize) → double` — measured px width of the wider of `"9.99M"` and `"99999"` at `fontSize`.
  - `MeterColumns.PercentWidth(VisualStyle style, double fontSize) → double` — measured px width of `"100%"`.
  - `MeterRowVisual.Update(MeterRow)` now also renders `row.Secondaries[0]` (0-or-1) in a muted fixed-width column; the value and secondary columns share `NumberWidth`, the percent uses `PercentWidth`; all three right-align; the secondary column collapses when empty.

- [ ] **Step 1: Create `MeterColumns.cs`.**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;

namespace Eq2Auras.Plugin.Overlay
{
    /// Fixed row-column widths, measured from the window's current font (SPEC Part III
    /// §Rows) — no hardcoded pixels, so a font/size change just re-measures. Number
    /// columns (value + secondary) reserve the wider of the sig-figs cap and a five-digit
    /// count so a rate column and a count column reserve identically; the percent reserves
    /// "100%". A value wider than its reserve clips in its cell (the row never widens).
    internal static class MeterColumns
    {
        private const string RateCap = "9.99M";    // three-sig-figs worst case
        private const string CountCap = "99999";   // five-digit count worst case
        private const string PercentCap = "100%";

        public static double NumberWidth(VisualStyle style, double fontSize)
            => Math.Max(Measure(style, RateCap, fontSize), Measure(style, CountCap, fontSize));

        public static double PercentWidth(VisualStyle style, double fontSize)
            => Measure(style, PercentCap, fontSize);

        private static double Measure(VisualStyle style, string sample, double fontSize)
        {
            var probe = new TextBlock { Text = sample };
            style.ApplyFont(probe, fontSize);
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(probe.DesiredSize.Width);
        }
    }
}
```

- [ ] **Step 2: Add the secondary column + fix the value/percent widths in `MeterRowVisual.cs`.** Replace the ctor body region that builds `_percent` and adds it to the trailing panel (current lines 37-44 — the `_percent` block through `_bar.TrailingPanel.Children.Add(_percent)`, leaving the blank line 45 and `SetOpacity(opacity)` at line 46 intact) so the trailing panel becomes three fixed, right-aligned columns in order `[secondary][percent][value]`. First add a field for the secondary and its color constant after the `_backplate` field (line 26):

```csharp
        private readonly TextBlock _secondary;
        private static readonly Color MutedText = Color.FromArgb(255, 0x9A, 0xA0, 0xAD);   // subordinate to the value
```

Then replace lines 37-44 (the `_percent` creation + `TrailingPanel.Children.Add(_percent)`) with:

```csharp
            double numberWidth = MeterColumns.NumberWidth(style, style.RowText);
            double percentWidth = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);

            // The value is the shared trailing text: pin it as the right-edge column.
            _bar.TrailingText.Width = numberWidth;
            _bar.TrailingText.TextAlignment = TextAlignment.Right;

            _percent = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = percentWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0)
            };
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);   // dimmer, slightly smaller

            _secondary = new TextBlock
            {
                Foreground = new SolidColorBrush(MutedText),
                VerticalAlignment = VerticalAlignment.Center,
                Width = numberWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Collapsed   // shown only when a secondary is selected
            };
            style.ApplyFont(_secondary, style.RowText);

            // Panel currently holds [value]; insert so the order becomes [secondary][percent][value].
            _bar.TrailingPanel.Children.Insert(0, _percent);
            _bar.TrailingPanel.Children.Insert(0, _secondary);
```

- [ ] **Step 3: Render the secondary in `Update`.** Replace `MeterRowVisual.Update` (current lines 49-56):

```csharp
        public void Update(MeterRow row)
        {
            _bar.NameText.Text = row.Name;
            _bar.TrailingText.Text = row.FormattedValue;
            _percent.Text = row.FormattedPercent;

            if (row.Secondaries != null && row.Secondaries.Count > 0)
            {
                _secondary.Text = row.Secondaries[0].FormattedValue;
                _secondary.Visibility = Visibility.Visible;
            }
            else
            {
                _secondary.Visibility = Visibility.Collapsed;
            }

            _bar.SetFillColor(row.FillArgb);
            _bar.AnimateToFraction(row.BarFraction);
        }
```

- [ ] **Step 4: Re-measure columns on font change.** Replace `MeterRowVisual.SetFont` (current lines 77-82):

```csharp
        public void SetFont(VisualStyle style)
        {
            style.ApplyFont(_bar.NameText, style.RowText);
            style.ApplyFont(_bar.TrailingText, style.RowText);
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);
            style.ApplyFont(_secondary, style.RowText);

            double numberWidth = MeterColumns.NumberWidth(style, style.RowText);
            _bar.TrailingText.Width = numberWidth;
            _secondary.Width = numberWidth;
            _percent.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
        }
```

- [ ] **Step 5: Verify csproj inclusion.** Check `src/eq2auras.Plugin/eq2auras.Plugin.csproj`: if it lists `<Compile Include>` items explicitly, add `<Compile Include="Overlay\MeterColumns.cs" />` alongside the other `Overlay\*.cs` entries; if it uses SDK-style globbing (no explicit `Overlay\*.cs` list), no edit is needed. (Also confirm the Core-test csproj / Core csproj need no change — they do not reference Plugin files.)

Run: `grep -c "Overlay\\\\MeterRowVisual.cs" src/eq2auras.Plugin/eq2auras.Plugin.csproj`
Expected: `1` → the csproj enumerates files, so add the `MeterColumns.cs` line. `0` → it globs, no edit.

- [ ] **Step 6: Commit.**

```bash
git add src/eq2auras.Plugin/Overlay/MeterColumns.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/eq2auras.Plugin.csproj
git commit -m "Plugin: fixed font-measured row columns + muted secondary column"
```

---

### Task 5: Header — cog to the left, total over the value column (Plugin — no Mac build)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (header assembly ~lines 93-121; `ApplyHeaderFont` ~lines 379-386)

**Interfaces:**
- Consumes: `MeterColumns.NumberWidth` (Task 4).
- Produces: header laid out as `[⚙ cog | (dur) title — metric | total]`; the total is a fixed-width right-aligned column equal to the row's `NumberWidth`, so it caps the value column; the cog leads the header.

- [ ] **Step 1: Restructure the header assembly.** In the ctor, the cog affordance is created at lines 93-101 and the `rightPanel` (lines 102-110) currently holds `[_totalText, affordance]`. Replace the `rightPanel` block (lines 102-110) and the outer `headerGrid` block (lines 115-121) with a three-column layout — cog (auto, left), `leftGrid` (star), total (auto, right):

```csharp
            // Total is a fixed-width right-aligned column matching the row's value column,
            // so it caps the column it sums (SPEC Part III §Header). The cog leads the
            // header to free the right edge for that alignment.
            _totalText.Width = MeterColumns.NumberWidth(style, style.RowText);
            _totalText.TextAlignment = TextAlignment.Right;

            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // cog
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // (dur) title — metric
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // total
            Grid.SetColumn(affordance, 0);
            Grid.SetColumn(leftGrid, 1);
            Grid.SetColumn(_totalText, 2);
            affordance.Margin = new Thickness(0, 0, 6 * hr, 0);   // gap between cog and the duration
            headerGrid.Children.Add(affordance);
            headerGrid.Children.Add(leftGrid);
            headerGrid.Children.Add(_totalText);
```

(Delete the old `rightPanel` `StackPanel` entirely — the cog and total are now direct children of `headerGrid`. Keep the `affordance` creation at lines 93-101 unchanged; it now lands in column 0.)

- [ ] **Step 2: Re-measure the total on font change.** In `ApplyHeaderFont` (lines 379-386), after the existing `ApplyFont` calls, re-pin the total's width so it tracks the font:

```csharp
        private void ApplyHeaderFont()
        {
            _style.ApplyFont(_durationText, _style.RowText);
            _style.ApplyFont(_titleText, _style.RowText);
            _style.ApplyFont(_metricText, _style.RowText);
            _style.ApplyFont(_totalText, _style.RowText);
            _style.ApplyFont(_affordance, _style.RowText);
            _totalText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
        }
```

- [ ] **Step 3: Commit.**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Plugin: meter header — cog leads, total caps the value column"
```

---

### Task 6: Host wiring — thread the secondary key through the ctor and Tick (Plugin — no Mac build)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` (add `SecondaryPicked`)
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (ctor `secondaryKey` param + field)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (pass `SecondaryKey` to ctor + `Tick`; wire the callback)

**Interfaces:**
- Consumes: `MeterWindowConfig.SecondaryKey` (Task 3); `MeterEngine.Tick(..., secondaryKey)` (Task 2).
- Produces: `MeterWindow` holds `_secondaryKey` (for the settings dropdown in Task 7); `MeterWindowCallbacks.SecondaryPicked(string)`; the host's per-poll `Tick` uses `config.SecondaryKey`.

- [ ] **Step 1: Add the callback.** In `MeterWindowCallbacks.cs`, after `MetricPicked` (line 11):

```csharp
        public Action<string> SecondaryPicked;   // null = None
```

- [ ] **Step 2: Add the ctor param + field in `MeterWindow.cs`.** Add a field after `_metricKey` (line 47):

```csharp
        private string _secondaryKey;   // null = None; the settings dropdown's current value
```

Change the ctor signature (lines 50-51) to accept it after `metricKey`, and store it (after line 56):

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, string secondaryKey, bool locked, double opacity, int visibleRows,
            MeterWindowCallbacks callbacks)
            : base(left, top, GrowDirection.Down, callbacks.PersistPosition, clickThroughBaseline: false)
        {
            _cb = callbacks;
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _secondaryKey = secondaryKey;                         // null/unknown -> None (no Resolve; off, not DPS)
```

- [ ] **Step 3: Pass it from the host ctor call + thread `Tick`.** In `OverlayHost.cs`:

(a) In `AddMeterWindow` (the `new MeterWindow(...)` at lines 103-110), pass `config.SecondaryKey` after `config.MetricKey`:

```csharp
            var window = new MeterWindow(
                config.Left ?? DefaultMeterLeft(style),
                config.Top ?? DefaultMeterTop,
                style,
                config.MetricKey,
                config.SecondaryKey,
                config.Locked,
                config.Opacity ?? MeterSettings.DefaultOpacity,
                config.VisibleRows ?? MeterWindow.DefaultVisibleRows,
```

(b) In the `MeterWindowCallbacks` object (lines 111-123), add after `MetricPicked` (line 114):

```csharp
                    SecondaryPicked = key => SettingsStore.Update(_settings, () => config.SecondaryKey = key),
```

(c) In `UpdateMeterSample`, thread the key into `Tick` (line 220):

```csharp
                    var frame = _meterEngine.Tick(encounter, combatants, pair.Key.MetricKey, paletteArgb, pair.Key.SecondaryKey);
```

(d) Confirm `AddNewWindow` (lines 133-149) is **unchanged** — it copies only `RowHeight`/`FontFamily`/`FontBaseSize`/`Opacity`, so `SecondaryKey` stays null on a new window (SPEC: new window → None). Add a one-line comment noting the deliberate omission.

- [ ] **Step 4: Commit.**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Plugin: thread per-window secondary key through the ctor and Tick"
```

---

### Task 7: Settings-window secondary dropdown (Plugin — no Mac build)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` (add the dropdown row + ctor param)
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (`OpenSettings` passes the current key + a setter)

**Interfaces:**
- Consumes: `MetricRegistry.All` (for the option list), `MeterWindowCallbacks.SecondaryPicked` (Task 6).
- Produces: a `Secondary` `ComboBox` (`None` + each metric label), `None` default, live-applying; `MeterWindow.SetSecondary(string)` updates `_secondaryKey` and persists via `_cb.SecondaryPicked`.

- [ ] **Step 1: Add the ctor param + dropdown to `MeterSettingsWindow.cs`.** Add fields after `_onFontChanged` (line 21):

```csharp
        private readonly Action<string> _onSecondaryChanged;
```

Extend the ctor signature (lines 25-26) with the current key + callback (append both parameters):

```csharp
        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged,
            string fontFamily, double fontBaseSize, Action<string, double> onFontChanged,
            string secondaryKey, Action<string> onSecondaryChanged)
        {
            _onSecondaryChanged = onSecondaryChanged;
```

Build the row after the `fontRow` block (after line 138), using a `ComboBox` whose first item is `None` (tag `null`) and the rest the registry metrics (tag = key). Select the item matching `secondaryKey`:

```csharp
            var secondaryLabel = new TextBlock
            {
                Text = "Secondary",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            var secondary = new ComboBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
            secondary.Items.Add(new ComboBoxItem { Content = "None", Tag = null });
            foreach (var metric in Eq2Auras.Core.Meter.MetricRegistry.All)
            {
                secondary.Items.Add(new ComboBoxItem { Content = metric.Label, Tag = metric.Key });
            }
            secondary.SelectedIndex = 0;
            for (int i = 0; i < secondary.Items.Count; i++)
            {
                if ((string)((ComboBoxItem)secondary.Items[i]).Tag == secondaryKey) { secondary.SelectedIndex = i; break; }
            }
            secondary.SelectionChanged += (s, e) =>
                _onSecondaryChanged((string)((ComboBoxItem)secondary.SelectedItem).Tag);
            var secondaryRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            secondaryRow.Children.Add(secondaryLabel);
            secondaryRow.Children.Add(secondary);
```

Add it to the body between `fontRow` and `opacityRow` (the body assembly is lines 199-203) — insert after `body.Children.Add(fontRow);`:

```csharp
            body.Children.Add(secondaryRow);
```

(Note: "Reset to defaults" (lines 187-197) resets the settings-window knobs — row height, opacity, font. The secondary is a **data** choice, sibling to the metric on the right-click menu, which Reset also does not touch; leave it out of Reset. Record this "no change" in the commit message so the reviewer sees it is deliberate.)

- [ ] **Step 2: Add `SetSecondary` + pass it from `OpenSettings` in `MeterWindow.cs`.** Add a method near `SetFont`:

```csharp
        /// Live secondary selection (SPEC Part III §Configuration): persist the per-window
        /// key; the next poll's Tick reads config.SecondaryKey and the column appears/clears
        /// from the frame data (same apply-on-next-poll path as the metric picker).
        public void SetSecondary(string key)
        {
            _secondaryKey = key;
            _cb.SecondaryPicked(key);
        }
```

Update the `MeterSettingsWindow` construction in `OpenSettings` (lines 324-325) to pass the current key + setter:

```csharp
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity,
                _style.Font?.Source, _style.BaseSize, SetFont, _secondaryKey, SetSecondary)
```

- [ ] **Step 3: Commit.**

```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Plugin: settings-window Secondary dropdown (None default, live-apply; not in Reset — a data choice)"
```

---

### Task 8: Branch CI + on-box merge-gate live verification

The Plugin cannot be built or run on the Mac. Verification is the branch's verify-only CI (compile + artifact) plus the on-box live script Alex runs.

- [ ] **Step 1: Push the branch and watch CI.**

```bash
git push -u origin meter-slice2b-secondary
gh run watch "$(gh run list --branch meter-slice2b-secondary --limit 1 --json databaseId --jq '.[0].databaseId')" --exit-status
```
Expected: green — Core tests pass and the WPF plugin compiles + artifacts.

- [ ] **Step 2: Record the merge-gate live script** (Alex runs this on the Windows box against the branch artifact — mirrors SPEC Part III "Testing strategy (Parse Meter slice 2b)"):
  1. **Pick a secondary** — open a meter window's ⚙ settings, set `Secondary` to HPS → a **muted, right-aligned column** appears left of the percent and lines up down every row; values read in **three significant figures** (`1.24M`, `12.4M`).
  2. **None clears it** — set `Secondary` back to `None` → the column disappears; no gap.
  3. **Same as primary renders twice** — set primary DPS and secondary DPS → the DPS value shows twice on each row, no crash.
  4. **Count secondary** — set `Secondary` to Cures → integer counts (no abbreviation), right-aligned in the same column.
  5. **Font re-measures** — change the window font/size → the number columns re-measure and stay aligned; no jitter as digits change (`9%`→`100%`, `98K`→`1.24M`).
  6. **Total caps the column** — the header total sits directly above the primary-value column; the **cog is at the header's far left**.
  7. **New window → None** — New meter window → its secondary starts at `None` (not inherited), even if the source had one; appearance (row height/font/opacity) still carries.
  8. **Persistence** — reload the plugin ("Check for updates" / re-enable) → each window's secondary selection survives.
  9. **Timer sanity** — the timer overlay is unchanged (slice 2b re-extracts nothing from the shared substrate: light check only — a timer fires, drains, colors correct).

- [ ] **Step 3: Present at the owner's merge gate** — branch is ready-for-review, not ready-to-merge. The plan's own third-party review runs first (this plan → review loop); then Alex's merge call.

---

## Self-Review

**Spec coverage** (each SPEC Part III slice-2b claim → task):
- Single secondary, single-select dropdown, None default → Tasks 3 (config), 7 (dropdown); engine 2.
- Muted right-aligned column left of percent → Task 4.
- Font-measured fixed columns, uniform 5-char reserve (wider of `9.99M`/`99999`), clip beyond → Task 4 (`MeterColumns`).
- Primary value right-edge anchor; header total caps it; cog to far-left → Task 5.
- 3-sig-figs formatter (integer below 1K) → Task 1.
- Secondary is a per-window data choice; new window → None → Task 6 Step 3(d).
- Same metric as primary allowed (renders twice) → Task 2 (test + no suppression).
- Unknown/missing secondary → off (Find, not Resolve) → Task 2.
- `MeterWindowConfig.secondaryKey` DCJS, missing → null → Task 3.
- Sort primary-only → Task 2 (test).
- Plan-watch items 1–3 → Tasks 4, 2/6, 2.

**Placeholder scan:** none — every step carries the exact code or command.

**Type consistency:** `Tick(..., string secondaryKey = null)` (Task 2) matches the host call in Task 6(c) and the test calls in Task 2. `MetricRegistry.Find` (Task 2) is consumed by `MeterEngine` (Task 2) and the dropdown lists `MetricRegistry.All` (Task 7). `MeterColumns.NumberWidth`/`PercentWidth` (Task 4) are consumed by `MeterRowVisual` (Task 4) and the header (Task 5). `MeterWindowCallbacks.SecondaryPicked` (Task 6) is consumed by `OverlayHost` (Task 6) and invoked by `MeterWindow.SetSecondary` (Task 7). `MeterWindow` ctor gains `secondaryKey` (Task 6) and the host passes it (Task 6). `MeterWindowConfig.SecondaryKey` (Task 3) is read by the host ctor call and `Tick` (Task 6) and written by `SecondaryPicked` (Task 6).
