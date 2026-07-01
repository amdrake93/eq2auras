using Eq2Auras.Core.Diagnostics;
using Xunit;

public class TimerSnapshotRecordTests
{
    [Fact]
    public void ToJsonl_serializes_all_fields()
    {
        var record = new TimerSnapshotRecord
        {
            Kind = "poll",
            TimestampUnixMs = 1750000000000,
            Name = "Tank Buster",
            Combatant = "Big Bad",
            TimeLeft = 12,
            WarningValue = 10,
            TotalValue = 30
        };

        var json = record.ToJsonl();

        Assert.Equal(
            "{\"kind\":\"poll\",\"ts\":1750000000000,\"name\":\"Tank Buster\"," +
            "\"combatant\":\"Big Bad\",\"timeLeft\":12,\"warningValue\":10,\"totalValue\":30}",
            json);
    }

    [Fact]
    public void ToJsonl_escapes_quotes_backslashes_and_control_chars_and_negative_timeLeft()
    {
        var record = new TimerSnapshotRecord
        {
            Kind = "notify",
            TimestampUnixMs = 1,
            Name = "He said \"hi\"\tand\\left",
            Combatant = "",
            TimeLeft = -3,
            WarningValue = 0,
            TotalValue = 0
        };

        var json = record.ToJsonl();

        Assert.Equal(
            "{\"kind\":\"notify\",\"ts\":1,\"name\":\"He said \\\"hi\\\"\\tand\\\\left\"," +
            "\"combatant\":\"\",\"timeLeft\":-3,\"warningValue\":0,\"totalValue\":0}",
            json);
    }

    [Fact]
    public void ToJsonl_emits_null_for_missing_timeLeft()
    {
        var record = new TimerSnapshotRecord
        {
            Kind = "poll", TimestampUnixMs = 5, Name = "x", Combatant = "",
            TimeLeft = null, WarningValue = 0, TotalValue = 0
        };

        Assert.Contains("\"timeLeft\":null", record.ToJsonl());
    }
}
