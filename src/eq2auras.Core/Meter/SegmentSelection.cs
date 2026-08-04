using System;

namespace Eq2Auras.Core.Meter
{
    public enum SegmentKind { Current = 0, Zonewide, Historical }

    /// A pure, ACT-free descriptor of what a window is showing. Current/Zonewide persist (via
    /// SegmentMode); a Historical pick is runtime-only, keyed by a session-stable handle (zone
    /// key + the encounter's start-ticks) re-resolved each poll.
    public sealed class SegmentSelection : IEquatable<SegmentSelection>
    {
        public SegmentKind Kind { get; }
        public string ZoneKey { get; }
        public long StartTicks { get; }
        public bool IsAll { get; }   // a Historical pick of a zone's "All" aggregate (Items[0]) vs. one of its fights

        private SegmentSelection(SegmentKind kind, string zoneKey, long startTicks, bool isAll)
        {
            Kind = kind;
            ZoneKey = zoneKey;
            StartTicks = startTicks;
            IsAll = isAll;
        }

        public static SegmentSelection Current() => new SegmentSelection(SegmentKind.Current, null, 0, false);
        public static SegmentSelection Zonewide() => new SegmentSelection(SegmentKind.Zonewide, null, 0, false);
        public static SegmentSelection Historical(string zoneKey, long startTicks) => new SegmentSelection(SegmentKind.Historical, zoneKey, startTicks, false);
        // A specific past zone's static "All" — resolved by zone (Items[0]), not by an encounter start-tick,
        // so it can never collide with that zone's first fight (which shares the All's first-hostile timestamp).
        public static SegmentSelection HistoricalAll(string zoneKey) => new SegmentSelection(SegmentKind.Historical, zoneKey, 0, true);

        public bool Equals(SegmentSelection other)
            => other != null && Kind == other.Kind && ZoneKey == other.ZoneKey && StartTicks == other.StartTicks && IsAll == other.IsAll;

        public override bool Equals(object obj) => Equals(obj as SegmentSelection);

        public override int GetHashCode()
            => ((int)Kind * 397) ^ ((ZoneKey?.GetHashCode() ?? 0) * 31) ^ StartTicks.GetHashCode() ^ (IsAll ? 17 : 0);
    }
}
