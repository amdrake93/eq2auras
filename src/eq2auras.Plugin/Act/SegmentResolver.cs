using System.Collections.Generic;
using System.Linq;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Act
{
    /// The ACT adapter for the pure Core segment model (SPEC §Segments): turns a Core segment
    /// KEY into an ACT EncounterData, reads the current zone key, and enumerates ZoneList for the
    /// flyout. Never holds an EncounterData across polls — each call re-resolves by handle.
    internal static class SegmentResolver
    {
        /// A session-stable per-visit zone handle. ACT appends a new ZoneData per zone visit with a
        /// fresh StartTime (decompile-confirmed), so name + StartTime.Ticks disambiguates revisits.
        public static string ZoneKey(ZoneData z) => z == null ? null : z.ZoneName + "#" + z.StartTime.Ticks;

        public static string CurrentZoneKey(FormActMain form) => ZoneKey(form.ActiveZone);

        /// Resolve a Core key ("C" / "Z:..." / "H:zoneKey:ticks") to an EncounterData. `unavailable`
        /// is true ONLY for Zonewide with PopulateAll off (a communicated dormant state); a missing
        /// historical handle returns (null, false) so the caller falls the window back to Current.
        public static EncounterData ResolveByKey(FormActMain form, string key, out bool unavailable)
        {
            unavailable = false;
            if (string.IsNullOrEmpty(key) || key == "C") return form.ActiveZone?.ActiveEncounter;

            if (key[0] == 'Z')   // "Z:<currentZoneKey>" — the current zone's live "All"
            {
                var zone = form.ActiveZone;
                if (zone == null || !zone.PopulateAll) { unavailable = true; return null; }   // option OFF → the "enable Zone All listing" hint
                return zone.Items.Count > 0 ? zone.Items[0] : null;   // option on, no combat yet → dormant empty (no hint)
            }

            if (key.StartsWith("HA:"))   // a specific past zone's static "All" (Items[0]) — resolved by zone, not by tick
            {
                string zoneKey = key.Substring(3);
                var z = form.ZoneList?.FirstOrDefault(zz => ZoneKey(zz) == zoneKey);
                return (z != null && z.PopulateAll && z.Items.Count > 0) ? z.Items[0] : null;
            }

            if (key.StartsWith("H:"))   // "H:<zoneKey>:<encounterStartTicks>"; zoneKey = "name#ticks" (no ':')
            {
                int lastColon = key.LastIndexOf(':');
                if (lastColon <= 2) return null;
                string zoneKey = key.Substring(2, lastColon - 2);
                if (!long.TryParse(key.Substring(lastColon + 1), out long ticks)) return null;
                var z = form.ZoneList?.FirstOrDefault(zz => ZoneKey(zz) == zoneKey);
                if (z == null) return null;
                // A fight — skip the zone's All (Items[0] when PopulateAll), which shares the first fight's
                // first-hostile timestamp, so a first-fight pick never resolves to the aggregate.
                var fights = (z.PopulateAll && z.Items.Count > 0) ? z.Items.Skip(1) : (IEnumerable<EncounterData>)z.Items;
                return fights.FirstOrDefault(e => e.StartTimes.Count > 0 && e.StartTimes[0].Ticks == ticks);
            }

            return form.ActiveZone?.ActiveEncounter;
        }

        /// On flyout open (not per poll), under the lock: snapshot ZoneList into Core RawZone/
        /// RawEncounter rows. `GetEncounterSuccessLevel()` forces ACT to compute AND CACHE an ally list
        /// per encounter — so on an import-heavy session, dotting every encounter permanently bloats
        /// ACT's memory and stalls the lock (field-2026-08-04, "all of ACT laggy"). We only pay it for
        /// zones the flyout shows expanded — the current zone plus the user's remembered ones; the rest
        /// render dot-less (Unknown). The flyout itself builds visual rows only for those zones too.
        public static SegmentListing Enumerate(FormActMain form, HashSet<string> dottedZones)
        {
            var raws = new List<SegmentListBuilder.RawZone>();
            lock (form.AfterCombatActionDataLock)
            {
                foreach (var z in form.ZoneList ?? Enumerable.Empty<ZoneData>())
                {
                    string zoneKey = ZoneKey(z);
                    bool isCurrent = ReferenceEquals(z, form.ActiveZone);
                    bool dotted = isCurrent || (dottedZones != null && dottedZones.Contains(zoneKey));
                    var rz = new SegmentListBuilder.RawZone
                    {
                        ZoneName = z.ZoneName,
                        ZoneKey = zoneKey,
                        IsCurrent = isCurrent,
                        StartTicks = z.StartTime.Ticks,
                        PopulateAll = z.PopulateAll,
                    };
                    for (int i = 0; i < z.Items.Count; i++)
                    {
                        var e = z.Items[i];
                        rz.Encounters.Add(new SegmentListBuilder.RawEncounter
                        {
                            Title = string.IsNullOrEmpty(e.Title) ? "Encounter" : e.Title,
                            DurationSeconds = e.Duration.TotalSeconds,
                            SuccessLevel = dotted ? e.GetEncounterSuccessLevel() : 0,   // the ally-cache-bloating call — scoped
                            StartTicks = e.StartTimes.Count > 0 ? e.StartTimes[0].Ticks : 0,
                            IsAll = z.PopulateAll && i == 0,
                        });
                    }
                    raws.Add(rz);
                }
            }
            return SegmentListBuilder.Build(raws);
        }
    }
}
