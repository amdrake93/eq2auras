using System;
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class ClassSignaturesTests
{
    [Fact]
    public void No_name_resolves_to_two_subclasses()   // the load-bearing invariant (SPEC §Class colors)
        => Assert.Empty(ClassSignatures.FindCrossSubclassCollisions());

    [Theory]
    // Final-specific STRONG tells → both final and subclass.
    [InlineData("Lich's Siphoning", Subclass.Summoner, FinalClass.Necromancer)]
    [InlineData("Reaver's Mania", Subclass.Crusader, FinalClass.Shadowknight)]
    [InlineData("Consecration", Subclass.Crusader, FinalClass.Paladin)]
    [InlineData("Evade Blame", Subclass.Rogue, FinalClass.Swashbuckler)]
    [InlineData("Dispatch", Subclass.Rogue, FinalClass.Brigand)]   // Backstab moved to SHARED (census); Dispatch is Brigand-specific
    [InlineData("Fiery Annihilation", Subclass.Summoner, FinalClass.Conjuror)]
    [InlineData("Darksong Blade", Subclass.Bard, FinalClass.Dirge)]
    [InlineData("Chaos Anthem", Subclass.Bard, FinalClass.Troubador)]
    public void Final_tells_resolve_final_and_subclass(string name, Subclass sc, FinalClass fc)
    {
        Assert.True(ClassSignatures.TryResolve(name, out var gotSc, out var gotFc));
        Assert.Equal(sc, gotSc);
        Assert.Equal(fc, gotFc);
    }

    [Theory]
    // SHARED tells → subclass only, final Unknown.
    [InlineData("Interrupt", Subclass.Rogue)]
    [InlineData("Aura of Warding", Subclass.Shaman)]
    [InlineData("Backstab", Subclass.Rogue)]   // census-MOVED to SHARED — the name can't split Swashbuckler/Brigand
    public void Shared_tells_resolve_subclass_only(string name, Subclass sc)
    {
        Assert.True(ClassSignatures.TryResolve(name, out var gotSc, out var gotFc));
        Assert.Equal(sc, gotSc);
        Assert.Equal(FinalClass.Unknown, gotFc);
    }

    [Fact]
    public void Case_insensitive()
        => Assert.True(ClassSignatures.TryResolve("lich's siphoning", out _, out _));

    [Theory]
    [InlineData("Ambush")]          // CUT — union spans multiple subclasses
    [InlineData("Healing Blanket")] // CUT — cloak proc
    [InlineData("Vampiric Requiem")]// CUT — cross-class proc
    public void Cut_names_do_not_resolve(string name)
        => Assert.False(ClassSignatures.TryResolve(name, out _, out _));

    [Theory]
    [InlineData("Lich's Siphoning")]
    [InlineData("Reaver's Mania")]
    [InlineData("Lunar Attendant's Oracle's Blessing")]
    [InlineData("Spiritual Circle")]
    public void Premium_procs_are_flagged(string name)
        => Assert.True(ClassSignatures.IsPremium(name));

    [Fact]
    public void Every_subclass_and_final_has_at_least_one_signature()
    {
        var subclassesSeen = new HashSet<Subclass>();
        var finalsSeen = new HashSet<FinalClass>();
        foreach (var name in ClassSignatures.AllNames)
        {
            ClassSignatures.TryResolve(name, out var sc, out var fc);
            subclassesSeen.Add(sc);
            if (fc != FinalClass.Unknown) finalsSeen.Add(fc);
        }
        foreach (Subclass s in Enum.GetValues(typeof(Subclass)))
            if (s != Subclass.Unknown) Assert.Contains(s, subclassesSeen);
        foreach (FinalClass f in Enum.GetValues(typeof(FinalClass)))
            if (f != FinalClass.Unknown) Assert.Contains(f, finalsSeen);
    }
}
