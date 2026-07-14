using CarbonioGoogleCalendarSync.Google;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class GoogleCalendarClientTests
{
  [Fact]
  public void ParseEventsUsesExplicitDtEndWhenDurationIsMissing()
  {
    const string ics = """
      BEGIN:VCALENDAR
      VERSION:2.0
      BEGIN:VEVENT
      UID:test-event@google.com
      SUMMARY:Appuntamento
      DTSTART:20260713T140000
      DTEND:20260713T150000
      END:VEVENT
      END:VCALENDAR
      """;

    var events = GoogleCalendarClient.ParseEvents(ics, 30, 30);

    var googleEvent = Assert.Single(events);
    Assert.Equal(new TimeSpan(14, 0, 0), googleEvent.StartsAt!.Value.TimeOfDay);
    Assert.Equal(new TimeSpan(15, 0, 0), googleEvent.EndsAt!.Value.TimeOfDay);
  }
}
