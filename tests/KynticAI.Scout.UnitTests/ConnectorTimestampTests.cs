using KynticAI.Scout.Infrastructure.Connectors;

namespace KynticAI.Scout.UnitTests;

public sealed class ConnectorTimestampTests
{
    [Fact]
    public void ToUtc_PreservesUtcValue()
    {
        var value = new DateTime(2026, 8, 17, 12, 30, 0, DateTimeKind.Utc);

        Assert.Equal(value, ConnectorTimestamp.ToUtc(value));
        Assert.Equal(DateTimeKind.Utc, ConnectorTimestamp.ToUtc(value).Kind);
    }

    [Fact]
    public void ToUtc_ConvertsLocalClockValue_InsteadOfRelabellingIt()
    {
        var local = new DateTime(2026, 1, 15, 12, 30, 0, DateTimeKind.Local);

        var actual = ConnectorTimestamp.ToUtc(local);

        Assert.Equal(local.ToUniversalTime(), actual);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
    }

    [Fact]
    public void ToUtc_TreatsUnspecifiedDatabaseValueAsUtcByContract()
    {
        var unspecified = new DateTime(2026, 8, 17, 12, 30, 0, DateTimeKind.Unspecified);

        var actual = ConnectorTimestamp.ToUtc(unspecified);

        Assert.Equal(new DateTime(2026, 8, 17, 12, 30, 0, DateTimeKind.Utc), actual);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
    }

    [Theory]
    [InlineData("2026-08-17T13:30:00+01:00", "2026-08-17T12:30:00Z")]
    [InlineData("2026-08-17T12:30:00Z", "2026-08-17T12:30:00Z")]
    [InlineData("2026-08-17T12:30:00", "2026-08-17T12:30:00Z")]
    public void ParseUtc_NormalisesOffsetAndOffsetlessSourceValues(string source, string expected)
    {
        var actual = ConnectorTimestamp.ParseUtc(source);

        Assert.NotNull(actual);
        Assert.Equal(DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.RoundtripKind), actual.Value);
        Assert.Equal(DateTimeKind.Utc, actual.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void ParseUtc_ReturnsNull_ForMissingOrMalformedValue(string source)
    {
        Assert.Null(ConnectorTimestamp.ParseUtc(source));
    }
}
