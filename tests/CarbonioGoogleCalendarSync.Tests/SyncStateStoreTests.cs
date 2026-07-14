using CarbonioGoogleCalendarSync.Configuration;
using CarbonioGoogleCalendarSync.Sync;

namespace CarbonioGoogleCalendarSync.Tests;

public sealed class SyncStateStoreTests
{
  [Fact]
  public async Task UpsertAndLoadActiveByGoogleIdRoundTripsState()
  {
    var path = Path.Combine(Path.GetTempPath(), $"cgcs-{Guid.NewGuid():N}.db");
    var store = new SyncStateStore(CreateConfig(path));
    await store.InitializeAsync(CancellationToken.None);

    var entry = new SyncStateEntry(
      "primary",
      "google-1",
      null,
      "google-1@google-carbonio-sync",
      "https://example.local/event.ics",
      "\"etag\"",
      DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
      "HASH",
      DateTimeOffset.Parse("2026-07-13T11:00:00Z"),
      false);

    await store.UpsertAsync(entry, CancellationToken.None);
    var loaded = await store.LoadActiveByGoogleIdAsync(CancellationToken.None);

    Assert.True(loaded.ContainsKey("primary\ngoogle-1"));
    Assert.Equal("\"etag\"", loaded["primary\ngoogle-1"].ETag);
    Assert.Equal("HASH", loaded["primary\ngoogle-1"].ContentHash);
  }

  [Fact]
  public async Task MarkDeletedHidesEntryFromActiveLoad()
  {
    var path = Path.Combine(Path.GetTempPath(), $"cgcs-{Guid.NewGuid():N}.db");
    var store = new SyncStateStore(CreateConfig(path));
    await store.InitializeAsync(CancellationToken.None);
    var entry = new SyncStateEntry(
      "primary",
      "google-1",
      null,
      "google-1@google-carbonio-sync",
      "https://example.local/event.ics",
      null,
      DateTimeOffset.UtcNow,
      "HASH",
      DateTimeOffset.UtcNow,
      false);

    await store.UpsertAsync(entry, CancellationToken.None);
    await store.MarkDeletedAsync(entry, CancellationToken.None);

    var loaded = await store.LoadActiveByGoogleIdAsync(CancellationToken.None);

    Assert.Empty(loaded);
  }

  private static AppConfiguration CreateConfig(string path)
  {
    return new AppConfiguration(
      new CarbonioConfiguration
      {
        BaseUrl = "https://webmail.example.local",
        Username = "user@example.local"
      },
      new GoogleConfiguration { CalendarId = "primary", IcsUrl = "https://calendar.google.com/calendar/ical/example/basic.ics" },
      new SyncConfiguration { StateDatabasePath = path },
      new LoggingConfiguration(),
      new HttpConfiguration());
  }
}
