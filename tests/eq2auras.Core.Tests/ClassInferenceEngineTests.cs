using Eq2Auras.Core.Meter;
using Xunit;

public class ClassInferenceEngineTests
{
    private static readonly int Grey = SubclassColors.Grey;

    [Fact]
    public void Unseen_name_is_grey_and_uncommitted()
    {
        var e = new ClassInferenceEngine();
        Assert.False(e.IsCommitted("Aeralik"));
        Assert.Equal(Grey, e.ColorForName("Aeralik"));
    }

    [Fact]
    public void First_final_hit_commits_subclass_and_final()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Aeralik", new[] { "Autoattack", "Lich's Siphoning" });
        Assert.True(e.IsCommitted("Aeralik"));
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("Aeralik"));
    }

    [Fact]
    public void Case_insensitive_name_keying()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Aeralik", new[] { "Lich's Siphoning" });
        Assert.True(e.IsCommitted("AERALIK"));
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("aeralik"));
    }

    [Fact]
    public void Unknown_fight_never_demotes_a_commit()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader
        e.Observe("Bob", new[] { "Autoattack", "Bandage" });   // no catalog hit
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Crusader), e.ColorForName("Bob"));
    }

    [Fact]
    public void Confident_disagreement_overrides_live()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader
        e.Observe("Bob", new[] { "Chromatic Shower" });        // Illusionist → Enchanter
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Enchanter), e.ColorForName("Bob"));
    }

    [Fact]
    public void Premium_hit_wins_within_a_call()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Interrupt", "Lich's Siphoning" });   // shared Rogue + premium Necro
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("Bob"));
    }

    [Fact]
    public void Committed_stays_committed_dirty_tracks_changes()
    {
        var e = new ClassInferenceEngine();
        Assert.False(e.HasDirty);
        e.Observe("Bob", new[] { "Reaver's Mania" });
        Assert.True(e.HasDirty);
        e.ClearDirty();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // same subclass again → no new dirt
        Assert.False(e.HasDirty);
    }

    [Fact]
    public void Warmstart_import_colors_immediately_but_is_not_committed()
    {
        var source = new ClassInferenceEngine();
        source.Observe("Bob", new[] { "Lich's Siphoning" });   // Summoner
        var e = new ClassInferenceEngine();
        e.Import(source.Export());
        Assert.False(e.IsCommitted("Bob"));                    // still re-read this session (persona guard)
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("Bob"));   // but colored now
    }

    [Fact]
    public void Reset_encounter_reopens_reads_but_keeps_color()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader, confirmed this encounter
        Assert.True(e.IsCommitted("Bob"));
        e.ResetEncounter();
        Assert.False(e.IsCommitted("Bob"));                    // re-read next encounter
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Crusader), e.ColorForName("Bob"));   // color survives
    }

    [Fact]
    public void Persona_swap_between_encounters_overrides()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader
        e.ResetEncounter();                                    // Bob relogs as another class between fights
        e.Observe("Bob", new[] { "Chromatic Shower" });        // Illusionist → Enchanter
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Enchanter), e.ColorForName("Bob"));
    }
}
