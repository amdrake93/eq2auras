using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Diagnostics
{
    /// One log file's facts, gathered by the plugin's IO layer.
    public sealed class LogFileInfo
    {
        public string Path { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public long SizeBytes { get; set; }
    }

    /// The rolling-window retention decision (SPEC §Diagnostic logging): pure logic,
    /// no IO — the plugin enumerates files and deletes what this returns. Age uses
    /// last-write time; size deletes oldest-first; the newest file always survives
    /// the size rule (a problem report must always have the latest log).
    public static class LogRetentionPolicy
    {
        public const int RetentionDays = 14;
        public const long MaxTotalBytes = 200L * 1024 * 1024;

        public static List<string> FilesToDelete(IEnumerable<LogFileInfo> files, DateTime nowUtc)
        {
            var cutoff = nowUtc.AddDays(-RetentionDays);
            var all = files.ToList();

            var doomed = all
                .Where(f => f.LastWriteUtc < cutoff)
                .Select(f => f.Path)
                .ToList();

            var survivors = all
                .Where(f => f.LastWriteUtc >= cutoff)
                .OrderBy(f => f.LastWriteUtc)
                .ToList();

            long total = survivors.Sum(f => f.SizeBytes);
            int oldest = 0;
            while (total > MaxTotalBytes && survivors.Count - oldest > 1)
            {
                total -= survivors[oldest].SizeBytes;
                doomed.Add(survivors[oldest].Path);
                oldest++;
            }

            return doomed;
        }
    }
}
