using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Diagnostics;
using Eq2Auras.Plugin.Diagnostics;

namespace Eq2Auras.Plugin.Act
{
    /// Subscribes to ACT's spell-timer lifecycle events and polls GetTimerFrames()
    /// on a WinForms timer, logging raw readings. Pure observation — no overlay logic.
    public sealed class TimerProbe : IDisposable
    {
        private readonly JsonlLogWriter _log;
        private readonly Timer _pollTimer;

        public TimerProbe(JsonlLogWriter log)
        {
            _log = log;

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify += OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning += OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire += OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved += OnRemoved;

            _pollTimer = new Timer { Interval = 100 }; // 10 Hz is ample for the spike
            _pollTimer.Tick += OnPoll;
            _pollTimer.Start();
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void LogFrame(string kind, TimerFrame frame)
        {
            // Copy primitives out of ACT's live objects immediately (snapshot).
            // Task 1.5 confirmed SpellTimer.TimeLeft is int (negative after expiry); null = no live timer.
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

        private void OnNotify(TimerFrame f) => LogFrame("notify", f);
        private void OnWarning(TimerFrame f) => LogFrame("warning", f);
        private void OnExpire(TimerFrame f) => LogFrame("expire", f);
        private void OnRemoved(TimerFrame f) => LogFrame("removed", f);

        private void OnPoll(object sender, EventArgs e)
        {
            List<TimerFrame> frames;
            try
            {
                frames = ActGlobals.oFormSpellTimers.GetTimerFrames();
            }
            catch
            {
                return; // collection mutating on ACT's thread; skip this tick
            }
            if (frames == null) return;
            foreach (var f in frames)
            {
                LogFrame("poll", f);
            }
        }

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
