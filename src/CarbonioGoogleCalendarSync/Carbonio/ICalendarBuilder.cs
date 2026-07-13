using System.Globalization;
using System.Text;

namespace CarbonioGoogleCalendarSync.Carbonio;

public static class ICalendarBuilder
{
  public static string Build(CalDavEvent calDavEvent, DateTimeOffset timestamp)
  {
    var builder = new StringBuilder();
    Append(builder, "BEGIN:VCALENDAR");
    Append(builder, "VERSION:2.0");
    Append(builder, "PRODID:-//CarbonioGoogleCalendarSync//IT");
    Append(builder, "CALSCALE:GREGORIAN");
    Append(builder, "BEGIN:VEVENT");
    Append(builder, $"UID:{EscapeText(calDavEvent.Uid)}");
    Append(builder, $"DTSTAMP:{FormatUtc(timestamp)}");
    Append(builder, $"DTSTART;TZID={calDavEvent.TimeZoneId}:{FormatLocal(calDavEvent.StartsAt)}");
    Append(builder, $"DTEND;TZID={calDavEvent.TimeZoneId}:{FormatLocal(calDavEvent.EndsAt)}");
    Append(builder, $"SUMMARY:{EscapeText(calDavEvent.Summary)}");
    Append(builder, $"DESCRIPTION:{EscapeText(calDavEvent.Description)}");

    foreach (var property in calDavEvent.CustomProperties.OrderBy(item => item.Key, StringComparer.Ordinal))
    {
      Append(builder, $"{property.Key}:{EscapeText(property.Value)}");
    }

    Append(builder, "END:VEVENT");
    Append(builder, "END:VCALENDAR");
    return builder.ToString();
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
