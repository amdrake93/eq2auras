using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class BuffSyncTests
{
    [Fact]
    public void Desired_is_catalog_intersect_enabled()
    {
        var desired = BuffSync.Desired(new[] { "bolster", "tortoise-shell", "not-a-buff" }).Select(b => b.Id).ToList();
        Assert.Equal(new[] { "bolster", "tortoise-shell" }, desired);
    }

    [Fact]
    public void Null_enabled_set_desires_nothing()
        => Assert.Empty(BuffSync.Desired(null));
}
