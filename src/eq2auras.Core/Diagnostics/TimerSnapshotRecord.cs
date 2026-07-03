using System.Text;

namespace Eq2Auras.Core.Diagnostics
{
    /// One raw reading of an ACT timer at a moment in time. Fields are captured
    /// verbatim from ACT — no derived state — because the spike observes ACT's
    /// behaviour rather than imposing the overlay's model.
    public sealed class TimerSnapshotRecord
    {
        public string Kind { get; set; }            // poll | notify | warning | expire | removed
        public long TimestampUnixMs { get; set; }
        public string Name { get; set; }
        public string Combatant { get; set; }
        // ACT's SpellTimer.TimeLeft is int seconds (goes negative after expiry, no clamp).
        // Null represents a frame with no live timer.
        public int? TimeLeft { get; set; }
        public int WarningValue { get; set; }
        public int TotalValue { get; set; }
        public bool PanelA { get; set; }    // TimerData.Panel1Display — group routing (SPEC §Diagnostic logging)
        public bool PanelB { get; set; }    // TimerData.Panel2Display

        public string ToJsonl()
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"kind\":\"").Append(Json.Escape(Kind)).Append("\"");
            sb.Append(",\"ts\":").Append(TimestampUnixMs);
            sb.Append(",\"name\":\"").Append(Json.Escape(Name)).Append("\"");
            sb.Append(",\"combatant\":\"").Append(Json.Escape(Combatant)).Append("\"");
            sb.Append(",\"timeLeft\":");
            if (TimeLeft.HasValue) sb.Append(TimeLeft.Value);      // int — no locale formatting concern
            else sb.Append("null");                                // keeps the JSONL parseable when no live timer
            sb.Append(",\"warningValue\":").Append(WarningValue);
            sb.Append(",\"totalValue\":").Append(TotalValue);
            sb.Append(",\"panelA\":").Append(PanelA ? "true" : "false");
            sb.Append(",\"panelB\":").Append(PanelB ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
