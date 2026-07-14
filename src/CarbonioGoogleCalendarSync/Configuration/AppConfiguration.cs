using Microsoft.Extensions.Configuration;

namespace CarbonioGoogleCalendarSync.Configuration;

public sealed record AppConfiguration(
  CarbonioConfiguration Carbonio,
  GoogleConfiguration Google,
  SyncConfiguration Sync,
  LoggingConfiguration Logging,
  HttpConfiguration Http)
{
  public static AppConfiguration Load(IConfiguration configuration, bool validate = true)
  {
    var appConfig = new AppConfiguration(
      NormalizeCarbonio(configuration.GetSection("Carbonio").Get<CarbonioConfiguration>() ?? new CarbonioConfiguration()),
      configuration.GetSection("Google").Get<GoogleConfiguration>() ?? new GoogleConfiguration(),
      configuration.GetSection("Sync").Get<SyncConfiguration>() ?? new SyncConfiguration(),
      configuration.GetSection("Logging").Get<LoggingConfiguration>() ?? new LoggingConfiguration(),
      configuration.GetSection("Http").Get<HttpConfiguration>() ?? new HttpConfiguration());

    if (validate)
    {
      appConfig.Validate();
    }

    return appConfig;
  }

  private static CarbonioConfiguration NormalizeCarbonio(CarbonioConfiguration carbonio)
  {
    return carbonio with
    {
      CalendarUrl = NormalizeCalDavUrl(carbonio.CalendarUrl)
    };
  }

  private static string NormalizeCalDavUrl(string value)
  {
    if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
    {
      return value;
    }

    var builder = new UriBuilder(uri)
    {
      Path = string.Concat(uri.Segments.Select(segment => segment.Replace("@", "%40", StringComparison.Ordinal))).TrimStart('/')
    };
    return builder.Uri.AbsoluteUri;
  }

  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(Carbonio.Username))
    {
      throw new ConfigurationException("Carbonio:Username mancante.");
    }

    if (!Uri.TryCreate(Carbonio.CalendarUrl, UriKind.Absolute, out var calendarUri) ||
        calendarUri.Scheme != Uri.UriSchemeHttps)
    {
      throw new ConfigurationException("Carbonio:CalendarUrl deve essere un URL HTTPS assoluto.");
    }

    if (string.IsNullOrWhiteSpace(Carbonio.CalendarName))
    {
      throw new ConfigurationException("Carbonio:CalendarName mancante.");
    }

    if (!Carbonio.AllowNonGoogleCalendar && !Carbonio.CalendarName.Equals("Google", StringComparison.Ordinal))
    {
      throw new ConfigurationException("Blocco di sicurezza: il calendario Carbonio di destinazione deve chiamarsi 'Google'.");
    }

    if (Http.TimeoutSeconds <= 0)
    {
      throw new ConfigurationException("Http:TimeoutSeconds deve essere maggiore di zero.");
    }

    if (Sync.Direction != "GoogleToCarbonio")
    {
      throw new ConfigurationException("Sync:Direction deve essere GoogleToCarbonio.");
    }

    var googleCalendars = Google.GetCalendars();
    if (googleCalendars.Count == 0)
    {
      throw new ConfigurationException("Google: almeno un calendario ICS deve essere configurato.");
    }

    foreach (var calendar in googleCalendars)
    {
      if (string.IsNullOrWhiteSpace(calendar.Id))
      {
        throw new ConfigurationException("Google:Calendars:Id mancante.");
      }

      if (!Uri.TryCreate(calendar.IcsUrl, UriKind.Absolute, out var icsUri) ||
          icsUri.Scheme != Uri.UriSchemeHttps)
      {
        throw new ConfigurationException($"Google:Calendars:{calendar.Id}:IcsUrl deve essere un URL HTTPS assoluto.");
      }
    }
  }
}

public sealed record CarbonioConfiguration
{
  public string BaseUrl { get; init; } = "";
  public string Username { get; init; } = "";
  public string CalendarName { get; init; } = "Google";
  public string CalendarUrl { get; init; } = "";
  public bool AllowNonGoogleCalendar { get; init; }
}

public sealed record GoogleConfiguration
{
  public string CalendarId { get; init; } = "primary";
  public string IcsUrl { get; init; } = "";
  public IReadOnlyList<GoogleCalendarConfiguration> Calendars { get; init; } = [];

  public IReadOnlyList<GoogleCalendarConfiguration> GetCalendars()
  {
    if (Calendars.Count > 0)
    {
      return Calendars
        .Where(calendar => !string.IsNullOrWhiteSpace(calendar.IcsUrl))
        .Select(calendar => calendar with
        {
          Id = string.IsNullOrWhiteSpace(calendar.Id) ? CalendarId : calendar.Id
        })
        .ToList();
    }

    return string.IsNullOrWhiteSpace(IcsUrl)
      ? []
      : [new GoogleCalendarConfiguration(CalendarId, IcsUrl, null, UseLegacyUid: true)];
  }
}

public sealed record GoogleCalendarConfiguration(
  string Id,
  string IcsUrl,
  string? TitlePrefix = null,
  bool UseLegacyUid = false);

public sealed record SyncConfiguration
{
  public string Direction { get; init; } = "GoogleToCarbonio";
  public int PastDays { get; init; } = 30;
  public int FutureDays { get; init; } = 365;
  public bool DeleteRemovedEvents { get; init; } = true;
  public bool DryRun { get; init; }
  public string StateDatabasePath { get; init; } = "state/sync-state.db";
  public string ImportedTitlePrefix { get; init; } = "(G) ";
}

public sealed record LoggingConfiguration
{
  public string Directory { get; init; } = "logs";
  public string MinimumLevel { get; init; } = "Information";
  public int RetentionDays { get; init; } = 30;
}

public sealed record HttpConfiguration
{
  public int TimeoutSeconds { get; init; } = 60;
}

public sealed class ConfigurationException(string message) : Exception(message);
