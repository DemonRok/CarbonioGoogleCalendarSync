using System.Net;
using CarbonioGoogleCalendarSync.Security;
using Microsoft.Extensions.Logging;

namespace CarbonioGoogleCalendarSync.Carbonio;

public sealed class CalDavTestService(
  CalDavClient client,
  ICredentialStore credentialStore,
  IConsolePasswordReader passwordReader,
  Configuration.AppConfiguration configuration,
  ILogger<CalDavTestService> logger)
{
  public async Task<int> RunAsync(bool keepTestEvent, CancellationToken cancellationToken)
  {
    var password = await credentialStore.GetCarbonioPasswordAsync(configuration.Carbonio.Username, cancellationToken)
      ?? passwordReader.ReadPassword("Password Carbonio: ");
    var uid = CalDavUtilities.GenerateTestUid();
    var eventUri = client.BuildEventUri(uid);
    string? etag = null;

    logger.LogInformation("Avvio test CalDAV Carbonio");

    var propFind = await client.PropFindCalendarAsync(password, cancellationToken);
    PrintStatus("PROPFIND calendario", propFind);
    if (propFind.StatusCode is HttpStatusCode.Unauthorized)
    {
      return ExitCodes.AuthenticationFailed;
    }

    if (propFind.StatusCode != (HttpStatusCode)207 || propFind.Body is null || !client.IsCalendarResource(propFind.Body))
    {
      return ExitCodes.CalendarNotFound;
    }

    var startsAt = DateTimeOffset.Now.AddMinutes(10);
    var testEvent = CreateTestEvent(uid, startsAt, "Test CarbonioGoogleCalendarSync");
    var ics = ICalendarBuilder.Build(testEvent, DateTimeOffset.UtcNow);

    var put = await client.PutEventAsync(eventUri, ics, password, "*", cancellationToken);
    PrintStatus("PUT creazione evento", put);
    if (put.StatusCode != HttpStatusCode.Created && put.StatusCode != HttpStatusCode.NoContent)
    {
      return ExitCodes.GenericError;
    }

    etag = put.ETag;
    var report = await client.ReportEventAsync(uid, password, cancellationToken);
    PrintStatus("REPORT rilettura evento", report);
    if (report.StatusCode != (HttpStatusCode)207 || report.Body is null)
    {
      return ExitCodes.GenericError;
    }

    var resources = client.ParseReport(report.Body);
    var resource = resources.FirstOrDefault(item => item.CalendarData?.Contains(uid, StringComparison.Ordinal) == true);
    etag = resource?.ETag ?? etag;

    var updatedEvent = CreateTestEvent(uid, startsAt, "Test CarbonioGoogleCalendarSync aggiornato");
    var updatedIcs = ICalendarBuilder.Build(updatedEvent, DateTimeOffset.UtcNow);
    var update = await client.PutEventAsync(eventUri, updatedIcs, password, etag ?? "*", cancellationToken);
    PrintStatus("PUT aggiornamento evento", update);
    if (update.StatusCode == HttpStatusCode.PreconditionFailed)
    {
      return ExitCodes.CompletedWithConflicts;
    }

    if (update.StatusCode != HttpStatusCode.Created && update.StatusCode != HttpStatusCode.NoContent)
    {
      return ExitCodes.GenericError;
    }

    etag = update.ETag ?? etag;
    var get = await client.GetEventAsync(eventUri, password, cancellationToken);
    PrintStatus("GET verifica finale", get);

    if (!keepTestEvent)
    {
      var delete = await client.DeleteEventAsync(eventUri, etag, password, cancellationToken);
      PrintStatus("DELETE rimozione evento", delete);
      return delete.StatusCode == HttpStatusCode.NoContent || delete.StatusCode == HttpStatusCode.NotFound
        ? ExitCodes.Success
        : ExitCodes.GenericError;
    }

    Console.WriteLine($"Evento di test mantenuto: {eventUri}");
    return ExitCodes.Success;
  }

  private static CalDavEvent CreateTestEvent(string uid, DateTimeOffset startsAt, string summary)
  {
    return new CalDavEvent(
      uid,
      summary,
      "Evento creato automaticamente per verificare CalDAV",
      startsAt,
      startsAt.AddMinutes(30),
      "Europe/Rome",
      new Dictionary<string, string>
      {
        ["X-CARBONIO-GOOGLE-SYNC"] = "TRUE"
      });
  }

  private static void PrintStatus(string operation, CalDavOperationResult result)
  {
    Console.WriteLine($"{operation}: HTTP {(int)result.StatusCode} {result.StatusCode}");
  }
}
