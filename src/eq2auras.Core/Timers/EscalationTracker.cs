using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The escalation state machine. Stateful but bounded (SPEC): per-key memory of
    /// "was escalated last tick" plus active LATE floors — nothing unbounded. Call
    /// Tick once per poll from a single thread with a monotonic nowMs.
    public sealed class EscalationTracker
    {
        private const int CenterSlots = 3;          // future config knob
        private const long LateFloorMs = 2000;      // minimum LATE display (measured: ACT gives <1s)

        private sealed class LateEntry
        {
            public long CreatedMs;
            public string Name;
            public string Combatant;
            public int FillArgb;
        }

        private readonly Dictionary<string, bool> _wasEscalated = new Dictionary<string, bool>();
        private readonly Dictionary<string, LateEntry> _lates = new Dictionary<string, LateEntry>();

        public OverlayFrame Tick(IReadOnlyList<TimerReading> readings, long nowMs)
        {
            // A re-firing trigger ADDS a SpellTimer instance to the same frame (measured,
            // AbsoluteTiming off) — but semantically the ability fired and the old countdown
            // is void. Newest instance (most time left) wins per (Name|Combatant) key; ACT's
            // own window does the equivalent by drawing one row per frame.
            var live = readings
                .Where(r => r.TimeLeft > 0)
                .GroupBy(KeyOf)
                .Select(g => g.OrderByDescending(TimerMath.PreciseOf).First())
                .ToList();
            var liveKeys = new HashSet<string>(live.Select(KeyOf));

            CancelLatesWithLiveReadings(liveKeys);
            CreateLatesForVanishedEscalatedKeys(readings, liveKeys, nowMs);
            ExpireLates(nowMs);
            RememberEscalationState(live, liveKeys);

            var lates = _lates.Values
                .OrderByDescending(l => l.CreatedMs)
                .Select(l => new CenterElement
                {
                    Kind = CenterElementKind.Late,
                    Name = l.Name,
                    Combatant = l.Combatant,
                    LateSeconds = (int)((nowMs - l.CreatedMs) / 1000),
                    FillArgb = l.FillArgb
                })
                .ToList();

            var imminent = live
                .Where(r => r.TimeLeft <= TimerMath.EffectiveWarning(r))
                .OrderBy(TimerMath.PreciseOf)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pieSlots = Math.Max(0, CenterSlots - lates.Count);
            var centered = imminent.Take(pieSlots).ToList();
            var pies = centered.Select(r => new CenterElement
            {
                Kind = CenterElementKind.Pie,
                Name = r.Name,
                Combatant = r.Combatant,
                SecondsLeft = r.TimeLeft,
                PreciseSecondsLeft = TimerMath.PreciseOf(r),
                PieFraction = Math.Min(1.0, TimerMath.PreciseOf(r) / TimerMath.EffectiveWarning(r)),
                FillArgb = r.FillArgb
            });

            return new OverlayFrame
            {
                ListRows = TimerListBuilder.Build(live.Except(centered)),
                CenterElements = lates.Concat(pies).ToList()
            };
        }

        private void CancelLatesWithLiveReadings(HashSet<string> liveKeys)
        {
            foreach (var key in _lates.Keys.Where(liveKeys.Contains).ToList())
            {
                _lates.Remove(key);
            }
        }

        private void CreateLatesForVanishedEscalatedKeys(
            IReadOnlyList<TimerReading> readings, HashSet<string> liveKeys, long nowMs)
        {
            foreach (var pair in _wasEscalated.Where(p => p.Value && !liveKeys.Contains(p.Key)).ToList())
            {
                if (_lates.ContainsKey(pair.Key)) continue;

                var lastSeen = readings.FirstOrDefault(r => KeyOf(r) == pair.Key);
                var parts = pair.Key.Split(new[] { '|' }, 2);
                _lates[pair.Key] = new LateEntry
                {
                    CreatedMs = nowMs,
                    Name = parts[0],
                    Combatant = parts.Length > 1 ? parts[1] : "",
                    FillArgb = lastSeen != null ? lastSeen.FillArgb : 0
                };
            }
        }

        private void ExpireLates(long nowMs)
        {
            foreach (var key in _lates.Where(p => nowMs - p.Value.CreatedMs >= LateFloorMs)
                                      .Select(p => p.Key).ToList())
            {
                _lates.Remove(key);
            }
        }

        private void RememberEscalationState(List<TimerReading> live, HashSet<string> liveKeys)
        {
            foreach (var key in _wasEscalated.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            {
                _wasEscalated.Remove(key);
            }
            foreach (var group in live.GroupBy(KeyOf))
            {
                _wasEscalated[group.Key] = group.Any(r => r.TimeLeft <= TimerMath.EffectiveWarning(r));
            }
        }

        private static string KeyOf(TimerReading r) => r.Name + "|" + r.Combatant;
    }
}
