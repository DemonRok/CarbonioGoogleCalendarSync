using CarbonioGoogleCalendarSync.Configuration;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Logging;

namespace CarbonioGoogleCalendarSync.Google;

public sealed class GoogleCalendarClient(HttpClient httpClient, AppConfiguration configuration, ILogger<GoogleCalendarClient> logger)
{
  public async Task<IReadOnlyList<SourcedGoogleCalendarEvent>> GetEventsAsync(CancellationToken cancellationToken)
  {
    var results = new List<SourcedGoogleCalendarEvent>();
    foreach (var calendar in configuration.Google.GetCalendars())
    {
      if (!Uri.TryCreate(calendar.IcsUrl, UriKind.Absolute, out var icsUri) ||
          icsUri.Scheme != Uri.UriSchemeHttps)
      {
        throw new ConfigurationException($"Google:Calendars:{calendar.Id}:IcsUrl deve essere un URL HTTPS assoluto.");
      }

      var ics = await httpClient.GetStringAsync(icsUri, cancellationToken);
      var events = ParseEvents(ics, configuration.Sync.PastDays, configuration.Sync.FutureDays)
        .Select(googleEvent => new SourcedGoogleCalendarEvent(calendar, googleEvent))
        .ToList();
      logger.LogInformation("Eventi letti da Google calendar {CalendarId}: {Count}", calendar.Id, events.Count);
      results.AddRange(events);
    }

    return results;
  }

  public static IReadOnlyList<GoogleCalendarEvent> ParseEvents(string ics, int pastDays, int futureDays)
  {
    var calendar = Calendar.Load(ics);
    var timeMin = DateTimeOffset.Now.AddDays(-pastDays);
    var timeMax = DateTimeOffset.Now.AddDays(futureDays);
    var sourceEvents = calendar?.Events ?? Enumerable.Empty<CalendarEvent>();
    return sourceEvents
      .Select(MapEvent)
      .Where(calendarEvent => IsInsideWindow(calendarEvent, timeMin, timeMax))
      .OrderBy(calendarEvent => calendarEvent.StartsAt ?? DateTimeOffset.MinValue)
      .ToList();
  }

  private static GoogleCalendarEvent MapEvent(CalendarEvent calendarEvent)
  {
    var start = calendarEvent.DtStart?.Value ?? DateTime.MinValue;
    var duration = ToTimeSpan(calendarEvent.Duration);
    var end = calendarEvent.DtEnd?.Value ?? start + duration;
    var effectiveDuration = end - start;
    var isAllDay = start.TimeOfDay == TimeSpan.Zero &&
      calendarEvent.DtStart?.TzId is null &&
      effectiveDuration.TotalDays >= 1;
    var id = calendarEvent.Uid ?? $"{calendarEvent.Summary}-{start:yyyyMMddTHHmmss}";

    return new GoogleCalendarEvent(
      id,
      null,
      calendarEvent.Summary ?? "(senza titolo)",
      calendarEvent.Description,
      calendarEvent.Location,
      isAllDay ? null : new DateTimeOffset(start),
      isAllDay ? null : new DateTimeOffset(end),
      isAllDay ? DateOnly.FromDateTime(start) : null,
      isAllDay ? DateOnly.FromDateTime(end) : null,
      calendarEvent.DtStart?.TzId,
      calendarEvent.LastModified?.Value is { } lastModified
        ? new DateTimeOffset(lastModified)
        : DateTimeOffset.MinValue,
      calendarEvent.Status?.ToString() ?? "",
      (calendarEvent.Attendees ?? []).Select(attendee => attendee.Value?.ToString() ?? "").Where(value => value.Length > 0).ToList());
  }

  private static TimeSpan ToTimeSpan(Duration? duration)
  {
    if (duration is null)
    {
      return TimeSpan.Zero;
    }

    var value = duration.Value;
    var timeSpan = new TimeSpan(
      days: value.Weeks.GetValueOrDefault() * 7 + value.Days.GetValueOrDefault(),
      hours: value.Hours.GetValueOrDefault(),
      minutes: value.Minutes.GetValueOrDefault(),
      seconds: value.Seconds.GetValueOrDefault());
    return value.Sign < 0 ? -timeSpan : timeSpan;
  }

  private static bool IsInsideWindow(GoogleCalendarEvent calendarEvent, DateTimeOffset from, DateTimeOffset to)
  {
    var start = calendarEvent.StartsAt ??
      (calendarEvent.StartDate is null ? DateTimeOffset.MinValue : new DateTimeOffset(calendarEvent.StartDate.Value.ToDateTime(TimeOnly.MinValue)));
    var end = calendarEvent.EndsAt ??
      (calendarEvent.EndDate is null ? DateTimeOffset.MaxValue : new DateTimeOffset(calendarEvent.EndDate.Value.ToDateTime(TimeOnly.MinValue)));
    return start <= to && end >= from;
  }
}

public sealed record SourcedGoogleCalendarEvent(
  GoogleCalendarConfiguration Source,
  GoogleCalendarEvent Event);
