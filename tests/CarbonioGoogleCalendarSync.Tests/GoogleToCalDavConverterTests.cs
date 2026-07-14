using CarbonioGoogleCalendarSync.Configuration;
using CarbonioGoogleCalendarSync.Google;
using CarbonioGoogleCalendarSync.Sync;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class GoogleToCalDavConverterTests
{
  [Fact]
  public void ConvertToICalendarCreatesTimedEventWithGoogleMetadata()
  {
    var converter = new GoogleToCalDavConverter(CreateConfig());
    var googleEvent = new GoogleCalendarEvent(
      "abc123",
      null,
      "Riunione",
      "Descrizione",
      "Sala 1",
      new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.FromHours(2)),
      new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.FromHours(2)),
      null,
      null,
      "Europe/Rome",
      new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero),
      "confirmed",
      ["Mario Rossi <mario@example.local>"]);

    var ics = converter.ConvertToICalendar(googleEvent);

    Assert.Contains("UID:abc123@google-carbonio-sync", ics);
    Assert.Contains("SUMMARY:(G) Riunione", ics);
    Assert.Contains("DTSTART;TZID=Europe/Rome:20260713T090000", ics);
    Assert.Contains("LOCATION:Sala 1", ics);
    Assert.Contains("X-CARBONIO-GOOGLE-SYNC:TRUE", ics);
    Assert.Contains("X-GOOGLE-EVENT-ID:abc123", ics);
    Assert.Contains("X-GOOGLE-CALENDAR-ID:primary", ics);
  }

  [Fact]
  public void ConvertToICalendarCreatesAllDayEvent()
  {
    var converter = new GoogleToCalDavConverter(CreateConfig());
    var googleEvent = new GoogleCalendarEvent(
      "allday",
      null,
      "Ferie",
      null,
      null,
      null,
      null,
      new DateOnly(2026, 8, 1),
      new DateOnly(2026, 8, 2),
      null,
      new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero),
      "confirmed",
      []);

    var ics = converter.ConvertToICalendar(googleEvent);

    Assert.Contains("DTSTART;VALUE=DATE:20260801", ics);
    Assert.Contains("DTEND;VALUE=DATE:20260802", ics);
  }

  [Fact]
  public void ConvertToICalendarUsesSourceCalendarPrefixAndMetadata()
  {
    var converter = new GoogleToCalDavConverter(CreateConfig());
    var googleEvent = CreateBasicEvent("same-id", "Riunione");
    var source = new GoogleCalendarConfiguration(
      "work",
      "https://calendar.google.com/calendar/ical/work/private-token/basic.ics",
      "(W)");

    var ics = converter.ConvertToICalendar(source, googleEvent);

    Assert.Contains("SUMMARY:(W) Riunione", ics);
    Assert.Contains("X-GOOGLE-CALENDAR-ID:work", ics);
    Assert.DoesNotContain("UID:same-id@google-carbonio-sync", ics);
  }

  [Fact]
  public void GetUidKeepsLegacyUidOnlyForLegacySource()
  {
    var converter = new GoogleToCalDavConverter(CreateConfig());
    var googleEvent = CreateBasicEvent("same-id", "Riunione");
    var legacySource = new GoogleCalendarConfiguration(
      "primary",
      "https://calendar.google.com/calendar/ical/primary/private-token/basic.ics",
      "(G)",
      useLegacyUid: true);
    var additionalSource = new GoogleCalendarConfiguration(
      "work",
      "https://calendar.google.com/calendar/ical/work/private-token/basic.ics",
      "(W)");

    Assert.Equal("same-id@google-carbonio-sync", converter.GetUid(legacySource, googleEvent));
    Assert.NotEqual(
      converter.GetUid(legacySource, googleEvent),
      converter.GetUid(additionalSource, googleEvent));
  }

  private static AppConfiguration CreateConfig()
  {
    return new AppConfiguration(
      new CarbonioConfiguration
      {
        BaseUrl = "https://webmail.example.local",
        Username = "user@example.local"
      },
      new GoogleConfiguration { CalendarId = "primary" },
      new SyncConfiguration(),
      new LoggingConfiguration(),
      new HttpConfiguration());
  }

  private static GoogleCalendarEvent CreateBasicEvent(string id, string summary)
  {
    return new GoogleCalendarEvent(
      id,
      null,
      summary,
      null,
      null,
      new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.FromHours(2)),
      new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.FromHours(2)),
      null,
      null,
      "Europe/Rome",
      new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero),
      "confirmed",
      []);
  }
}
