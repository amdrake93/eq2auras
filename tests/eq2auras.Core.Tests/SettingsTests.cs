using System;
using Eq2Auras.Core.Config;
using Xunit;

public class SettingsTests
{
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
        Assert.Equal(2, parsed.Panels.Count);
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

        Assert.Equal(2, parsed.Panels.Count);
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

        Assert.Equal(2, parsed.Panels.Count);
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
    public void Panel_list_normalizes_to_exactly_two(string json)
    {
        Assert.Equal(2, Settings.Parse(json).Panels.Count);
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
    public void Retired_scale_keys_are_ignored()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"listScale\":1.5},{\"centerScale\":0.7}]}");

        Assert.Equal(2, parsed.Panels.Count);              // parses fine, keys dropped
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
}
