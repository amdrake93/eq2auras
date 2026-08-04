using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The pure part of segment resolution: keys, the per-distinct-segment collapse, and the
    /// culled->Current decision. The Plugin's SegmentResolver turns a key into an EncounterData.
    public static class SegmentKeys
    {
        public static string Of(SegmentSelection sel, string currentZoneKey)
        {
            switch (sel.Kind)
            {
                case SegmentKind.Zonewide: return "Z:" + currentZoneKey;
                case SegmentKind.Historical: return "H:" + sel.ZoneKey + ":" + sel.StartTicks;
                default: return "C";
            }
        }

        public static List<string> Distinct(IEnumerable<SegmentSelection> selections, string currentZoneKey)
        {
            var keys = new List<string> { "C" };   // always resolve Current (the fallback target)
            foreach (var s in selections)
            {
                var k = Of(s, currentZoneKey);
                if (!keys.Contains(k)) keys.Add(k);
            }
            return keys;
        }

        public static SegmentSelection FallbackOnMissing(SegmentSelection sel, bool resolved)
            => (resolved || sel.Kind != SegmentKind.Historical) ? sel : SegmentSelection.Current();
    }
}
