using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Diagnostics;
using Eq2Auras.Core.Timers;
using Eq2Auras.Plugin.Diagnostics;

namespace Eq2Auras.Plugin.Act
{
    /// Polls GetTimerFrames() on ACT's UI thread (WinForms timer), snapshots EVERY
    /// SpellTimer instance in every frame into plain TimerReadings, hands them to the
    /// overlay pipeline, and logs diagnostics. Lifecycle events log-only.
    public sealed class TimerProbe : IDisposable
    {
        private readonly JsonlLogWriter _log;
        private readonly Action<List<TimerReading>> _onReadings;
        private readonly Timer _pollTimer;

        public TimerProbe(JsonlLogWriter log, Action<List<TimerReading>> onReadings)
        {
            _log = log;
            _onReadings = onReadings;

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify += OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning += OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire += OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved += OnRemoved;

            _pollTimer = new Timer { Interval = 100 };
            _pollTimer.Tick += OnPoll;
            _pollTimer.Start();
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void OnPoll(object sender, EventArgs e)
        {
            List<TimerFrame> frames;
            try { frames = ActGlobals.oFormSpellTimers.GetTimerFrames(); }
            catch { return; }
            if (frames == null) return;

            var readings = new List<TimerReading>();
            foreach (var frame in frames)
            {
                var data = frame.TimerData;
                var instances = frame.SpellTimers;
                if (data == null || instances == null) continue;

                foreach (var instance in instances)
                {
                    readings.Add(new TimerReading
                    {
                        Name = frame.Name ?? "",
                        Combatant = frame.Combatant ?? "",
                        TimeLeft = instance.TimeLeft,
                        RawPreciseTimeLeft = instance.TimerFinalDuration
                            - (DateTime.Now - instance.StartTime).TotalSeconds,
                        WarningValue = data.WarningValue,
                        TotalSeconds = instance.TimerFinalDuration,
                        FillArgb = data.FillColor.ToArgb()
                    });
                }
            }

            foreach (var reading in readings) LogReading("poll", reading);
            _onReadings(readings);
        }

        private void LogReading(string kind, TimerReading reading)
        {
            _log.Write(new TimerSnapshotRecord
            {
                Kind = kind,
                TimestampUnixMs = NowMs(),
                Name = reading.Name,
                Combatant = reading.Combatant,
                TimeLeft = reading.TimeLeft,
                WarningValue = reading.WarningValue,
                TotalValue = reading.TotalSeconds
            });
        }

        private void LogFrameEvent(string kind, TimerFrame frame)
        {
            var timers = frame.SpellTimers;
            int? timeLeft = timers != null && timers.Count > 0 ? timers[0].TimeLeft : (int?)null;
            _log.Write(new TimerSnapshotRecord
            {
                Kind = kind,
                TimestampUnixMs = NowMs(),
                Name = frame.Name ?? "",
                Combatant = frame.Combatant ?? "",
                TimeLeft = timeLeft,
                WarningValue = frame.TimerData != null ? frame.TimerData.WarningValue : 0,
                TotalValue = frame.TimerData != null ? frame.TimerData.TimerValue : 0
            });
        }

        private void OnNotify(TimerFrame f) => LogFrameEvent("notify", f);
        private void OnWarning(TimerFrame f) => LogFrameEvent("warning", f);
        private void OnExpire(TimerFrame f) => LogFrameEvent("expire", f);
        private void OnRemoved(TimerFrame f) => LogFrameEvent("removed", f);

        public void Dispose()
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= OnPoll;
            _pollTimer.Dispose();

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify -= OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning -= OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire -= OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved -= OnRemoved;
        }
    }
}
