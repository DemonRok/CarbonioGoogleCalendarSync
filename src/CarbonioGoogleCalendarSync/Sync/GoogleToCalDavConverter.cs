using System.Globalization;
using System.Text;
using CarbonioGoogleCalendarSync.Carbonio;
using CarbonioGoogleCalendarSync.Configuration;
using CarbonioGoogleCalendarSync.Google;

namespace CarbonioGoogleCalendarSync.Sync;

public sealed class GoogleToCalDavConverter(AppConfiguration configuration)
{
  public string GetUid(GoogleCalendarEvent googleEvent)
  {
    return CalDavUtilities.GenerateGoogleUid(googleEvent.Id);
  }

  public string GetUid(GoogleCalendarConfiguration source, GoogleCalendarEvent googleEvent)
  {
    return source.UseLegacyUid || source.Id.Equals("primary", StringComparison.OrdinalIgnoreCase)
      ? GetUid(googleEvent)
      : CalDavUtilities.GenerateGoogleUid($"{source.Id}:{googleEvent.Id}");
  }

  public string ConvertToICalendar(GoogleCalendarEvent googleEvent)
  {
    return ConvertToICalendar(
      new GoogleCalendarConfiguration(configuration.Google.CalendarId, configuration.Google.IcsUrl, configuration.Sync.ImportedTitlePrefix, useLegacyUid: true),
      googleEvent);
  }

  public string ConvertToICalendar(GoogleCalendarConfiguration source, GoogleCalendarEvent googleEvent)
  {
    var uid = GetUid(source, googleEvent);
    var builder = new StringBuilder();
    Append(builder, "BEGIN:VCALENDAR");
    Append(builder, "VERSION:2.0");
    Append(builder, "PRODID:-//CarbonioGoogleCalendarSync//IT");
    Append(builder, "CALSCALE:GREGORIAN");
    Append(builder, "METHOD:PUBLISH");
    Append(builder, "BEGIN:VEVENT");
    Append(builder, $"UID:{EscapeText(uid)}");
    Append(builder, $"DTSTAMP:{FormatUtc(GetStableTimestamp(googleEvent))}");
    Append(builder, $"SUMMARY:{EscapeText(GetImportedSummary(source, googleEvent.Summary))}");

    if (googleEvent.StartDate is not null && googleEvent.EndDate is not null)
    {
      Append(builder, $"DTSTART;VALUE=DATE:{googleEvent.StartDate.Value:yyyyMMdd}");
      Append(builder, $"DTEND;VALUE=DATE:{googleEvent.EndDate.Value:yyyyMMdd}");
    }
    else if (googleEvent.StartsAt is not null && googleEvent.EndsAt is not null)
    {
      var timezone = string.IsNullOrWhiteSpace(googleEvent.TimeZoneId)
        ? "Europe/Rome"
        : googleEvent.TimeZoneId;
      Append(builder, $"DTSTART;TZID={timezone}:{FormatLocal(googleEvent.StartsAt.Value)}");
      Append(builder, $"DTEND;TZID={timezone}:{FormatLocal(googleEvent.EndsAt.Value)}");
    }

    var description = BuildDescription(googleEvent);
    if (!string.IsNullOrWhiteSpace(description))
    {
      Append(builder, $"DESCRIPTION:{EscapeText(description)}");
    }

    if (!string.IsNullOrWhiteSpace(googleEvent.Location))
    {
      Append(builder, $"LOCATION:{EscapeText(googleEvent.Location)}");
    }

    Append(builder, $"STATUS:{MapStatus(googleEvent.Status)}");
    Append(builder, "X-CARBONIO-GOOGLE-SYNC:TRUE");
    Append(builder, $"X-GOOGLE-EVENT-ID:{EscapeText(googleEvent.Id)}");
    Append(builder, $"X-GOOGLE-CALENDAR-ID:{EscapeText(source.Id)}");
    Append(builder, $"X-GOOGLE-UPDATED:{EscapeText(googleEvent.Updated.ToString("O", CultureInfo.InvariantCulture))}");
    Append(builder, "END:VEVENT");
    Append(builder, "END:VCALENDAR");
    return builder.ToString();
  }

  public string ComputeHash(GoogleCalendarEvent googleEvent)
  {
    return CalDavUtilities.ComputeNormalizedHash(ConvertToICalendar(googleEvent));
  }

  public string ComputeHash(GoogleCalendarConfiguration source, GoogleCalendarEvent googleEvent)
  {
    return CalDavUtilities.ComputeNormalizedHash(ConvertToICalendar(source, googleEvent));
  }

  private static string BuildDescription(GoogleCalendarEvent googleEvent)
  {
    var parts = new List<string>();
    if (!string.IsNullOrWhiteSpace(googleEvent.Description))
    {
      parts.Add(googleEvent.Description);
    }

    if (googleEvent.Attendees.Count > 0)
    {
      parts.Add("Partecipanti:");
      parts.AddRange(googleEvent.Attendees.Select(attendee => $"- {attendee}"));
    }

    return string.Join(Environment.NewLine, parts);
  }

  private string GetImportedSummary(string summary)
  {
    return GetImportedSummary(null, summary);
  }

  private string GetImportedSummary(GoogleCalendarConfiguration? source, string summary)
  {
    var prefix = NormalizeTitlePrefix(source?.TitlePrefix ?? configuration.Sync.ImportedTitlePrefix);
    if (string.IsNullOrEmpty(prefix) || summary.StartsWith(prefix, StringComparison.Ordinal))
    {
      return summary;
    }

    return prefix + summary;
  }

  private static string NormalizeTitlePrefix(string? prefix)
  {
    if (string.IsNullOrWhiteSpace(prefix))
    {
      return "";
    }

    return prefix.Trim() + " ";
  }

  private static string MapStatus(string status)
  {
    return status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ? "CANCELLED" : "CONFIRMED";
  }

  private static DateTimeOffset GetStableTimestamp(GoogleCalendarEvent googleEvent)
  {
    if (googleEvent.Updated != DateTimeOffset.MinValue)
    {
      return googleEvent.Updated;
    }

    return googleEvent.StartsAt ?? DateTimeOffset.UnixEpoch;
  }

  private static void Append(StringBuilder builder, string line)
  {
    builder.Append(line).Append("\r\n");
  }

  private static string FormatUtc(DateTimeOffset value)
  {
    return value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
  }

  private static string FormatLocal(DateTimeOffset value)
  {
    return value.DateTime.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
  }

  private static string EscapeText(string value)
  {
    return value
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace(";", "\\;", StringComparison.Ordinal)
      .Replace(",", "\\,", StringComparison.Ordinal)
      .Replace("\r\n", "\\n", StringComparison.Ordinal)
      .Replace("\n", "\\n", StringComparison.Ordinal);
  }
}
