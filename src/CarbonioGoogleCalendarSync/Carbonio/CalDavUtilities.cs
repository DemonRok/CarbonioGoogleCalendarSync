using System.Security.Cryptography;
using System.Text;

namespace CarbonioGoogleCalendarSync.Carbonio;

public static class CalDavUtilities
{
  public static string EscapeCalendarUser(string username)
  {
    return Uri.EscapeDataString(username);
  }

  public static Uri BuildEventUri(Uri calendarUri, string uid)
  {
    var baseText = calendarUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
      ? calendarUri.AbsoluteUri
      : calendarUri.AbsoluteUri + "/";
    return new Uri(baseText + Uri.EscapeDataString(uid) + ".ics");
  }

  public static string GenerateTestUid()
  {
    return $"carbonio-google-sync-test-{Guid.NewGuid():N}@google-carbonio-sync";
  }

  public static string GenerateGoogleUid(string googleEventId)
  {
    return $"{googleEventId}@google-carbonio-sync";
  }

  public static string ComputeNormalizedHash(string content)
  {
    var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
  }

  public static bool IsManagedEvent(string calendarData)
  {
    return calendarData.Contains("X-CARBONIO-GOOGLE-SYNC:TRUE", StringComparison.OrdinalIgnoreCase);
  }
}
