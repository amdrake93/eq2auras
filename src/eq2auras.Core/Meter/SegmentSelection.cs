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

        private SegmentSelection(SegmentKind kind, string zoneKey, long startTicks)
        {
            Kind = kind;
            ZoneKey = zoneKey;
            StartTicks = startTicks;
        }

        public static SegmentSelection Current() => new SegmentSelection(SegmentKind.Current, null, 0);
        public static SegmentSelection Zonewide() => new SegmentSelection(SegmentKind.Zonewide, null, 0);
        public static SegmentSelection Historical(string zoneKey, long startTicks) => new SegmentSelection(SegmentKind.Historical, zoneKey, startTicks);

        public bool Equals(SegmentSelection other)
            => other != null && Kind == other.Kind && ZoneKey == other.ZoneKey && StartTicks == other.StartTicks;

        public override bool Equals(object obj) => Equals(obj as SegmentSelection);

        public override int GetHashCode()
            => ((int)Kind * 397) ^ ((ZoneKey?.GetHashCode() ?? 0) * 31) ^ StartTicks.GetHashCode();
    }
}
