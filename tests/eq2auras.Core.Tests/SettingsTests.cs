using System;
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class SettingsTests
{
    [Fact]
    public void Normalize_seeds_three_groups_with_panel_and_buff_sources()
    {
        var s = Settings.Parse("{}");
        Assert.Equal(3, s.Panels.Count);
        Assert.Equal(SourceRuleType.Panel, s.Panels[0].Sources[0].Type);
        Assert.Equal("1", s.Panels[0].Sources[0].Value);
        Assert.Equal("2", s.Panels[1].Sources[0].Value);
        Assert.Equal(SourceRuleType.Category, s.Panels[2].Sources[0].Type);
        Assert.Equal("eq2auras Buffs", s.Panels[2].Sources[0].Value);
    }

    [Fact]
    public void A_legacy_two_panel_file_migrates_forward_to_three_groups()
    {
        // A saved file from before the buff window: two panels, no sources, no buff group.
        var s = Settings.Parse("{\"panels\":[{\"colorSource\":0},{\"colorSource\":0}]}");
        Assert.Equal(3, s.Panels.Count);
        Assert.Equal("1", s.Panels[0].Sources[0].Value);
        Assert.Equal("eq2auras Buffs", s.Panels[2].Sources[0].Value);
    }

    [Fact]
    public void Buff_prefs_default_to_all_catalog_ids_enabled_and_no_override()
    {
        var s = Settings.Parse("{}");
        Assert.Equal(BuffCatalog.All.Select(b => b.Id).OrderBy(x => x),
                     s.EnabledBuffIds().OrderBy(x => x));
        Assert.All(s.BuffPrefs, p => Assert.Null(p.DurationOverride));
    }

    [Fact]
    public void An_explicit_empty_pref_list_is_preserved_as_none_enabled()
        => Assert.Empty(Settings.Parse("{\"buffPrefs\":[]}").EnabledBuffIds());

    [Fact]
    public void A_duration_override_wins_over_the_catalog_base()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true,\"durationOverride\":48}]}");
        Assert.Equal(48, s.EffectiveDuration("bolster"));
    }

    [Fact]
    public void Effective_duration_falls_back_to_the_catalog_base_with_no_override()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true}]}");
        Assert.Equal(36, s.EffectiveDuration("bolster"));   // census base
    }

    [Fact]
    public void A_freshly_constructed_settings_defaults_all_buffs_on()
    {
        // The missing-file / corrupt-file path returns new Settings() WITHOUT Normalize — the field
        // initializer must still yield all-on, else a fresh install injects nothing (code review Critical 1).
        Assert.Equal(BuffCatalog.All.Select(b => b.Id).OrderBy(x => x),
                     new Settings().EnabledBuffIds().OrderBy(x => x));
    }

    [Fact]
    public void An_out_of_range_duration_override_reverts_to_the_catalog_base()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true,\"durationOverride\":0}," +
                               "{\"id\":\"tsunami\",\"enabled\":true,\"durationOverride\":99999}]}");
        Assert.Equal(36, s.EffectiveDuration("bolster"));   // 0 -> base
        Assert.Equal(21, s.EffectiveDuration("tsunami"));   // 99999 -> base
    }

    [Fact]
    public void A_newer_catalog_buff_absent_from_a_saved_list_defaults_off()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true}]}");
        Assert.Contains("bolster", s.EnabledBuffIds());
        Assert.DoesNotContain("tsunami", s.EnabledBuffIds());    // present-but-off, not auto-enabled
        Assert.Equal(BuffCatalog.All.Count, s.BuffPrefs.Count);  // every catalog id now has a pref
    }

    [Fact]
    public void Sources_buff_prefs_and_a_fourth_group_survive_a_json_round_trip()
    {
        var s = new Settings();
        s.Panels.Add(new PanelSettings { Sources = new List<SourceRule> { SourceRule.OfName("Special") } });
        s.BuffPrefs.First(p => p.Id == "bolster").DurationOverride = 50;

        var parsed = Settings.Parse(s.ToJson());

        Assert.Equal(4, parsed.Panels.Count);                          // no truncation across persistence
        Assert.Equal("Special", parsed.Panels[3].Sources[0].Value);
        Assert.Equal("eq2auras Buffs", parsed.Panels[2].Sources[0].Value);
        Assert.Equal(50, parsed.EffectiveDuration("bolster"));         // override round-trips
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
        Assert.Equal(3, parsed.Panels.Count);
    }

    [Fact]
    public void Roundtrips_per_panel_knobs_and_positions()
    {
        var settings = new Settings();
        settings.Panels[0].ColorSource = ColorSource.Greyscale;
        settings.Panels[0].ListLeft = 42.5;
        settings.Panels[0].ListTop = 0;              // zero is a REAL position, must survive
        settings.Panels[1].EscalationStyle = EscalationStyle.HighlightInPlace;
        settings.Panels[1].CenterLeft = 900;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(3, parsed.Panels.Count);
        Assert.Equal(ColorSource.Greyscale, parsed.Panels[0].ColorSource);
        Assert.Equal(42.5, parsed.Panels[0].ListLeft);
        Assert.Equal(0.0, parsed.Panels[0].ListTop);
        Assert.Null(parsed.Panels[0].CenterLeft);    // unset stays null — never 0
        Assert.Equal(EscalationStyle.HighlightInPlace, parsed.Panels[1].EscalationStyle);
        Assert.Equal(900.0, parsed.Panels[1].CenterLeft);
        Assert.Null(parsed.Panels[1].ListLeft);
    }

    [Fact]
    public void Legacy_flat_file_seeds_panel_A_and_defaults_panel_B()
    {
        var parsed = Settings.Parse("{\"colorSource\":1,\"escalationStyle\":1}");

        Assert.Equal(3, parsed.Panels.Count);
        Assert.Equal(ColorSource.Greyscale, parsed.Panels[0].ColorSource);
        Assert.Equal(EscalationStyle.HighlightInPlace, parsed.Panels[0].EscalationStyle);
        Assert.Equal(ColorSource.Palette, parsed.Panels[1].ColorSource);
        Assert.Equal(EscalationStyle.CenterRadial, parsed.Panels[1].EscalationStyle);
        Assert.Null(parsed.Panels[0].ListLeft);
    }

    [Fact]
    public void Save_mirrors_panel_A_knobs_to_the_legacy_flat_fields()
    {
        var settings = new Settings();
        settings.Panels[0].ColorSource = ColorSource.ActColor;
        settings.Panels[0].EscalationStyle = EscalationStyle.HighlightInPlace;

        var json = settings.ToJson();

        // DCJS serializes unordered members alphabetically, so the FLAT knobs precede
        // the "panels" key. Without mirroring, the only ":2" would sit inside the
        // panels array (after it) — plain Contains could never fail.
        Assert.True(json.IndexOf("\"colorSource\":2", StringComparison.Ordinal)
            < json.IndexOf("\"panels\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"escalationStyle\":1", StringComparison.Ordinal)
            < json.IndexOf("\"panels\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"panels\":[]}")]                     // empty list
    [InlineData("{\"panels\":[{\"colorSource\":1}]}")]  // one entry
    [InlineData("{\"panels\":[{},{},{}]}")]             // three entries
    public void Short_panel_list_pads_up_to_the_three_seeded_groups(string json)
    {
        Assert.Equal(3, Settings.Parse(json).Panels.Count);
    }

    [Fact]
    public void Short_panel_list_keeps_existing_entries_and_pads_defaults()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"colorSource\":1}]}");

        Assert.Equal(ColorSource.Greyscale, parsed.Panels[0].ColorSource);
        Assert.Equal(ColorSource.Palette, parsed.Panels[1].ColorSource);
    }

    [Fact]
    public void Roundtrips_palette_font_and_dimensions()
    {
        var settings = new Settings();
        settings.PaletteArgb = new System.Collections.Generic.List<int> { -65536, -16711936 };
        settings.Panels[0].FontFamily = "Comic Sans MS";
        settings.Panels[0].FontBaseSize = 16.0;
        settings.Panels[0].RowWidth = 300.0;
        settings.Panels[0].RowHeight = 40.0;
        settings.Panels[1].RadialSize = 200.0;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(new[] { -65536, -16711936 }, parsed.PaletteArgb);
        Assert.Equal("Comic Sans MS", parsed.Panels[0].FontFamily);
        Assert.Equal(16.0, parsed.Panels[0].FontBaseSize);
        Assert.Equal(300.0, parsed.Panels[0].RowWidth);
        Assert.Equal(40.0, parsed.Panels[0].RowHeight);
        Assert.Equal(200.0, parsed.Panels[1].RadialSize);
        Assert.Null(parsed.Panels[0].RadialSize);          // unset stays null — never 0
        Assert.Null(parsed.Panels[1].RowWidth);
        Assert.Null(parsed.Panels[1].FontFamily);
    }

    [Theory]
    [InlineData("{}")]                          // no palette key
    [InlineData("{\"paletteArgb\":[]}")]        // empty list
    public void Missing_or_empty_palette_yields_the_default_five(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.Equal(Eq2Auras.Core.Timers.ColorPolicy.DefaultPaletteArgb, parsed.PaletteArgb);
    }

    [Fact]
    public void Oversized_palette_truncates_to_max()
    {
        var seventeen = string.Join(",", new int[17]);
        var parsed = Settings.Parse("{\"paletteArgb\":[" + seventeen + "]}");

        Assert.Equal(16, parsed.PaletteArgb.Count);
    }

    [Fact]
    public void Out_of_range_dimensions_clamp_on_parse()
    {
        var parsed = Settings.Parse(
            "{\"panels\":[{\"rowWidth\":9999,\"rowHeight\":5},{\"radialSize\":10}]}");

        Assert.Equal(800.0, parsed.Panels[0].RowWidth);
        Assert.Equal(16.0, parsed.Panels[0].RowHeight);
        Assert.Equal(40.0, parsed.Panels[1].RadialSize);
    }

    [Fact]
    public void Roundtrips_grow_directions_and_spacing()
    {
        var settings = new Settings();
        settings.Panels[0].ListGrowDirection = GrowDirection.Up;
        settings.Panels[0].RowSpacing = 0.0;               // zero is MEANINGFUL (touching)
        settings.Panels[1].CenterGrowDirection = GrowDirection.Up;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(GrowDirection.Up, parsed.Panels[0].ListGrowDirection);
        Assert.Equal(GrowDirection.Down, parsed.Panels[0].CenterGrowDirection);
        Assert.Equal(0.0, parsed.Panels[0].RowSpacing);    // survives as 0, not null
        Assert.Equal(GrowDirection.Up, parsed.Panels[1].CenterGrowDirection);
        Assert.Null(parsed.Panels[1].RowSpacing);          // unset stays null (= default 4)
        Assert.Equal(GrowDirection.Down, parsed.Panels[1].ListGrowDirection);
    }

    [Fact]
    public void Missing_grow_and_spacing_fields_read_as_defaults()
    {
        // DCJS skips initializers: missing enum -> 0 (must mean Down); missing
        // nullable -> null (must mean "default 4", never a legal-looking 0).
        var parsed = Settings.Parse("{\"panels\":[{},{}]}");

        Assert.Equal(GrowDirection.Down, parsed.Panels[0].ListGrowDirection);
        Assert.Equal(GrowDirection.Down, parsed.Panels[0].CenterGrowDirection);
        Assert.Null(parsed.Panels[0].RowSpacing);
    }

    [Fact]
    public void Out_of_range_spacing_clamps_on_parse()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"rowSpacing\":99},{\"rowSpacing\":-3}]}");

        Assert.Equal(50.0, parsed.Panels[0].RowSpacing);
        Assert.Equal(0.0, parsed.Panels[1].RowSpacing);
    }

    [Fact]
    public void Retired_scale_keys_are_ignored()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"listScale\":1.5},{\"centerScale\":0.7}]}");

        Assert.Equal(3, parsed.Panels.Count);              // parses fine, keys dropped
        Assert.Null(parsed.Panels[0].RowWidth);
    }

    [Fact]
    public void Valid_palette_survives_normalize_untouched()
    {
        // Normalize must never rebuild a valid list: the engine reads the property per
        // tick on ACT's UI thread while saves (which call ToJson -> Normalize) can run
        // on the overlay thread — gratuitous list replacement would be a cross-thread
        // mutation of a list being enumerated.
        var settings = new Settings();
        var palette = settings.PaletteArgb;

        settings.ToJson();

        Assert.Same(palette, settings.PaletteArgb);
    }

    [Fact]
    public void DebugLogging_defaults_off_and_round_trips()
    {
        // DCJS 0-value rule: absent from an old settings.json -> false = off.
        Assert.False(Settings.Parse("{}").DebugLogging);

        var settings = new Settings { DebugLogging = true };
        Assert.True(Settings.Parse(settings.ToJson()).DebugLogging);
    }

    [Fact]
    public void BetaChannel_defaults_off_and_round_trips()
    {
        // DCJS 0-value rule: absent from an old settings.json -> false = stable channel.
        Assert.False(Settings.Parse("{}").BetaChannel);

        var settings = new Settings { BetaChannel = true };
        Assert.True(Settings.Parse(settings.ToJson()).BetaChannel);
    }
}
