namespace CarbonioGoogleCalendarSync.Sync;

using System.Net;
using CarbonioGoogleCalendarSync.Carbonio;
using CarbonioGoogleCalendarSync.Configuration;
using CarbonioGoogleCalendarSync.Google;
using CarbonioGoogleCalendarSync.Security;
using Microsoft.Extensions.Logging;

public sealed class CalendarSyncService(
  AppConfiguration configuration,
  GoogleCalendarClient googleClient,
  CalDavClient calDavClient,
  GoogleToCalDavConverter converter,
  SyncStateStore stateStore,
  ICredentialStore credentialStore,
  IConsolePasswordReader passwordReader,
  ILogger<CalendarSyncService> logger)
{
  public async Task<int> RunAsync(bool dryRunArgument, CancellationToken cancellationToken)
  {
    var dryRun = dryRunArgument || configuration.Sync.DryRun;

    logger.LogInformation("Avvio sincronizzazione Google -> Carbonio. DryRun={DryRun}", dryRun);
    await stateStore.InitializeAsync(cancellationToken);
    var events = await googleClient.GetEventsAsync(cancellationToken);
    var stateByGoogleId = await stateStore.LoadActiveByGoogleIdAsync(cancellationToken);
    var currentGoogleIds = events.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

    var password = await credentialStore.GetCarbonioPasswordAsync(configuration.Carbonio.Username, cancellationToken)
      ?? (dryRun ? null : passwordReader.ReadPassword("Password Carbonio: "));
    var managedResources = password is null
      ? new Dictionary<string, CalDavResource>(StringComparer.Ordinal)
      : await LoadStateResourcesAsync(stateByGoogleId.Values, password, cancellationToken);
    var plan = BuildPlan(events, stateByGoogleId, managedResources, password is not null);

    PrintPlan(plan);
    if (dryRun)
    {
      Console.WriteLine(password is null
        ? "Dry-run completato senza verifica Carbonio: password non salvata. Nessuna scrittura su Carbonio."
        : "Dry-run completato con verifica Carbonio. Nessuna scrittura su Carbonio.");
      return ExitCodes.Success;
    }

    if (password is null)
    {
      password = passwordReader.ReadPassword("Password Carbonio: ");
    }

    var result = await ApplyPlanAsync(plan, currentGoogleIds, stateByGoogleId, managedResources, password, cancellationToken);
    Console.WriteLine($"Sync completata. Creati={result.Created}, aggiornati={result.Updated}, eliminati={result.Deleted}, invariati={result.Unchanged}, conflitti={result.Conflicts}.");
    return result.Conflicts > 0 ? ExitCodes.CompletedWithConflicts : ExitCodes.Success;
  }

  public async Task<int> PurgeImportedAsync(CancellationToken cancellationToken)
  {
    logger.LogInformation("Avvio cancellazione eventi importati da Google");
    await stateStore.InitializeAsync(cancellationToken);
    var stateByGoogleId = await stateStore.LoadActiveByGoogleIdAsync(cancellationToken);
    if (stateByGoogleId.Count == 0)
    {
      Console.WriteLine("Nessun evento importato presente nello stato locale.");
      return ExitCodes.Success;
    }

    var password = await credentialStore.GetCarbonioPasswordAsync(configuration.Carbonio.Username, cancellationToken)
      ?? passwordReader.ReadPassword("Password Carbonio: ");
    var resources = await LoadStateResourcesAsync(stateByGoogleId.Values, password, cancellationToken);
    var deleted = 0;
    var skipped = 0;
    var conflicts = 0;

    foreach (var state in stateByGoogleId.Values)
    {
      if (!resources.TryGetValue(state.CalDavUrl, out var resource) ||
          resource.CalendarData is null ||
          !CalDavUtilities.IsManagedEvent(resource.CalendarData))
      {
        skipped++;
        Console.WriteLine($"SKIP purge {state.GoogleEventId}: risorsa non trovata o non marcata dal sincronizzatore");
        continue;
      }

      var result = await calDavClient.DeleteEventAsync(new Uri(state.CalDavUrl), resource.ETag ?? state.ETag, password, cancellationToken);
      Console.WriteLine($"DELETE purge {state.GoogleEventId}: HTTP {(int)result.StatusCode} {result.StatusCode}");
      if (result.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
      {
        await stateStore.MarkDeletedAsync(state with { ETag = resource.ETag ?? state.ETag }, cancellationToken);
        deleted++;
      }
      else if (result.StatusCode == HttpStatusCode.PreconditionFailed)
      {
        conflicts++;
      }
      else
      {
        skipped++;
      }
    }

    Console.WriteLine($"Cancellazione importati completata. Eliminati={deleted}, saltati={skipped}, conflitti={conflicts}.");
    return conflicts > 0 ? ExitCodes.CompletedWithConflicts : ExitCodes.Success;
  }

  private SyncPlan BuildPlan(
    IReadOnlyList<GoogleCalendarEvent> events,
    IReadOnlyDictionary<string, SyncStateEntry> stateByGoogleId,
    IReadOnlyDictionary<string, CalDavResource> managedResources,
    bool carbonioVerified)
  {
    var creates = new List<GoogleEventPlan>();
    var recreates = new List<GoogleEventPlan>();
    var updates = new List<GoogleEventPlan>();
    var unchanged = new List<GoogleEventPlan>();

    foreach (var googleEvent in events)
    {
      var ics = converter.ConvertToICalendar(googleEvent);
      var hash = converter.ComputeHash(googleEvent);
      var uid = converter.GetUid(googleEvent);
      var url = calDavClient.BuildEventUri(uid);
      var item = new GoogleEventPlan(googleEvent, uid, url, ics, hash);

      if (!stateByGoogleId.TryGetValue(googleEvent.Id, out var state))
      {
        creates.Add(item);
      }
      else if (carbonioVerified && !managedResources.ContainsKey(state.CalDavUrl))
      {
        recreates.Add(item);
      }
      else if (state.ContentHash != hash)
      {
        updates.Add(item);
      }
      else
      {
        unchanged.Add(item);
      }
    }

    var currentIds = events.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
    var deletes = stateByGoogleId.Values
      .Where(state => !currentIds.Contains(state.GoogleEventId))
      .ToList();

    return new SyncPlan(creates, recreates, updates, deletes, unchanged);
  }

  private static void PrintPlan(SyncPlan plan)
  {
    foreach (var item in plan.Creates)
    {
      Console.WriteLine($"CREATE: {item.GoogleEvent.Summary} | GoogleId={item.GoogleEvent.Id} | Hash={item.Hash[..12]}");
    }

    foreach (var item in plan.Updates)
    {
      Console.WriteLine($"UPDATE: {item.GoogleEvent.Summary} | GoogleId={item.GoogleEvent.Id} | Hash={item.Hash[..12]}");
    }

    foreach (var item in plan.Recreates)
    {
      Console.WriteLine($"RECREATE: {item.GoogleEvent.Summary} | GoogleId={item.GoogleEvent.Id} | Hash={item.Hash[..12]}");
    }

    foreach (var item in plan.Deletes)
    {
      Console.WriteLine($"DELETE: GoogleId={item.GoogleEventId} | Url={item.CalDavUrl}");
    }

    Console.WriteLine($"Piano sync: create={plan.Creates.Count}, recreate={plan.Recreates.Count}, update={plan.Updates.Count}, delete={plan.Deletes.Count}, invariati={plan.Unchanged.Count}.");
  }

  private async Task<IReadOnlyDictionary<string, CalDavResource>> LoadManagedResourcesAsync(string password, CancellationToken cancellationToken)
  {
    var report = await calDavClient.ReportManagedEventsAsync(password, cancellationToken);
    if (report.StatusCode != (HttpStatusCode)207 || report.Body is null)
    {
      logger.LogWarning("REPORT eventi gestiti non riuscito: HTTP {StatusCode}", (int)report.StatusCode);
      return new Dictionary<string, CalDavResource>(StringComparer.Ordinal);
    }

    return calDavClient.ParseReport(report.Body)
      .Where(resource => resource.CalendarData is not null && CalDavUtilities.IsManagedEvent(resource.CalendarData))
      .ToDictionary(resource => resource.Url.AbsoluteUri, resource => resource, StringComparer.Ordinal);
  }

  private async Task<IReadOnlyDictionary<string, CalDavResource>> LoadStateResourcesAsync(
    IEnumerable<SyncStateEntry> stateEntries,
    string password,
    CancellationToken cancellationToken)
  {
    var result = new Dictionary<string, CalDavResource>(StringComparer.Ordinal);
    foreach (var state in stateEntries)
    {
      var get = await calDavClient.GetEventAsync(new Uri(state.CalDavUrl), password, cancellationToken);
      if (get.StatusCode == HttpStatusCode.OK &&
          get.Body is not null &&
          CalDavUtilities.IsManagedEvent(get.Body))
      {
        result[state.CalDavUrl] = new CalDavResource(new Uri(state.CalDavUrl), get.ETag ?? state.ETag, get.Body);
      }
      else if (get.StatusCode != HttpStatusCode.NotFound)
      {
        logger.LogWarning("GET risorsa sincronizzata non riuscito per GoogleId={GoogleEventId}: HTTP {StatusCode}", state.GoogleEventId, (int)get.StatusCode);
      }
    }

    return result;
  }

  private async Task<SyncResult> ApplyPlanAsync(
    SyncPlan plan,
    HashSet<string> currentGoogleIds,
    IReadOnlyDictionary<string, SyncStateEntry> stateByGoogleId,
    IReadOnlyDictionary<string, CalDavResource> managedResources,
    string password,
    CancellationToken cancellationToken)
  {
    var created = 0;
    var updated = 0;
    var deleted = 0;
    var conflicts = 0;

    foreach (var item in plan.Creates)
    {
      var result = await calDavClient.PutEventAsync(item.CalDavUrl, item.ICalendar, password, "*", cancellationToken);
      Console.WriteLine($"PUT create {item.GoogleEvent.Id}: HTTP {(int)result.StatusCode} {result.StatusCode}");
      if (result.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent)
      {
        await stateStore.UpsertAsync(ToStateEntry(item, result.ETag), cancellationToken);
        created++;
      }
      else if (result.StatusCode == HttpStatusCode.PreconditionFailed)
      {
        conflicts++;
      }
    }

    foreach (var item in plan.Recreates)
    {
      var result = await calDavClient.PutEventAsync(item.CalDavUrl, item.ICalendar, password, "*", cancellationToken);
      Console.WriteLine($"PUT recreate {item.GoogleEvent.Id}: HTTP {(int)result.StatusCode} {result.StatusCode}");
      if (result.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent)
      {
        await stateStore.UpsertAsync(ToStateEntry(item, result.ETag), cancellationToken);
        created++;
      }
      else if (result.StatusCode == HttpStatusCode.PreconditionFailed)
      {
        conflicts++;
      }
    }

    foreach (var item in plan.Updates)
    {
      var urlText = item.CalDavUrl.AbsoluteUri;
      var etag = stateByGoogleId.TryGetValue(item.GoogleEvent.Id, out var entry) ? entry.ETag : null;
      if (managedResources.TryGetValue(urlText, out var resource))
      {
        etag = resource.ETag ?? etag;
      }

      var result = await calDavClient.PutEventAsync(item.CalDavUrl, item.ICalendar, password, etag ?? "*", cancellationToken);
      Console.WriteLine($"PUT update {item.GoogleEvent.Id}: HTTP {(int)result.StatusCode} {result.StatusCode}");
      if (result.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent)
      {
        await stateStore.UpsertAsync(ToStateEntry(item, result.ETag ?? etag), cancellationToken);
        updated++;
      }
      else if (result.StatusCode == HttpStatusCode.PreconditionFailed)
      {
        conflicts++;
      }
    }

    if (configuration.Sync.DeleteRemovedEvents)
    {
      foreach (var item in plan.Deletes.Where(item => !currentGoogleIds.Contains(item.GoogleEventId)))
      {
        if (!managedResources.TryGetValue(item.CalDavUrl, out var resource) ||
            resource.CalendarData is null ||
            !CalDavUtilities.IsManagedEvent(resource.CalendarData))
        {
          logger.LogWarning("Salto eliminazione non verificata per GoogleId={GoogleEventId}", item.GoogleEventId);
          continue;
        }

        var result = await calDavClient.DeleteEventAsync(new Uri(item.CalDavUrl), resource.ETag ?? item.ETag, password, cancellationToken);
        Console.WriteLine($"DELETE {item.GoogleEventId}: HTTP {(int)result.StatusCode} {result.StatusCode}");
        if (result.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
          await stateStore.MarkDeletedAsync(item with { ETag = resource.ETag ?? item.ETag }, cancellationToken);
          deleted++;
        }
        else if (result.StatusCode == HttpStatusCode.PreconditionFailed)
        {
          conflicts++;
        }
      }
    }

    return new SyncResult(created, updated, deleted, plan.Unchanged.Count, conflicts);
  }

  private SyncStateEntry ToStateEntry(GoogleEventPlan item, string? etag)
  {
    return new SyncStateEntry(
      configuration.Google.CalendarId,
      item.GoogleEvent.Id,
      item.GoogleEvent.RecurringEventId,
      item.Uid,
      item.CalDavUrl.AbsoluteUri,
      etag,
      item.GoogleEvent.Updated,
      item.Hash,
      DateTimeOffset.UtcNow,
      false);
  }
}

internal sealed record GoogleEventPlan(
  GoogleCalendarEvent GoogleEvent,
  string Uid,
  Uri CalDavUrl,
  string ICalendar,
  string Hash);

internal sealed record SyncPlan(
  IReadOnlyList<GoogleEventPlan> Creates,
  IReadOnlyList<GoogleEventPlan> Recreates,
  IReadOnlyList<GoogleEventPlan> Updates,
  IReadOnlyList<SyncStateEntry> Deletes,
  IReadOnlyList<GoogleEventPlan> Unchanged);
