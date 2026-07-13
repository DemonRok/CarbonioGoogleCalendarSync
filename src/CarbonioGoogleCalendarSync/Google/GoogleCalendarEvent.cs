namespace CarbonioGoogleCalendarSync.Google;

public sealed record GoogleCalendarEvent(
  string Id,
  string? RecurringEventId,
  string Summary,
  string? Description,
  string? Location,
  DateTimeOffset? StartsAt,
  DateTimeOffset? EndsAt,
  DateOnly? StartDate,
  DateOnly? EndDate,
  string? TimeZoneId,
  DateTimeOffset Updated,
  string Status,
  IReadOnlyList<string> Attendees);
