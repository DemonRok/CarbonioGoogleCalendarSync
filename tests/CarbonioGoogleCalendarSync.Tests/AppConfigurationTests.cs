using CarbonioGoogleCalendarSync.Configuration;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class AppConfigurationTests
{
  [Fact]
  public void GetCarbonioCalendarUriBuildsDestinationFromGoogleCalendar()
  {
    var config = CreateConfig();
    var source = new GoogleCalendarConfiguration(
      "work",
      "https://calendar.google.com/calendar/ical/work/private-token/basic.ics",
      carbonioCalendarName: "Work Calendar");

    var uri = config.GetCarbonioCalendarUri(source);

    Assert.Equal("https://webmail.example.local/dav/user%40example.local/Work%20Calendar/", uri.AbsoluteUri);
  }

  [Fact]
  public void ValidateAllowsAnyPerCalendarDestination()
  {
    var config = CreateConfig() with
    {
      Google = new GoogleConfiguration
      {
        Calendars =
        [
          new GoogleCalendarConfiguration(
            "work",
            "https://calendar.google.com/calendar/ical/work/private-token/basic.ics",
            carbonioCalendarName: "Work")
        ]
      }
    };

    config.Validate();
  }

  private static AppConfiguration CreateConfig()
  {
    return new AppConfiguration(
      new CarbonioConfiguration
      {
        BaseUrl = "https://webmail.example.local",
        Username = "user@example.local"
      },
      new GoogleConfiguration
      {
        Calendars =
        [
          new GoogleCalendarConfiguration(
            "primary",
            "https://calendar.google.com/calendar/ical/primary/private-token/basic.ics")
        ]
      },
      new SyncConfiguration(),
      new LoggingConfiguration(),
      new HttpConfiguration());
  }
}
