using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CarbonioGoogleCalendarSync.Configuration;

namespace CarbonioGoogleCalendarSync.Carbonio;

public sealed class CalDavClient(HttpClient httpClient, AppConfiguration configuration, CalDavResponseParser parser)
{
  private readonly Uri _calendarUri = configuration.GetCarbonioCalendarUri();

  public async Task<CalDavOperationResult> PropFindCalendarAsync(string password, CancellationToken cancellationToken)
  {
    return await PropFindCalendarAsync(_calendarUri, password, cancellationToken);
  }

  public async Task<CalDavOperationResult> PropFindCalendarAsync(Uri calendarUri, string password, CancellationToken cancellationToken)
  {
    const string body = """
      <?xml version="1.0" encoding="utf-8" ?>
      <D:propfind xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
        <D:prop>
          <D:displayname />
          <D:resourcetype />
        </D:prop>
      </D:propfind>
      """;
    using var request = CreateRequest(new HttpMethod("PROPFIND"), calendarUri, password);
    request.Headers.Add("Depth", "0");
    request.Content = XmlContent(body);
    return await SendAsync(request, cancellationToken);
  }

  public async Task<CalDavOperationResult> PutEventAsync(Uri eventUri, string ics, string password, string ifMatch, CancellationToken cancellationToken)
  {
    using var request = CreateRequest(HttpMethod.Put, eventUri, password);
    request.Headers.TryAddWithoutValidation(ifMatch == "*" ? "If-None-Match" : "If-Match", ifMatch);
    request.Content = new StringContent(ics, Encoding.UTF8, "text/calendar");
    return await SendAsync(request, cancellationToken);
  }

  public async Task<CalDavOperationResult> GetEventAsync(Uri eventUri, string password, CancellationToken cancellationToken)
  {
    using var request = CreateRequest(HttpMethod.Get, eventUri, password);
    return await SendAsync(request, cancellationToken);
  }

  public async Task<CalDavOperationResult> ReportEventAsync(string uid, string password, CancellationToken cancellationToken)
  {
    var body = $$"""
      <?xml version="1.0" encoding="utf-8" ?>
      <C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
        <D:prop>
          <D:getetag />
          <C:calendar-data />
        </D:prop>
        <C:filter>
          <C:comp-filter name="VCALENDAR">
            <C:comp-filter name="VEVENT">
              <C:prop-filter name="UID">
                <C:text-match collation="i;octet">{{SecurityElementEscape(uid)}}</C:text-match>
              </C:prop-filter>
            </C:comp-filter>
          </C:comp-filter>
        </C:filter>
      </C:calendar-query>
      """;
    using var request = CreateRequest(new HttpMethod("REPORT"), _calendarUri, password);
    request.Headers.Add("Depth", "1");
    request.Content = XmlContent(body);
    return await SendAsync(request, cancellationToken);
  }

  public async Task<CalDavOperationResult> ReportManagedEventsAsync(string password, CancellationToken cancellationToken)
  {
    return await ReportManagedEventsAsync(_calendarUri, password, cancellationToken);
  }

  public async Task<CalDavOperationResult> ReportManagedEventsAsync(Uri calendarUri, string password, CancellationToken cancellationToken)
  {
    const string body = """
      <?xml version="1.0" encoding="utf-8" ?>
      <C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
        <D:prop>
          <D:getetag />
          <C:calendar-data />
        </D:prop>
        <C:filter>
          <C:comp-filter name="VCALENDAR">
            <C:comp-filter name="VEVENT">
              <C:prop-filter name="X-CARBONIO-GOOGLE-SYNC">
                <C:text-match collation="i;ascii-casemap">TRUE</C:text-match>
              </C:prop-filter>
            </C:comp-filter>
          </C:comp-filter>
        </C:filter>
      </C:calendar-query>
      """;
    using var request = CreateRequest(new HttpMethod("REPORT"), calendarUri, password);
    request.Headers.Add("Depth", "1");
    request.Content = XmlContent(body);
    return await SendAsync(request, cancellationToken);
  }

  public async Task<CalDavOperationResult> DeleteEventAsync(Uri eventUri, string? etag, string password, CancellationToken cancellationToken)
  {
    using var request = CreateRequest(HttpMethod.Delete, eventUri, password);
    if (!string.IsNullOrWhiteSpace(etag))
    {
      request.Headers.TryAddWithoutValidation("If-Match", etag);
    }

    return await SendAsync(request, cancellationToken);
  }

  public bool IsCalendarResource(string xml)
  {
    return parser.IsCalendarResource(xml, configuration.GetCarbonioCalendarName());
  }

  public bool IsCalendarResourceAtUri(string xml)
  {
    return parser.IsCalendarResource(xml);
  }

  public bool IsCalendarResource(string xml, string calendarName)
  {
    return parser.IsCalendarResource(xml, calendarName);
  }

  public IReadOnlyList<CalDavResource> ParseReport(string xml)
  {
    return parser.ParseCalendarMultistatus(xml, _calendarUri);
  }

  public IReadOnlyList<CalDavResource> ParseReport(string xml, Uri calendarUri)
  {
    return parser.ParseCalendarMultistatus(xml, calendarUri);
  }

  public Uri BuildEventUri(string uid)
  {
    return CalDavUtilities.BuildEventUri(_calendarUri, uid);
  }

  public Uri BuildEventUri(Uri calendarUri, string uid)
  {
    return CalDavUtilities.BuildEventUri(calendarUri, uid);
  }

  private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string password)
  {
    var request = new HttpRequestMessage(method, uri);
    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration.Carbonio.Username}:{password}"));
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
    return request;
  }

  private static StringContent XmlContent(string body)
  {
    return new StringContent(body, Encoding.UTF8, "application/xml");
  }

  private async Task<CalDavOperationResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    using var response = await httpClient.SendAsync(request, cancellationToken);
    var body = response.Content is null
      ? null
      : await response.Content.ReadAsStringAsync(cancellationToken);
    var etag = response.Headers.ETag?.Tag;
    if (etag is null && response.Headers.TryGetValues("ETag", out var values))
    {
      etag = values.FirstOrDefault();
    }

    return new CalDavOperationResult(response.StatusCode, etag, body);
  }

  private static string SecurityElementEscape(string value)
  {
    return System.Security.SecurityElement.Escape(value) ?? "";
  }
}
