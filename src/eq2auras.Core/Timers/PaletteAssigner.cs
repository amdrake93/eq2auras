using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Timers
{
    /// Session-stable color identity: keyed by NORMALIZED NAME ONLY, first-fired
    /// order, stable for the plugin-instance lifetime. Two instances exist by design
    /// (SPEC §Timer colors; SPEC Part III §The meter window — Rows): the timer
    /// module's (timer names — the ability as players think of it) and the meter's
    /// (ally names). The namespaces are disjoint and the maps deliberately separate,
    /// so ally names never shift timer slot assignments. (Consistency is a
    /// repeated-attempts feature — a reload resetting assignments is accepted.)
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
