using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class ClassCacheTests
{
    [Fact]
    public void Roundtrips_through_dcjs()
    {
        var cache = new ClassCache { Entries = new List<ClassCacheEntry>
        {
            new ClassCacheEntry { Name = "Aeralik", Subclass = Subclass.Summoner, Final = FinalClass.Necromancer },
            new ClassCacheEntry { Name = "Bob", Subclass = Subclass.Cleric, Final = FinalClass.Unknown },
        }};
        var back = ClassCache.Parse(cache.ToJson());
        Assert.Equal(2, back.Entries.Count);
        Assert.Equal("Aeralik", back.Entries[0].Name);
        Assert.Equal(Subclass.Summoner, back.Entries[0].Subclass);
        Assert.Equal(FinalClass.Necromancer, back.Entries[0].Final);
        Assert.Equal(FinalClass.Unknown, back.Entries[1].Final);
    }

    [Fact]
    public void Corrupt_or_empty_json_yields_empty_cache()
    {
        Assert.Empty(ClassCache.Parse("not json").Entries);
        Assert.Empty(ClassCache.Parse("").Entries);
    }
}
