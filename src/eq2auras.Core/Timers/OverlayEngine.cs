using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// The multi-group policy (SPEC §Timer groups): one EscalationTracker per configured
    /// group, all sharing ONE PaletteAssigner — color identity is global, so an ability
    /// keeps its color in every group. Routing mirrors ACT's panel flags exactly: a
    /// reading goes to every group whose flag is set — both -> both, neither -> nowhere.
    public sealed class OverlayEngine
    {
        private readonly PaletteAssigner _palette = new PaletteAssigner();
        private readonly List<EscalationTracker> _trackers;

        public OverlayEngine(Settings settings)
        {
            _trackers = (settings ?? new Settings()).Panels
                .Select(panel => new EscalationTracker(panel, _palette))
                .ToList();
        }

        /// One frame per group, index-aligned with Settings.Panels.
        public List<OverlayFrame> Tick(IReadOnlyList<TimerReading> readings)
        {
            return _trackers
                .Select((tracker, i) => tracker.Tick(
                    readings.Where(r => RoutesTo(i, r)).ToList()))
                .ToList();
        }

        // The one deliberately two-shaped piece: ACT's routing data IS two panel
        // booleans. N-groups later = per-group source rules here; nothing else
        // changes shape (SPEC §Timer groups).
        private static bool RoutesTo(int panelIndex, TimerReading reading)
            => panelIndex == 0 ? reading.ShowInPanelA : reading.ShowInPanelB;
    }
}
