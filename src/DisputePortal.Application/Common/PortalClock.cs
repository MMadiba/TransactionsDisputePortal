using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputePortal.Application.Common;

/// <summary>
/// "Now" and the portal's display time zone. Storage is always UTC; this converts at the edges
/// (showing dates to people, and turning a customer's "from/to date" into a UTC range).
/// </summary>
public interface IPortalClock
{
    DateTime UtcNow { get; }

    TimeZoneInfo Zone { get; }

    DateOnly Today { get; }

    DateTime ToLocal(DateTime utc);

    /// <summary>Midnight at the start of <paramref name="date"/> in the portal time zone, as UTC.</summary>
    DateTime StartOfDayUtc(DateOnly date);
}

internal sealed class PortalClock : IPortalClock
{
    private readonly TimeProvider _time;

    public PortalClock(TimeProvider time, IOptions<PortalOptions> options, ILogger<PortalClock> logger)
    {
        _time = time;
        try
        {
            Zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Time zone {TimeZone} is not available on this host; falling back to UTC.", options.Value.TimeZone);
            Zone = TimeZoneInfo.Utc;
        }
    }

    public TimeZoneInfo Zone { get; }

    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(ToLocal(UtcNow));

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
}
