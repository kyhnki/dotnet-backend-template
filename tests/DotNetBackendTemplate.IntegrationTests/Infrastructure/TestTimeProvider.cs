namespace DotNetBackendTemplate.IntegrationTests.Infrastructure;

public sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private long _utcTicks = GetUtcTicks(utcNow);

    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void SetUtcNow(DateTimeOffset utcNow) =>
        Interlocked.Exchange(ref _utcTicks, GetUtcTicks(utcNow));

    private static long GetUtcTicks(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The test time must use the UTC offset (+00:00).", nameof(value));
        }

        return value.Ticks;
    }
}
