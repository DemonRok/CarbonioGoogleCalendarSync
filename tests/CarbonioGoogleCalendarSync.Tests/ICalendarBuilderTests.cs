using CarbonioGoogleCalendarSync.Carbonio;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class ICalendarBuilderTests
{
  [Fact]
  public void BuildCreatesTimedEventWithEuropeRomeAndCustomMarker()
  {
    var calDavEvent = new CalDavEvent(
      "uid-1@google-carbonio-sync",
      "Test CarbonioGoogleCalendarSync",
      "Evento creato automaticamente per verificare CalDAV",
      new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.FromHours(2)),
      new DateTimeOffset(2026, 7, 13, 12, 30, 0, TimeSpan.FromHours(2)),
      "Europe/Rome",
      new Dictionary<string, string>
      {
        ["X-CARBONIO-GOOGLE-SYNC"] = "TRUE"
      });

    var ics = ICalendarBuilder.Build(calDavEvent, new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));

    Assert.Contains("UID:uid-1@google-carbonio-sync", ics);
    Assert.Contains("DTSTART;TZID=Europe/Rome:20260713T120000", ics);
    Assert.Contains("DTEND;TZID=Europe/Rome:20260713T123000", ics);
    Assert.Contains("X-CARBONIO-GOOGLE-SYNC:TRUE", ics);
  }
}
