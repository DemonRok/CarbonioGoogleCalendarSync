namespace CarbonioGoogleCalendarSync.Sync;

public sealed record SyncResult(
  int Created,
  int Updated,
  int Deleted,
  int Unchanged,
  int Conflicts);
