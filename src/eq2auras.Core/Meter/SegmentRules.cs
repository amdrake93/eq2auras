namespace Eq2Auras.Core.Meter
{
    /// Pure selection/knob transitions (SPEC §Segments — "Selection is a live choice plus a
    /// behavior knob"). The Plugin holds the live selection and calls these on the poll edges.
    public static class SegmentRules
    {
        public static SegmentSelection FromMode(SegmentMode mode)
            => mode == SegmentMode.Zonewide ? SegmentSelection.Zonewide() : SegmentSelection.Current();

        /// Picking Zonewide pins the window in one gesture (PinnedToSegment = true).
        public static bool ClearsKnobOnPick(SegmentSelection pick)
            => pick != null && pick.Kind == SegmentKind.Zonewide;

        /// On a new-combat transition: a non-pinned, non-Current selection snaps to Current;
        /// pinned stays; Current is a no-op. Uniform — Zonewide is not exempt.
        public static SegmentSelection OnNewCombat(SegmentSelection current, bool pinned)
        {
            if (pinned || current == null || current.Kind == SegmentKind.Current) return current ?? SegmentSelection.Current();
            return SegmentSelection.Current();
        }
    }
}
