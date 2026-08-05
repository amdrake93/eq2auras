using System;
using System.Collections.Generic;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// The per-window callback set for a MeterWindow, bundled so the ctor stays legible as
    /// knobs accrue (SPEC Part III §Configuration). Assembled by OverlayHost per config;
    /// each persists through SettingsStore.Update.
    public sealed class MeterWindowCallbacks
    {
        public Action<double, double> PersistPosition;
        public Action<MeterScope, string> PrimaryPicked;   // (scope, metricKey) persisted together
        public Action<string> SecondaryPicked;   // null = None
        public Action<bool> LockChanged;
        public Action<double> OpacityChanged;
        public Action<double> BackdropOpacityChanged;
        public Action<double> RowHeightChanged;
        public Action<string, double, bool, bool> FontChanged;   // family, size, bold, italic
        public Action<double, int> GeometryChanged;   // width + visible-row count, persisted at resize drag-end
        public Action NewWindow;
        public Action CloseWindow;
        public Func<bool> CanClose;
        public Action DrillChanged;   // window entered/left drill (or hover) mode -> host rebuilds the request snapshot
        public Func<DrillRequest, List<MeterRow>> ReadHoverNow;   // synchronous hover first-paint: host reads + ranks the combatant's by-counterpart rows now (instant), null if unavailable
        public Action<SegmentMode> SegmentModeChanged;   // persist the live segment mode (Current/Zonewide); a historical pick is runtime-only
        public Action<bool> PinnedChanged;               // persist the inverted knob (PinnedToSegment)
        public Func<HashSet<string>, SegmentListing> EnumerateSegments;   // on flyout open: snapshot ZoneList -> the listing; arg = the zones to dot (current + remembered-expanded)
        public Action SegmentChanged;                    // the window's live selection changed -> host rebuilds the segment + drill request sets
    }
}
