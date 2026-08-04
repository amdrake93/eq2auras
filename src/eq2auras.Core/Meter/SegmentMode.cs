namespace Eq2Auras.Core.Meter
{
    /// The persisted segment selection. Only the two live modes persist; a historical pick is
    /// runtime-only (SPEC §Segments Persistence). Current is the 0-value so a never-set /
    /// legacy window deserializes to it (DCJS rule).
    public enum SegmentMode { Current = 0, Zonewide = 1 }
}
