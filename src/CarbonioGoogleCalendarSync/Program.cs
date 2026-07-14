using CarbonioGoogleCalendarSync.Carbonio;
using CarbonioGoogleCalendarSync.Configuration;
using CarbonioGoogleCalendarSync.Google;
using CarbonioGoogleCalendarSync.Security;
using CarbonioGoogleCalendarSync.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace CarbonioGoogleCalendarSync;

public static class Program
{
  public static async Task<int> Main(string[] args)
  {
    var configuration = BuildConfiguration();
    var skipValidation = args.Length >= 2 &&
      args[0].Equals("config", StringComparison.OrdinalIgnoreCase) &&
      args[1].Equals("init", StringComparison.OrdinalIgnoreCase);
    var appConfig = AppConfiguration.Load(configuration, validate: !skipValidation);
    ConfigureSerilog(appConfig.Logging);

    try
    {
      using var singleInstance = new Semaphore(1, 1, @"Local\CarbonioGoogleCalendarSync");
      if (!singleInstance.WaitOne(TimeSpan.Zero))
      {
        Console.Error.WriteLine("Una sincronizzazione e' gia' in corso.");
        return ExitCodes.AlreadyRunning;
      }

      try
      {
        using var host = Host.CreateDefaultBuilder(args)
          .UseSerilog()
          .ConfigureServices(services =>
          {
            services.AddSingleton(appConfig);
            services.AddSingleton<IConsolePasswordReader, ConsolePasswordReader>();
            services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
            services.AddSingleton<ConfigInitializer>();
            services.AddSingleton<CalDavResponseParser>();
            services.AddSingleton<CalDavTestService>();
            services.AddSingleton<GoogleToCalDavConverter>();
            services.AddSingleton<SyncStateStore>();
            services.AddSingleton<CalendarSyncService>();
            services.AddHttpClient<GoogleCalendarClient>(client =>
            {
              client.Timeout = TimeSpan.FromSeconds(appConfig.Http.TimeoutSeconds);
              client.DefaultRequestHeaders.UserAgent.ParseAdd("CarbonioGoogleCalendarSync/0.1");
            });
            services.AddHttpClient<CalDavClient>(client =>
            {
              client.Timeout = TimeSpan.FromSeconds(appConfig.Http.TimeoutSeconds);
              client.DefaultRequestHeaders.UserAgent.ParseAdd("CarbonioGoogleCalendarSync/0.1");
            });
          })
          .Build();

        return await RunAsync(host.Services, args, CancellationToken.None);
      }
      finally
      {
        singleInstance.Release();
      }
    }
    catch (ConfigurationException ex)
    {
      Console.Error.WriteLine(ex.Message);
      return ExitCodes.ConfigurationInvalid;
    }
    catch (HttpRequestException ex)
    {
      Log.Error(ex, "Errore HTTP temporaneo");
      return ExitCodes.TemporaryNetworkError;
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Errore non gestito");
      return ExitCodes.GenericError;
    }
    finally
    {
      await Log.CloseAndFlushAsync();
    }
  }

  private static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken)
  {
    if (args.Length == 0)
    {
      PrintUsage();
      return ExitCodes.ConfigurationInvalid;
    }

    var command = args[0].ToLowerInvariant();
    if (!(command == "config" &&
          args.Skip(1).FirstOrDefault()?.Equals("init", StringComparison.OrdinalIgnoreCase) == true))
    {
      await services.GetRequiredService<ConfigInitializer>().NormalizeExistingConfigAsync(cancellationToken);
    }

    return command switch
    {
      "carbonio-test" => await services.GetRequiredService<CalDavTestService>()
        .RunAsync(args.Contains("--keep-test-event", StringComparer.OrdinalIgnoreCase), cancellationToken),
      "config" when args.Skip(1).FirstOrDefault()?.Equals("validate", StringComparison.OrdinalIgnoreCase) == true
        => ValidateConfig(services.GetRequiredService<AppConfiguration>()),
      "config" when args.Skip(1).FirstOrDefault()?.Equals("init", StringComparison.OrdinalIgnoreCase) == true
        => await InitConfigAsync(services, cancellationToken),
      "config" when args.Skip(1).FirstOrDefault()?.Equals("set-google-ics", StringComparison.OrdinalIgnoreCase) == true
        => await SetGoogleIcsAsync(services, cancellationToken),
      "connection-test" => await services.GetRequiredService<CalendarSyncService>()
        .ValidateConnectionsAsync(cancellationToken),
      "credentials" => await RunCredentialsAsync(services, args, cancellationToken),
      "sync" => await services.GetRequiredService<CalendarSyncService>()
        .RunAsync(args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase), cancellationToken),
      "purge-google" => await services.GetRequiredService<CalendarSyncService>()
        .PurgeImportedAsync(cancellationToken),
      _ => UnknownCommand(command)
    };
  }

  private static async Task<int> RunCredentialsAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken)
  {
    var action = args.Skip(1).FirstOrDefault()?.ToLowerInvariant();
    var config = services.GetRequiredService<AppConfiguration>();
    var store = services.GetRequiredService<ICredentialStore>();
    var reader = services.GetRequiredService<IConsolePasswordReader>();

    if (action == "set-carbonio")
    {
      var password = reader.ReadPassword("Password Carbonio: ");
      await store.SaveCarbonioPasswordAsync(config.Carbonio.Username, password, cancellationToken);
      Console.WriteLine("Credenziale Carbonio salvata con DPAPI per l'utente Windows corrente.");
      return ExitCodes.Success;
    }

    if (action == "remove-carbonio")
    {
      await store.RemoveCarbonioPasswordAsync(config.Carbonio.Username, cancellationToken);
      Console.WriteLine("Credenziale Carbonio rimossa.");
      return ExitCodes.Success;
    }

    return UnknownCommand("credentials");
  }

  private static int ValidateConfig(AppConfiguration config)
  {
    config.Validate();
    Console.WriteLine("Configurazione valida.");
    return ExitCodes.Success;
  }

  private static async Task<int> InitConfigAsync(IServiceProvider services, CancellationToken cancellationToken)
  {
    await services.GetRequiredService<ConfigInitializer>().CreateInteractiveAsync(cancellationToken);
    return ExitCodes.Success;
  }

  private static async Task<int> SetGoogleIcsAsync(IServiceProvider services, CancellationToken cancellationToken)
  {
    await services.GetRequiredService<ConfigInitializer>().SetGoogleIcsUrlAsync(cancellationToken);
    return ExitCodes.Success;
  }

  private static int NotImplemented(string message)
  {
    Console.WriteLine(message);
    return ExitCodes.Success;
  }

  private static int UnknownCommand(string command)
  {
    Console.Error.WriteLine($"Comando non riconosciuto: {command}");
    PrintUsage();
    return ExitCodes.ConfigurationInvalid;
  }

  private static IConfigurationRoot BuildConfiguration()
  {
    ConfigPaths.MigrateLegacyConfigIfNeeded();

    return new ConfigurationBuilder()
      .SetBasePath(AppContext.BaseDirectory)
      .AddJsonFile(ConfigPaths.ConfigPath, optional: true, reloadOnChange: false)
      .AddEnvironmentVariables("CGCS_")
      .Build();
  }

  private static void ConfigureSerilog(LoggingConfiguration config)
  {
    var level = Enum.TryParse<LogEventLevel>(config.MinimumLevel, true, out var parsed)
      ? parsed
      : LogEventLevel.Information;

    var logDirectory = ResolveLogDirectory(config.Directory);
    Directory.CreateDirectory(logDirectory);
    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Is(level)
      .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
      .Enrich.WithProperty("MachineName", Environment.MachineName)
      .Enrich.WithProperty("UserName", Environment.UserName)
      .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{MachineName}\\{UserName}] {Message:lj}{NewLine}{Exception}")
      .WriteTo.File(Path.Combine(logDirectory, "carbonio-google-calendar-sync-.log"),
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{MachineName}\\{UserName}] {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: config.RetentionDays)
      .CreateLogger();
  }

  private static string ResolveLogDirectory(string directory)
  {
    if (Path.IsPathRooted(directory))
    {
      return directory;
    }

    return Path.Combine(AppContext.BaseDirectory, directory);
  }

  private static void PrintUsage()
  {
    Console.WriteLine("Uso:");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe carbonio-test [--keep-test-event]");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe connection-test");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe credentials set-carbonio");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe credentials remove-carbonio");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe config init");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe config set-google-ics");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe config validate");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe sync [--dry-run]");
    Console.WriteLine("  CarbonioGoogleCalendarSync.exe purge-google");
  }
}
