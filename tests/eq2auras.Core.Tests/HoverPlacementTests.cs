using Eq2Auras.Core.Overlay;
using Xunit;

public class HoverPlacementTests
{
    private static HoverRect R(double left, double top, double width, double height)
        => new HoverRect { Left = left, Top = top, Width = width, Height = height };

    // Common card/screen for the readable cases: 300x120 card, 1920x1080 screen,
    // topInset 30, bottomInset 10, gap 8.
    private static HoverPoint Place(HoverRect host, HoverRect anchor, bool preferLeft = true, bool growUp = true,
        double cardW = 300, double cardH = 120, double screenW = 1920, double screenH = 1080)
        => HoverPlacement.Compute(host, anchor, cardW, cardH, screenW, screenH,
            topInset: 30, bottomInset: 10, gap: 8, preferLeft: preferLeft, growUp: growUp);

    [Fact]
    public void PreferLeft_when_left_fits_places_left_and_grows_up_last_row_on_row_bottom()
    {
        var p = Place(host: R(1000, 800, 260, 200), anchor: R(1000, 850, 260, 26));
        Assert.Equal(692, p.Left);                 // 1000 - 8 - 300
        Assert.Equal(766, p.Top);                  // rowBottom(876) - 120 + 10
        Assert.Equal(876, p.Top + 120 - 10);       // last row bottom == row bottom
    }

    [Fact]
    public void PreferLeft_when_left_overflows_flips_right()
    {
        var p = Place(host: R(100, 800, 260, 200), anchor: R(100, 850, 260, 26));
        Assert.Equal(368, p.Left);                 // right(360) + 8
    }

    [Fact]
    public void When_neither_side_fits_keeps_preferred_then_clamps_on_screen()
    {
        var p = Place(host: R(50, 800, 260, 40), anchor: R(50, 810, 260, 26), screenW: 400);
        Assert.Equal(0, p.Left);                   // leftX -258 kept, clamped to 0
    }

    [Fact]
    public void GrowUp_off_the_top_falls_back_to_grow_down_first_row_on_row_top()
    {
        var p = Place(host: R(1000, 30, 260, 200), anchor: R(1000, 50, 260, 26));
        Assert.Equal(20, p.Top);                   // yUp -34 -> yDown = rowTop(50) - 30
        Assert.Equal(50, p.Top + 30);              // first row top == row top
    }

    [Fact]
    public void GrowDown_preference_when_down_fits_first_row_on_row_top()
    {
        var p = Place(host: R(1000, 100, 260, 200), anchor: R(1000, 100, 260, 26), growUp: false);
        Assert.Equal(70, p.Top);                   // yDown = rowTop(100) - 30
        Assert.Equal(100, p.Top + 30);             // first row top == row top
    }

    [Fact]
    public void GrowDown_off_the_bottom_falls_back_to_grow_up()
    {
        var p = Place(host: R(1000, 900, 260, 200), anchor: R(1000, 1000, 260, 26), growUp: false);
        Assert.Equal(916, p.Top);                  // yDown 970 overflows -> yUp = rowBottom(1026) - 120 + 10
    }

    [Fact]
    public void PreferRight_when_right_fits_places_right()
    {
        var p = Place(host: R(200, 800, 260, 200), anchor: R(200, 850, 260, 26), preferLeft: false);
        Assert.Equal(468, p.Left);                 // right(460) + 8
    }

    [Fact]
    public void GrowUp_that_would_overflow_the_bottom_is_clamped_up()
    {
        var p = Place(host: R(1000, 1040, 260, 40), anchor: R(1000, 1049, 260, 26), cardH: 50);
        Assert.Equal(1030, p.Top);                 // yUp 1035 -> clamped to screenH(1080) - 50
    }

    [Fact]
    public void No_input_yields_an_off_screen_card()
    {
        var p = Place(host: R(-500, 2000, 260, 40), anchor: R(-500, 2010, 260, 26));
        Assert.InRange(p.Left, 0, 1920 - 300);
        Assert.InRange(p.Top, 0, 1080 - 120);
    }
}
