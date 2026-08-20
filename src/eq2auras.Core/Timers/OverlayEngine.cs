using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// The multi-group policy (SPEC §Timer groups): one EscalationTracker per configured
    /// group, all sharing ONE PaletteAssigner — color identity is global, so an ability
    /// keeps its color in every group. Routing is the ASSOCIATION MODEL: each group selects
    /// its readings via its own list of source rules (panel:/category:/name:) through one
    /// generic predicate — routing lives on the window, not on the timer. No panel C.
    public sealed class OverlayEngine
    {
        private readonly PaletteAssigner _palette = new PaletteAssigner();
        private readonly Settings _settings;
        private readonly List<EscalationTracker> _trackers;

        public OverlayEngine(Settings settings)
        {
            _settings = settings ?? new Settings();
            _trackers = _settings.Panels
                .Select(panel => new EscalationTracker(panel, _palette))
                .ToList();
        }

        /// One frame per group, index-aligned with Settings.Panels. The palette is
        /// read PER TICK so tab edits apply live with no notification plumbing.
        public List<OverlayFrame> Tick(IReadOnlyList<TimerReading> readings)
        {
            return _trackers
                .Select((tracker, i) => tracker.Tick(
                    readings.Where(r => SourceRules.MatchesAny(_settings.Panels[i].Sources, r)).ToList(),
                    _settings.PaletteArgb))
                .ToList();
        }
    }
}
