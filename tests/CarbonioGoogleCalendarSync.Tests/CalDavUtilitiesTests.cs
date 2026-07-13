using CarbonioGoogleCalendarSync.Carbonio;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class CalDavUtilitiesTests
{
  [Fact]
  public void EscapeCalendarUserEscapesEmailAddress()
  {
    Assert.Equal("user.name%40example.local", CalDavUtilities.EscapeCalendarUser("user.name@example.local"));
  }

  [Fact]
  public void BuildEventUriAppendsEscapedUidAndIcsExtension()
  {
    var uri = CalDavUtilities.BuildEventUri(
      new Uri("https://webmail.example.local/dav/user%40example.local/Google/"),
      "event id@google-carbonio-sync");

    Assert.Equal("https://webmail.example.local/dav/user%40example.local/Google/event%20id%40google-carbonio-sync.ics", uri.AbsoluteUri);
  }

  [Fact]
  public void GenerateGoogleUidUsesStableSuffix()
  {
    Assert.Equal("abc123@google-carbonio-sync", CalDavUtilities.GenerateGoogleUid("abc123"));
  }

  [Fact]
  public void ComputeNormalizedHashIgnoresLineEndingDifferences()
  {
    var first = CalDavUtilities.ComputeNormalizedHash("A\r\nB\r\n");
    var second = CalDavUtilities.ComputeNormalizedHash("A\nB");

    Assert.Equal(first, second);
  }

  [Fact]
  public void IsManagedEventRequiresSyncMarker()
  {
    Assert.True(CalDavUtilities.IsManagedEvent("BEGIN:VEVENT\r\nX-CARBONIO-GOOGLE-SYNC:TRUE\r\nEND:VEVENT"));
    Assert.False(CalDavUtilities.IsManagedEvent("BEGIN:VEVENT\r\nSUMMARY:Privato\r\nEND:VEVENT"));
  }
}
