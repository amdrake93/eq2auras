using System;
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Diagnostics;
using Xunit;

public class LogRetentionPolicyTests
{
    private static readonly DateTime Now = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);

    private static LogFileInfo File(string path, int ageDays, long sizeBytes)
        => new LogFileInfo { Path = path, LastWriteUtc = Now.AddDays(-ageDays), SizeBytes = sizeBytes };

    private static List<string> Doomed(params LogFileInfo[] files)
        => LogRetentionPolicy.FilesToDelete(files, Now);

    [Fact]
    public void Fresh_files_under_budget_are_all_kept()
    {
        Assert.Empty(Doomed(File("a", 1, 1000), File("b", 5, 1000), File("c", 13, 1000)));
    }

    [Fact]
    public void Files_older_than_the_window_are_deleted()
    {
        Assert.Equal(new[] { "old" }, Doomed(File("old", 15, 10), File("fresh", 2, 10)));
    }

    [Fact]
    public void Size_overflow_deletes_oldest_first_until_under_budget()
    {
        long mb80 = 80L * 1024 * 1024;
        var doomed = Doomed(File("newest", 1, mb80), File("middle", 2, mb80), File("oldest", 3, mb80));

        Assert.Equal(new[] { "oldest" }, doomed);   // 240 MB -> drop oldest -> 160 MB fits
    }

    [Fact]
    public void A_lone_newest_file_over_the_cap_survives()
    {
        Assert.Empty(Doomed(File("giant", 1, 300L * 1024 * 1024)));
    }

    [Fact]
    public void Giant_newest_still_evicts_everything_older()
    {
        var doomed = Doomed(File("giant", 1, 300L * 1024 * 1024), File("older", 2, 10));

        Assert.Equal(new[] { "older" }, doomed);    // newest wins; older can't fit any budget
    }

    [Fact]
    public void Age_and_size_rules_compose()
    {
        long mb150 = 150L * 1024 * 1024;
        var doomed = Doomed(
            File("ancient", 20, 10),                 // age
            File("big-old", 10, mb150),              // size (oldest survivor)
            File("big-new", 1, mb150));

        Assert.Equal(new[] { "ancient", "big-old" }, doomed);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        long mb80 = 80L * 1024 * 1024;
        var doomed = Doomed(File("oldest", 3, mb80), File("newest", 1, mb80), File("middle", 2, mb80));

        Assert.Equal(new[] { "oldest" }, doomed);
    }

    [Fact]
    public void Empty_input_deletes_nothing()
    {
        Assert.Empty(LogRetentionPolicy.FilesToDelete(new List<LogFileInfo>(), Now));
    }
}
