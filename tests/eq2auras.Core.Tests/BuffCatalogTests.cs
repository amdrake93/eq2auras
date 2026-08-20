using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class BuffCatalogTests
{
    [Fact]
    public void The_category_is_the_reserved_namespace()
        => Assert.Equal("eq2auras Buffs", BuffCatalog.Category);

    [Fact]
    public void Seeds_the_twenty_two_v1_buffs()
    {
        var ids = BuffCatalog.All.Select(b => b.Id).ToList();
        Assert.Equal(22, ids.Count);
        Assert.Equal(12, BuffCatalog.All.Count(b => b.IsTargeted));
        Assert.Equal(10, BuffCatalog.All.Count(b => !b.IsTargeted));
        Assert.Contains("bolster", ids);
        Assert.Contains("advance-warning", ids);
    }

    [Fact]
    public void Every_buff_has_a_positive_duration_and_a_pattern()
        => Assert.All(BuffCatalog.All, b =>
        {
            Assert.True(b.DurationSeconds > 0);
            Assert.False(string.IsNullOrEmpty(b.Pattern));
        });

    [Fact]
    public void Ids_are_unique()
        => Assert.Equal(BuffCatalog.All.Count, BuffCatalog.All.Select(b => b.Id).Distinct().Count());

    [Fact]
    public void A_single_target_buff_captures_the_target_from_the_payload()
    {
        var bolster = BuffCatalog.Find("bolster");
        Assert.True(bolster.IsTargeted);
        var line = "(1734900000)[Wed Aug 19 20:00:00 2026] Alex says to the group, \"eq2auras Bolster Bob\"";
        Assert.True(bolster.TryMatch(line, out var name));
        Assert.Equal("Bob", name);   // the target
    }

    [Theory]
    [InlineData("says to the group")]
    [InlineData("says to the raid party")]
    [InlineData("tells you")]
    public void A_group_wide_buff_captures_the_caster_from_any_channel(string wrapper)
    {
        var turtle = BuffCatalog.Find("tortoise-shell");
        Assert.False(turtle.IsTargeted);
        // Real EQ2 chat format (spike-data/2026-08-09): \aPC <id> <Name>:<Name>\/a <wrapper>, "…"
        var line = $"(1786327033)[Sun Aug  9 20:57:13 2026] \\aPC 111782 Onlyfans:Onlyfans\\/a {wrapper}, \"eq2auras Tortoise Shell\"";
        Assert.True(turtle.TryMatch(line, out var name));
        Assert.Equal("Onlyfans", name);   // the caster — any channel (SPEC §Display)
    }

    [Fact]
    public void A_markup_stripped_group_wide_line_still_captures_the_caster()
    {
        // The (?:\/a)? optional lets it tolerate ACT delivering a markup-stripped line.
        var line = "(1786327033)[Sun Aug  9 20:57:13 2026] Onlyfans says to the group, \"eq2auras Tortoise Shell\"";
        Assert.True(BuffCatalog.Find("tortoise-shell").TryMatch(line, out var name));
        Assert.Equal("Onlyfans", name);
    }

    [Fact]
    public void A_non_matching_line_is_rejected()
        => Assert.False(BuffCatalog.Find("bolster").TryMatch("(1734900000)[date] Alex says, \"hello\"", out _));

    [Fact]
    public void Find_by_id_and_by_name_both_resolve_the_same_entry()
    {
        Assert.Null(BuffCatalog.Find("nope"));
        Assert.Equal("Jester's Cap", BuffCatalog.Find("jesters-cap").DisplayName);
        Assert.Equal("jesters-cap", BuffCatalog.FindByName("Jester's Cap").Id);
        Assert.Equal("jesters-cap", BuffCatalog.FindByName("jester's cap").Id);   // case-insensitive
        Assert.Null(BuffCatalog.FindByName("Not A Buff"));
    }
}
