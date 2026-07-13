namespace CarbonioGoogleCalendarSync.Configuration;

public static class ConfigPaths
{
  public static string AppDataDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "CarbonioGoogleCalendarSync");

  public static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");

  public static void MigrateLegacyConfigIfNeeded()
  {
    if (File.Exists(ConfigPath))
    {
      return;
    }

    var legacyCandidates = new[]
    {
      Path.Combine(Directory.GetCurrentDirectory(), "config.json"),
      Path.Combine(AppContext.BaseDirectory, "config.json")
    }
    .Select(Path.GetFullPath)
    .Distinct(StringComparer.OrdinalIgnoreCase);

    var source = legacyCandidates.FirstOrDefault(File.Exists);
    if (source is null)
    {
      return;
    }

    Directory.CreateDirectory(AppDataDirectory);
    File.Copy(source, ConfigPath);
  }
}
