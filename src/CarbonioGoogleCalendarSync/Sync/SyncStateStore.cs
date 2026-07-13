namespace CarbonioGoogleCalendarSync.Sync;

using CarbonioGoogleCalendarSync.Configuration;
using Microsoft.Data.Sqlite;

public sealed record SyncStateEntry(
  string GoogleCalendarId,
  string GoogleEventId,
  string? GoogleRecurringEventId,
  string ICalendarUid,
  string CalDavUrl,
  string? ETag,
  DateTimeOffset GoogleUpdated,
  string ContentHash,
  DateTimeOffset LastSyncedAt,
  bool IsDeleted);

public sealed class SyncStateStore(AppConfiguration configuration)
{
  private readonly string _databasePath = ResolveDatabasePath(configuration.Sync.StateDatabasePath);

  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
    MigrateLegacyStateIfNeeded(_databasePath, configuration.Sync.StateDatabasePath);
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      CREATE TABLE IF NOT EXISTS sync_state (
        google_calendar_id TEXT NOT NULL,
        google_event_id TEXT NOT NULL,
        google_recurring_event_id TEXT NULL,
        icalendar_uid TEXT NOT NULL,
        caldav_url TEXT NOT NULL,
        etag TEXT NULL,
        google_updated TEXT NOT NULL,
        content_hash TEXT NOT NULL,
        last_synced_at TEXT NOT NULL,
        is_deleted INTEGER NOT NULL DEFAULT 0,
        PRIMARY KEY (google_calendar_id, google_event_id)
      );
      """;
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task<IReadOnlyDictionary<string, SyncStateEntry>> LoadActiveByGoogleIdAsync(CancellationToken cancellationToken)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      SELECT google_calendar_id, google_event_id, google_recurring_event_id, icalendar_uid, caldav_url,
             etag, google_updated, content_hash, last_synced_at, is_deleted
      FROM sync_state
      WHERE google_calendar_id = $calendarId AND is_deleted = 0;
      """;
    command.Parameters.AddWithValue("$calendarId", configuration.Google.CalendarId);

    var result = new Dictionary<string, SyncStateEntry>(StringComparer.Ordinal);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      var entry = ReadEntry(reader);
      result[entry.GoogleEventId] = entry;
    }

    return result;
  }

  public async Task UpsertAsync(SyncStateEntry entry, CancellationToken cancellationToken)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      INSERT INTO sync_state (
        google_calendar_id, google_event_id, google_recurring_event_id, icalendar_uid, caldav_url,
        etag, google_updated, content_hash, last_synced_at, is_deleted
      )
      VALUES (
        $calendarId, $eventId, $recurringEventId, $uid, $url,
        $etag, $updated, $hash, $syncedAt, $isDeleted
      )
      ON CONFLICT(google_calendar_id, google_event_id) DO UPDATE SET
        google_recurring_event_id = excluded.google_recurring_event_id,
        icalendar_uid = excluded.icalendar_uid,
        caldav_url = excluded.caldav_url,
        etag = excluded.etag,
        google_updated = excluded.google_updated,
        content_hash = excluded.content_hash,
        last_synced_at = excluded.last_synced_at,
        is_deleted = excluded.is_deleted;
      """;
    AddParameters(command, entry);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  public async Task MarkDeletedAsync(SyncStateEntry entry, CancellationToken cancellationToken)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
      UPDATE sync_state
      SET etag = $etag, last_synced_at = $syncedAt, is_deleted = 1
      WHERE google_calendar_id = $calendarId AND google_event_id = $eventId;
      """;
    command.Parameters.AddWithValue("$etag", (object?)entry.ETag ?? DBNull.Value);
    command.Parameters.AddWithValue("$syncedAt", DateTimeOffset.UtcNow.ToString("O"));
    command.Parameters.AddWithValue("$calendarId", entry.GoogleCalendarId);
    command.Parameters.AddWithValue("$eventId", entry.GoogleEventId);
    await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private SqliteConnection CreateConnection()
  {
    return new SqliteConnection($"Data Source={_databasePath}");
  }

  private static string ResolveDatabasePath(string configuredPath)
  {
    if (Path.IsPathRooted(configuredPath))
    {
      return Path.GetFullPath(configuredPath);
    }

    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "CarbonioGoogleCalendarSync",
      configuredPath.Replace('/', Path.DirectorySeparatorChar));
  }

  private static void MigrateLegacyStateIfNeeded(string targetPath, string configuredPath)
  {
    if (Path.IsPathRooted(configuredPath))
    {
      return;
    }

    var targetActiveCount = File.Exists(targetPath) ? CountActiveEntries(targetPath) : 0;
    var bestCandidate = GetLegacyCandidates(configuredPath)
      .Where(File.Exists)
      .Select(path => new { Path = path, ActiveCount = CountActiveEntries(path) })
      .Where(candidate => candidate.ActiveCount > targetActiveCount)
      .OrderByDescending(candidate => candidate.ActiveCount)
      .FirstOrDefault();

    if (bestCandidate is null)
    {
      return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
    if (File.Exists(targetPath))
    {
      File.Copy(targetPath, targetPath + ".bak", overwrite: true);
    }

    File.Copy(bestCandidate.Path, targetPath, overwrite: true);
  }

  private static IEnumerable<string> GetLegacyCandidates(string configuredPath)
  {
    yield return Path.GetFullPath(configuredPath);
    yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", configuredPath));
  }

  private static int CountActiveEntries(string databasePath)
  {
    try
    {
      using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText = "SELECT COUNT(*) FROM sync_state WHERE is_deleted = 0;";
      return Convert.ToInt32(command.ExecuteScalar());
    }
    catch
    {
      return 0;
    }
  }

  private static SyncStateEntry ReadEntry(SqliteDataReader reader)
  {
    return new SyncStateEntry(
      reader.GetString(0),
      reader.GetString(1),
      reader.IsDBNull(2) ? null : reader.GetString(2),
      reader.GetString(3),
      reader.GetString(4),
      reader.IsDBNull(5) ? null : reader.GetString(5),
      DateTimeOffset.Parse(reader.GetString(6)),
      reader.GetString(7),
      DateTimeOffset.Parse(reader.GetString(8)),
      reader.GetInt32(9) != 0);
  }

  private static void AddParameters(SqliteCommand command, SyncStateEntry entry)
  {
    command.Parameters.AddWithValue("$calendarId", entry.GoogleCalendarId);
    command.Parameters.AddWithValue("$eventId", entry.GoogleEventId);
    command.Parameters.AddWithValue("$recurringEventId", (object?)entry.GoogleRecurringEventId ?? DBNull.Value);
    command.Parameters.AddWithValue("$uid", entry.ICalendarUid);
    command.Parameters.AddWithValue("$url", entry.CalDavUrl);
    command.Parameters.AddWithValue("$etag", (object?)entry.ETag ?? DBNull.Value);
    command.Parameters.AddWithValue("$updated", entry.GoogleUpdated.ToString("O"));
    command.Parameters.AddWithValue("$hash", entry.ContentHash);
    command.Parameters.AddWithValue("$syncedAt", entry.LastSyncedAt.ToString("O"));
    command.Parameters.AddWithValue("$isDeleted", entry.IsDeleted ? 1 : 0);
  }
}
