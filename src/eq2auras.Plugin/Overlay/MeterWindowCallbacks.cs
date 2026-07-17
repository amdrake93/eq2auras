using System;

namespace Eq2Auras.Plugin.Overlay
{
    /// The per-window callback set for a MeterWindow, bundled so the ctor stays legible as
    /// knobs accrue (SPEC Part III §Configuration). Assembled by OverlayHost per config;
    /// each persists through SettingsStore.Update.
    internal sealed class MeterWindowCallbacks
    {
        public Action<double, double> PersistPosition;
        public Action<string> MetricPicked;
        public Action<bool> LockChanged;
        public Action<double> OpacityChanged;
        public Action<double> RowHeightChanged;
        public Action<string, double> FontChanged;
        public Action<double, int> GeometryChanged;   // width + visible-row count, persisted at resize drag-end
        public Action NewWindow;
        public Action CloseWindow;
        public Func<bool> CanClose;
    }
}
