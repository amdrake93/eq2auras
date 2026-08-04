using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    public enum EncounterOutcome { Unknown = 0, Win = 1, Partial = 2, Wipe = 3 }

    public sealed class SegmentEntry
    {
        public string Title { get; set; }
        public double DurationSeconds { get; set; }
        public EncounterOutcome Outcome { get; set; }
        public bool IsAll { get; set; }
        public bool Available { get; set; } = true;   // false only for a placeholder All in a PopulateAll-off zone
        public string ZoneKey { get; set; }
        public long StartTicks { get; set; }
    }

    public sealed class ZoneGroup
    {
        public string ZoneName { get; set; }
        public string ZoneKey { get; set; }
        public bool IsCurrent { get; set; }
        public long StartTicks { get; set; }
        public SegmentEntry All { get; set; }         // always non-null (real aggregate or disabled placeholder)
        public List<SegmentEntry> Fights { get; set; } = new List<SegmentEntry>();
    }

    public sealed class SegmentListing
    {
        public bool ZonewideAvailable { get; set; }   // the current zone's PopulateAll (gates the top-level Zonewide)
        public List<ZoneGroup> Zones { get; set; } = new List<ZoneGroup>();
    }

    /// Pure: turns the Plugin's ACT-snapshotted zone/encounter rows into the flyout listing.
    /// Zero-duration fights dropped; fights newest-first; every group leads with its All (real
    /// or a disabled placeholder — SPEC §Availability); zones current-first then newest-first;
    /// empty/junk groups (no available All, no fights) dropped.
    public static class SegmentListBuilder
    {
        public sealed class RawEncounter
        {
            public string Title { get; set; }
            public double DurationSeconds { get; set; }
            public int SuccessLevel { get; set; }     // ACT GetEncounterSuccessLevel(): 1/2/3, 0 unknown
            public long StartTicks { get; set; }
            public bool IsAll { get; set; }
        }

        public sealed class RawZone
        {
            public string ZoneName { get; set; }
            public string ZoneKey { get; set; }
            public bool IsCurrent { get; set; }
            public long StartTicks { get; set; }
            public bool PopulateAll { get; set; }
            public List<RawEncounter> Encounters { get; set; } = new List<RawEncounter>();
        }

        public static EncounterOutcome OutcomeOf(int successLevel)
        {
            switch (successLevel)
            {
                case 1: return EncounterOutcome.Win;
                case 2: return EncounterOutcome.Partial;
                case 3: return EncounterOutcome.Wipe;
                default: return EncounterOutcome.Unknown;
            }
        }

        public static SegmentListing Build(IEnumerable<RawZone> zones)
        {
            var listing = new SegmentListing();
            var ordered = zones.OrderByDescending(z => z.IsCurrent).ThenByDescending(z => z.StartTicks);

            foreach (var z in ordered)
            {
                var group = new ZoneGroup { ZoneName = z.ZoneName, ZoneKey = z.ZoneKey, IsCurrent = z.IsCurrent, StartTicks = z.StartTicks };

                var rawAll = z.Encounters.FirstOrDefault(e => e.IsAll);
                group.All = (z.PopulateAll && rawAll != null)
                    ? Entry(rawAll, z.ZoneKey, isAll: true, available: true)
                    : new SegmentEntry { Title = "All", IsAll = true, Available = false, Outcome = EncounterOutcome.Unknown, ZoneKey = z.ZoneKey };

                group.Fights = z.Encounters
                    .Where(e => !e.IsAll && e.DurationSeconds > 0)
                    .OrderByDescending(e => e.StartTicks)
                    .Select(e => Entry(e, z.ZoneKey, isAll: false, available: true))
                    .ToList();

                if (!group.All.Available && group.Fights.Count == 0) continue;   // drop empty/junk groups
                listing.Zones.Add(group);

                if (z.IsCurrent) listing.ZonewideAvailable = z.PopulateAll;
            }

            return listing;
        }

        static SegmentEntry Entry(RawEncounter e, string zoneKey, bool isAll, bool available) => new SegmentEntry
        {
            Title = e.Title,
            DurationSeconds = e.DurationSeconds,
            Outcome = isAll ? EncounterOutcome.Unknown : OutcomeOf(e.SuccessLevel),
            IsAll = isAll,
            Available = available,
            ZoneKey = zoneKey,
            StartTicks = e.StartTicks,
        };
    }
}
