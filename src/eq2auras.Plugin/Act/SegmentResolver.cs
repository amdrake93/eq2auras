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
    }
}
