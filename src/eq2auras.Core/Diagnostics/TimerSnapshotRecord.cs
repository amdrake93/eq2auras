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
        public double TimeLeft { get; set; }
        public int WarningValue { get; set; }
        public int TotalValue { get; set; }

        public string ToJsonl()
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"kind\":\"").Append(Json.Escape(Kind)).Append("\"");
            sb.Append(",\"ts\":").Append(TimestampUnixMs);
            sb.Append(",\"name\":\"").Append(Json.Escape(Name)).Append("\"");
            sb.Append(",\"combatant\":\"").Append(Json.Escape(Combatant)).Append("\"");
            sb.Append(",\"timeLeft\":");
            if (double.IsNaN(TimeLeft)) sb.Append("null");            // invalid JSON otherwise — keeps the spike log parseable
            else sb.Append(Json.Number(TimeLeft));
            sb.Append(",\"warningValue\":").Append(WarningValue);
            sb.Append(",\"totalValue\":").Append(TotalValue);
            sb.Append("}");
            return sb.ToString();
        }
    }
}
