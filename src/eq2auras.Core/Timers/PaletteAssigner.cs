using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Timers
{
    /// Session-stable color identity (SPEC §Timer colors): keyed by NORMALIZED TIMER
    /// NAME ONLY — the ability as players think of it. Same ability from different boss
    /// variants / zone-categorized triggers keeps one color. First-fired order; stable
    /// for the plugin-instance lifetime (consistency is a repeated-attempts feature —
    /// a reload resetting assignments is accepted).
    public sealed class PaletteAssigner
    {
        private readonly Dictionary<string, int> _slotByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _nextSlot;

        public int IndexFor(string timerName)
        {
            var key = (timerName ?? "").Trim().ToLowerInvariant();
            if (!_slotByName.TryGetValue(key, out var slot))
            {
                slot = _nextSlot++;
                _slotByName[key] = slot;
            }
            return slot;
        }
    }
}
