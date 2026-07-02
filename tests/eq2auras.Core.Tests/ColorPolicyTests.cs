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
