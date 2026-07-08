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
            TotalValue = 30,
            PanelA = true,
            PanelB = false,
            Master = true,
            Instances = null
        };

        var json = record.ToJsonl();

        Assert.Equal(
            "{\"kind\":\"poll\",\"ts\":1750000000000,\"name\":\"Tank Buster\"," +
            "\"combatant\":\"Big Bad\",\"timeLeft\":12,\"warningValue\":10,\"totalValue\":30," +
            "\"panelA\":true,\"panelB\":false,\"master\":true,\"instances\":null}",
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
            "\"combatant\":\"\",\"timeLeft\":-3,\"warningValue\":0,\"totalValue\":0," +
            "\"panelA\":false,\"panelB\":false,\"master\":null,\"instances\":null}",
            json);
    }

    [Fact]
    public void ToJsonl_frame_event_carries_null_master_and_an_instance_count()
    {
        // `removed` semantics (SPEC §Diagnostic logging): value null by construction,
        // positive instance count = evidence of killed non-masters.
        var record = new TimerSnapshotRecord
        {
            Kind = "removed", TimestampUnixMs = 2, Name = "x", Combatant = "y",
            TimeLeft = null, WarningValue = 0, TotalValue = 0,
            PanelA = false, PanelB = false, Master = null, Instances = 10
        };

        Assert.Contains("\"timeLeft\":null", record.ToJsonl());
        Assert.Contains("\"master\":null", record.ToJsonl());
        Assert.Contains("\"instances\":10", record.ToJsonl());
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

    [Fact]
    public void Includes_panel_routing_flags()
    {
        var record = new TimerSnapshotRecord { Kind = "poll", Name = "t", PanelA = true, PanelB = false };

        var json = record.ToJsonl();

        Assert.Contains("\"panelA\":true", json);
        Assert.Contains("\"panelB\":false", json);
    }
}
