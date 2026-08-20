using System.Collections.Generic;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class EscalationDefaultsTests
{
    private static PanelSettings Panel(EscalationStyle? style, params SourceRule[] sources)
        => new PanelSettings { EscalationStyle = style, Sources = new List<SourceRule>(sources) };

    [Fact]
    public void None_is_a_new_value_not_a_renumber()
    {
        Assert.Equal(0, (int)EscalationStyle.CenterRadial);
        Assert.Equal(1, (int)EscalationStyle.HighlightInPlace);
        Assert.Equal(2, (int)EscalationStyle.None);
    }

    [Fact]
    public void Null_on_the_buff_category_group_resolves_to_None()
        => Assert.Equal(EscalationStyle.None,
            EscalationDefaults.Resolve(Panel(null, SourceRule.OfCategory("eq2auras Buffs"))));

    [Fact]
    public void Null_on_a_panel_group_resolves_to_CenterRadial()
        => Assert.Equal(EscalationStyle.CenterRadial,
            EscalationDefaults.Resolve(Panel(null, SourceRule.Panel(1))));

    [Fact]
    public void An_explicit_value_is_used_verbatim_even_on_the_buff_group()
        => Assert.Equal(EscalationStyle.CenterRadial,
            EscalationDefaults.Resolve(Panel(EscalationStyle.CenterRadial, SourceRule.OfCategory("eq2auras Buffs"))));
}
