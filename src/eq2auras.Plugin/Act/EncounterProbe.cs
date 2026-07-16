using System;
using System.Collections.Generic;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Act
{
    /// The encounter adapter (SPEC Part III §Assembly split & polling): samples ACT's
    /// computed combat model on a divider of the existing 100 ms poll tick — briefly
    /// under AfterCombatActionDataLock, snapshot into Core DTOs, release, hand off.
    /// Reads only the cheap shapes: per-combatant totals, the ally list, the live
    /// title, StartTime (live branch) / Duration (frozen branch). Never holds an
    /// EncounterData reference across ticks; never touches EncId/GetHashCode.
    public sealed class EncounterProbe
    {
        public const int SampleEveryNthTick = 3;   // 100 ms tick -> ~300 ms effective (SPEC: ~2-4 Hz)

        private readonly Func<bool> _enabled;
        private readonly Action<EncounterReading, List<CombatantReading>> _onSample;
        private int _tick;

        public EncounterProbe(Func<bool> enabled, Action<EncounterReading, List<CombatantReading>> onSample)
        {
            _enabled = enabled;
            _onSample = onSample;
        }

        /// Called once per TimerProbe poll tick, on ACT's UI thread.
        public void OnTick()
        {
            if (++_tick % SampleEveryNthTick != 0) return;
            if (!_enabled()) return;

            EncounterReading encounterReading;
            var allies = new List<CombatantReading>();
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    var encounter = form.ActiveZone?.ActiveEncounter;
                    if (encounter == null)
                    {
                        encounterReading = new EncounterReading { Exists = false };
                    }
                    else
                    {
                        bool active = encounter.Active;
                        encounterReading = new EncounterReading
                        {
                            Exists = true,
                            Active = active,
                            Title = encounter.GetStrongestEnemy(ActGlobals.charName),
                            // Degenerate pre-first-swing polls (StartTime == DateTime.MaxValue)
                            // produce a hugely negative estimate here — MeterEngine clamps.
                            LiveDurationSeconds = (form.LastEstimatedTime - encounter.StartTime).TotalSeconds,
                            FinalDurationSeconds = active ? 0 : encounter.Duration.TotalSeconds,
                        };

                        foreach (var ally in encounter.GetAllies())
                        {
                            allies.Add(new CombatantReading
                            {
                                Name = ally.Name,
                                Damage = ally.Damage,
                                Healed = ally.Healed,
                                CureDispels = ally.CureDispels,
                            });
                        }
                    }
                }
            }
            catch
            {
                return;   // same defensive stance as TimerProbe's GetTimerFrames read
            }

            _onSample(encounterReading, allies);   // outside the lock — hold it briefly
        }
    }
}
