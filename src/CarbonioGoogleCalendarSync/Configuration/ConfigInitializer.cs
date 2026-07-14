using System.Text.Json;
using System.Text.Json.Serialization;
using CarbonioGoogleCalendarSync.Security;

namespace CarbonioGoogleCalendarSync.Configuration;

public sealed class ConfigInitializer(ICredentialStore credentialStore)
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
    var calendarName = ReadRequired("Calendario Carbonio di destinazione", "Google");
    var googleCalendarId = ReadRequired("Nome/ID logico calendario Google", "primary");
    var googleIcsUrl = ReadRequired("URL privato ICS Google", "https://calendar.google.com/calendar/ical/.../basic.ics");

    var config = new
    {
      Carbonio = new
      {
        BaseUrl = carbonioBaseUrl,
        Username = username
      },
      Google = new
      {
        Calendars = new[]
        {
          new
          {
            Id = googleCalendarId,
            IcsUrl = googleIcsUrl,
            TitlePrefix = "(G)",
            CarbonioCalendarName = calendarName
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
        ImportedTitlePrefix = "(G)"
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
    await credentialStore.SaveGoogleIcsUrlAsync(username, googleCalendarId, googleIcsUrl, cancellationToken);
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
    var calendarId = mutable.Google.Calendars.Count > 0
      ? mutable.Google.Calendars[0].Id
      : mutable.Google.CalendarId ?? "primary";
    if (mutable.Google.Calendars.Count > 0)
    {
      mutable.Google.Calendars[0] = mutable.Google.Calendars[0] with { IcsUrl = null };
    }
    else
    {
      mutable.Google.Calendars.Add(new GoogleCalendarFileModel(
        calendarId,
        null,
        "(G)",
        mutable.Carbonio?.CalendarName));
    }

    if (string.IsNullOrWhiteSpace(mutable.Carbonio?.Username))
    {
      throw new ConfigurationException("Carbonio:Username mancante.");
    }

    await credentialStore.SaveGoogleIcsUrlAsync(mutable.Carbonio.Username, calendarId, icsUrl, cancellationToken);
    mutable.Google.CalendarId = null;
    mutable.Google.IcsUrl = null;

    var options = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(target, JsonSerializer.Serialize(mutable, options), cancellationToken);
    Console.WriteLine("URL ICS Google salvato nello store protetto dell'utente Windows.");
  }

  public async Task NormalizeExistingConfigAsync(CancellationToken cancellationToken)
  {
    ConfigPaths.MigrateLegacyConfigIfNeeded();
    var target = ConfigPaths.ConfigPath;
    if (!File.Exists(target))
    {
      return;
    }

    var json = await File.ReadAllTextAsync(target, cancellationToken);
    var mutable = JsonSerializer.Deserialize<ConfigFileModel>(json) ?? new ConfigFileModel();
    if (mutable.Google is null ||
        string.IsNullOrWhiteSpace(mutable.Carbonio?.Username))
    {
      return;
    }

    var changed = false;
    if (mutable.Google.Calendars.Count == 0 && !string.IsNullOrWhiteSpace(mutable.Google.IcsUrl))
    {
      mutable.Google.Calendars.Add(new GoogleCalendarFileModel(
        mutable.Google.CalendarId ?? "primary",
        mutable.Google.IcsUrl,
        mutable.Sync?.ImportedTitlePrefix ?? "(G)",
        mutable.Carbonio.CalendarName ?? "Google"));
      changed = true;
    }

    if (mutable.Google.Calendars.Count > 0 && !string.IsNullOrWhiteSpace(mutable.Carbonio.CalendarName))
    {
      for (var index = 0; index < mutable.Google.Calendars.Count; index++)
      {
        var calendar = mutable.Google.Calendars[index];
        if (string.IsNullOrWhiteSpace(calendar.CarbonioCalendarName))
        {
          mutable.Google.Calendars[index] = calendar with { CarbonioCalendarName = mutable.Carbonio.CalendarName };
          changed = true;
        }
      }
    }

    for (var index = 0; index < mutable.Google.Calendars.Count; index++)
    {
      var calendar = mutable.Google.Calendars[index];
      if (string.IsNullOrWhiteSpace(calendar.IcsUrl))
      {
        continue;
      }

      await credentialStore.SaveGoogleIcsUrlAsync(mutable.Carbonio.Username, calendar.Id, calendar.IcsUrl, cancellationToken);
      mutable.Google.Calendars[index] = calendar with { IcsUrl = null };
      changed = true;
    }

    if (!string.IsNullOrWhiteSpace(mutable.Google.CalendarId) ||
        !string.IsNullOrWhiteSpace(mutable.Google.IcsUrl))
    {
      mutable.Google.CalendarId = null;
      mutable.Google.IcsUrl = null;
      changed = true;
    }

    if (!string.IsNullOrWhiteSpace(mutable.Carbonio.CalendarName) ||
        !string.IsNullOrWhiteSpace(mutable.Carbonio.CalendarUrl))
    {
      mutable.Carbonio.CalendarName = null;
      mutable.Carbonio.CalendarUrl = null;
      changed = true;
    }

    if (changed)
    {
      var options = new JsonSerializerOptions
      {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
      };
      await File.WriteAllTextAsync(target, JsonSerializer.Serialize(mutable, options), cancellationToken);
      Console.WriteLine("Configurazione Google migrata: URL ICS salvati nello store protetto dell'utente Windows.");
    }
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
    public string? CalendarName { get; set; }
    public string? CalendarUrl { get; set; }
  }

  private sealed record GoogleFileModel
  {
    public string? CalendarId { get; set; }
    public string? IcsUrl { get; set; }
    public List<GoogleCalendarFileModel> Calendars { get; init; } = [];
  }

  private sealed record GoogleCalendarFileModel(
    string Id,
    string? IcsUrl,
    string? TitlePrefix,
    string? CarbonioCalendarName = null,
    string? CarbonioCalendarUrl = null);

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
