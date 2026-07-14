using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarbonioGoogleCalendarSync.Configuration;

public sealed class ConfigInitializer
{
  public async Task CreateInteractiveAsync(CancellationToken cancellationToken)
  {
    ConfigPaths.MigrateLegacyConfigIfNeeded();
    var target = ConfigPaths.ConfigPath;
    if (File.Exists(target))
    {
      Console.Write("config.json esiste gia'. Sovrascrivere? [s/N] ");
      var overwrite = Console.ReadLine();
      if (!string.Equals(overwrite, "s", StringComparison.OrdinalIgnoreCase))
      {
        Console.WriteLine("Operazione annullata.");
        return;
      }
    }

    var carbonioBaseUrl = ReadRequired("URL base Carbonio", "https://webmail.example.local");
    var username = ReadRequired("Utente Carbonio", "user.name@example.local");
    var calendarName = ReadRequired("Nome calendario Carbonio", "Google");
    var calendarUrl = ReadRequired("URL CalDAV calendario Carbonio", BuildDefaultCalendarUrl(carbonioBaseUrl, username, calendarName));
    var googleCalendarId = ReadRequired("Nome/ID logico calendario Google", "primary");
    var googleIcsUrl = ReadRequired("URL privato ICS Google", "https://calendar.google.com/calendar/ical/.../basic.ics");

    var config = new
    {
      Carbonio = new
      {
        BaseUrl = carbonioBaseUrl,
        Username = username,
        CalendarName = calendarName,
        CalendarUrl = calendarUrl,
        AllowNonGoogleCalendar = false
      },
      Google = new
      {
        CalendarId = googleCalendarId,
        IcsUrl = googleIcsUrl,
        Calendars = new[]
        {
          new
          {
            Id = googleCalendarId,
            IcsUrl = googleIcsUrl,
            TitlePrefix = "(G) ",
            UseLegacyUid = true
          }
        }
      },
      Sync = new
      {
        Direction = "GoogleToCarbonio",
        PastDays = 30,
        FutureDays = 365,
        DeleteRemovedEvents = true,
        DryRun = false,
        StateDatabasePath = "state/sync-state.db",
        ImportedTitlePrefix = "(G) "
      },
      Logging = new
      {
        Directory = "logs",
        MinimumLevel = "Information",
        RetentionDays = 30
      },
      Http = new
      {
        TimeoutSeconds = 60
      }
    };

    var options = new JsonSerializerOptions
    {
      WriteIndented = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    await File.WriteAllTextAsync(target, JsonSerializer.Serialize(config, options), cancellationToken);
    Console.WriteLine($"Creato {target}");
  }

  public async Task SetGoogleIcsUrlAsync(CancellationToken cancellationToken)
  {
    ConfigPaths.MigrateLegacyConfigIfNeeded();
    var target = ConfigPaths.ConfigPath;
    if (!File.Exists(target))
    {
      throw new ConfigurationException("config.json non trovato. Esegui prima: config init");
    }

    var json = await File.ReadAllTextAsync(target, cancellationToken);
    var mutable = JsonSerializer.Deserialize<ConfigFileModel>(json) ??
      throw new ConfigurationException("config.json non valido.");

    var icsUrl = ReadRequired("URL segreto ICS Google", "https://calendar.google.com/calendar/ical/.../basic.ics");
    if (!Uri.TryCreate(icsUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
    {
      throw new ConfigurationException("L'URL ICS Google deve essere un URL HTTPS assoluto.");
    }

    mutable.Google ??= new GoogleFileModel();
    mutable.Google.IcsUrl = icsUrl;
    if (mutable.Google.Calendars.Count > 0)
    {
      mutable.Google.Calendars[0] = mutable.Google.Calendars[0] with { IcsUrl = icsUrl };
    }
    else
    {
      mutable.Google.Calendars.Add(new GoogleCalendarFileModel(mutable.Google.CalendarId ?? "primary", icsUrl, "(G) ", true));
    }

    var options = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(target, JsonSerializer.Serialize(mutable, options), cancellationToken);
    Console.WriteLine("URL ICS Google salvato in config.json.");
  }

  private static string ReadRequired(string label, string defaultValue)
  {
    while (true)
    {
      Console.Write($"{label} [{defaultValue}]: ");
      var value = Console.ReadLine();
      if (string.IsNullOrWhiteSpace(value))
      {
        return defaultValue;
      }

      return value.Trim();
    }
  }

  private static string BuildDefaultCalendarUrl(string baseUrl, string username, string calendarName)
  {
    var cleanBaseUrl = baseUrl.TrimEnd('/');
    return $"{cleanBaseUrl}/dav/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(calendarName)}/";
  }

  private sealed record ConfigFileModel
  {
    public CarbonioFileModel? Carbonio { get; init; }
    public GoogleFileModel? Google { get; set; }
    public SyncFileModel? Sync { get; init; }
    public LoggingFileModel? Logging { get; init; }
    public HttpFileModel? Http { get; init; }
  }

  private sealed record CarbonioFileModel
  {
    public string? BaseUrl { get; init; }
    public string? Username { get; init; }
    public string? CalendarName { get; init; }
    public string? CalendarUrl { get; init; }
    public bool AllowNonGoogleCalendar { get; init; }
  }

  private sealed record GoogleFileModel
  {
    public string? CalendarId { get; init; }
    public string? IcsUrl { get; set; }
    public List<GoogleCalendarFileModel> Calendars { get; init; } = [];
  }

  private sealed record GoogleCalendarFileModel(string Id, string IcsUrl, string? TitlePrefix, bool UseLegacyUid = false);

  private sealed record SyncFileModel
  {
    public string? Direction { get; init; }
    public int PastDays { get; init; }
    public int FutureDays { get; init; }
    public bool DeleteRemovedEvents { get; init; }
    public bool DryRun { get; init; }
    public string? StateDatabasePath { get; init; }
    public string? ImportedTitlePrefix { get; init; }
  }

  private sealed record LoggingFileModel
  {
    public string? Directory { get; init; }
    public string? MinimumLevel { get; init; }
    public int RetentionDays { get; init; }
  }

  private sealed record HttpFileModel
  {
    public int TimeoutSeconds { get; init; }
  }
}
