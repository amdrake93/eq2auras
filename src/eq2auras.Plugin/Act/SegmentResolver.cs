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
                if (zone != null && zone.PopulateAll && zone.Items.Count > 0) return zone.Items[0];
                unavailable = true;
                return null;
            }

            if (key[0] == 'H')   // "H:<zoneKey>:<encounterStartTicks>"; zoneKey = "name#ticks" (no ':')
            {
                int lastColon = key.LastIndexOf(':');
                if (lastColon <= 2) return null;
                string zoneKey = key.Substring(2, lastColon - 2);
                if (!long.TryParse(key.Substring(lastColon + 1), out long ticks)) return null;
                var z = form.ZoneList?.FirstOrDefault(zz => ZoneKey(zz) == zoneKey);
                return z?.Items.FirstOrDefault(e => e.StartTimes.Count > 0 && e.StartTimes[0].Ticks == ticks);
            }

            return form.ActiveZone?.ActiveEncounter;
        }

        /// On flyout open (not per poll), under the lock: snapshot ZoneList into the Core RawZone/
        /// RawEncounter rows and let the tested SegmentListBuilder do the grouping/ordering/filter/
        /// availability. GetEncounterSuccessLevel is per-call (uncached), but this runs on-open only
        /// and ACT's culling bounds ZoneList.
        public static SegmentListing Enumerate(FormActMain form)
        {
            var raws = new List<SegmentListBuilder.RawZone>();
            lock (form.AfterCombatActionDataLock)
            {
                foreach (var z in form.ZoneList ?? Enumerable.Empty<ZoneData>())
                {
                    var rz = new SegmentListBuilder.RawZone
                    {
                        ZoneName = z.ZoneName,
                        ZoneKey = ZoneKey(z),
                        IsCurrent = ReferenceEquals(z, form.ActiveZone),
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
                            SuccessLevel = e.GetEncounterSuccessLevel(),
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
