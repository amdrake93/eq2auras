using System.Collections.Generic;
using Eq2Auras.Core.Timers;
using Xunit;

public class SourceRulesTests
{
    private static TimerReading Reading(bool a = false, bool b = false, string category = "", string name = "x")
        => new TimerReading { Name = name, Category = category, ShowInPanelA = a, ShowInPanelB = b };

    [Fact]
    public void Panel_type_is_the_zero_value()
        => Assert.Equal(0, (int)SourceRuleType.Panel);

    [Theory]
    [InlineData("1", true, false, true)]
    [InlineData("2", true, false, false)]
    [InlineData("2", false, true, true)]
    [InlineData("1", false, false, false)]
    public void Panel_rule_matches_the_named_panel_flag(string value, bool a, bool b, bool expected)
        => Assert.Equal(expected, SourceRules.Matches(Reading(a: a, b: b), SourceRule.Panel(int.Parse(value))));

    [Fact]
    public void Category_rule_matches_case_insensitively()
    {
        var rule = SourceRule.OfCategory("eq2auras Buffs");
        Assert.True(SourceRules.Matches(Reading(category: "eq2auras Buffs"), rule));
        Assert.True(SourceRules.Matches(Reading(category: "EQ2AURAS BUFFS"), rule));
        Assert.False(SourceRules.Matches(Reading(category: "Cooldowns"), rule));
    }

    [Fact]
    public void Name_rule_matches_the_timer_name_case_insensitively()
    {
        var rule = SourceRule.OfName("Bloodlust");
        Assert.True(SourceRules.Matches(Reading(name: "bloodlust"), rule));
        Assert.False(SourceRules.Matches(Reading(name: "Turtle Shell"), rule));
    }

    [Fact]
    public void Matches_any_is_a_union_over_a_windows_rules()
    {
        // The litmus test: a group bound to TWO rules (a category AND a name) catches
        // a reading matching EITHER, with zero new code beyond the generic predicate.
        var rules = new List<SourceRule> { SourceRule.OfCategory("eq2auras Buffs"), SourceRule.OfName("Special") };
        Assert.True(SourceRules.MatchesAny(rules, Reading(category: "eq2auras Buffs", name: "Bloodlust")));
        Assert.True(SourceRules.MatchesAny(rules, Reading(category: "other", name: "Special")));
        Assert.False(SourceRules.MatchesAny(rules, Reading(category: "other", name: "Nope")));
    }

    [Fact]
    public void Empty_or_null_rule_list_matches_nothing()
    {
        Assert.False(SourceRules.MatchesAny(null, Reading(a: true)));
        Assert.False(SourceRules.MatchesAny(new List<SourceRule>(), Reading(a: true)));
    }
}
