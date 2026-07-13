using System.Xml.Linq;

namespace CarbonioGoogleCalendarSync.Carbonio;

public sealed class CalDavResponseParser
{
  private static readonly XNamespace Dav = "DAV:";
  private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

  public bool IsCalendarResource(string xml, string expectedDisplayName)
  {
    var document = XDocument.Parse(xml);
    var responses = document.Descendants(Dav + "response");
    return responses.Any(response =>
      response.Descendants(Dav + "displayname").Any(name => name.Value == expectedDisplayName) &&
      response.Descendants(CalDav + "calendar").Any());
  }

  public IReadOnlyList<CalDavResource> ParseCalendarMultistatus(string xml, Uri calendarUri)
  {
    var document = XDocument.Parse(xml);
    return document.Descendants(Dav + "response")
      .Select(response => ParseResource(response, calendarUri))
      .Where(resource => resource is not null)
      .Select(resource => resource!)
      .ToList();
  }

  private static CalDavResource? ParseResource(XElement response, Uri calendarUri)
  {
    var href = response.Element(Dav + "href")?.Value;
    if (string.IsNullOrWhiteSpace(href))
    {
      return null;
    }

    var uri = Uri.TryCreate(href, UriKind.Absolute, out var absolute)
      ? absolute
      : new Uri(calendarUri.GetLeftPart(UriPartial.Authority) + href);
    var etag = response.Descendants(Dav + "getetag").FirstOrDefault()?.Value;
    var data = response.Descendants(CalDav + "calendar-data").FirstOrDefault()?.Value;
    return new CalDavResource(uri, etag, data);
  }
}
