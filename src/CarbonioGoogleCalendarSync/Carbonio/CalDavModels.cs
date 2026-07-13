using System.Net;

namespace CarbonioGoogleCalendarSync.Carbonio;

public sealed record CalDavEvent(
  string Uid,
  string Summary,
  string Description,
  DateTimeOffset StartsAt,
  DateTimeOffset EndsAt,
  string TimeZoneId,
  IReadOnlyDictionary<string, string> CustomProperties);

public sealed record CalDavResource(Uri Url, string? ETag, string? CalendarData);

public sealed record CalDavOperationResult(HttpStatusCode StatusCode, string? ETag, string? Body)
{
  public bool IsSuccess => (int)StatusCode >= 200 && (int)StatusCode <= 299;
}
