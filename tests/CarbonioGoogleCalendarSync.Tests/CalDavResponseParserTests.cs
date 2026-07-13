using CarbonioGoogleCalendarSync.Carbonio;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class CalDavResponseParserTests
{
  [Fact]
  public void IsCalendarResourceRecognizesCalendarDisplayName()
  {
    const string xml = """
      <D:multistatus xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
        <D:response>
          <D:href>/dav/user%40example.local/Google/</D:href>
          <D:propstat>
            <D:prop>
              <D:displayname>Google</D:displayname>
              <D:resourcetype><D:collection/><C:calendar/></D:resourcetype>
            </D:prop>
          </D:propstat>
        </D:response>
      </D:multistatus>
      """;

    var parser = new CalDavResponseParser();

    Assert.True(parser.IsCalendarResource(xml, "Google"));
  }

  [Fact]
  public void ParseCalendarMultistatusReadsHrefEtagAndCalendarData()
  {
    const string xml = """
      <D:multistatus xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
        <D:response>
          <D:href>/dav/user%40example.local/Google/test.ics</D:href>
          <D:propstat>
            <D:prop>
              <D:getetag>"abc"</D:getetag>
              <C:calendar-data>BEGIN:VCALENDAR
      X-CARBONIO-GOOGLE-SYNC:TRUE
      END:VCALENDAR</C:calendar-data>
            </D:prop>
          </D:propstat>
        </D:response>
      </D:multistatus>
      """;

    var parser = new CalDavResponseParser();
    var resources = parser.ParseCalendarMultistatus(xml, new Uri("https://webmail.example.local/dav/user%40example.local/Google/"));

    Assert.Single(resources);
    Assert.Equal("https://webmail.example.local/dav/user%40example.local/Google/test.ics", resources[0].Url.AbsoluteUri);
    Assert.Equal("\"abc\"", resources[0].ETag);
    Assert.Contains("X-CARBONIO-GOOGLE-SYNC:TRUE", resources[0].CalendarData);
  }
}
