using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace CarbonioGoogleCalendarSync.Security;

public interface ICredentialStore
{
  Task SaveCarbonioPasswordAsync(string username, string password, CancellationToken cancellationToken);
  Task<string?> GetCarbonioPasswordAsync(string username, CancellationToken cancellationToken);
  Task RemoveCarbonioPasswordAsync(string username, CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
  private const string Purpose = "CarbonioGoogleCalendarSync.CarbonioPassword";

  public async Task SaveCarbonioPasswordAsync(string username, string password, CancellationToken cancellationToken)
  {
    var path = GetCredentialPath(username);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var protectedBytes = ProtectedData.Protect(
      Encoding.UTF8.GetBytes(password),
      Encoding.UTF8.GetBytes(Purpose),
      DataProtectionScope.CurrentUser);
    await File.WriteAllBytesAsync(path, protectedBytes, cancellationToken);
  }

  public async Task<string?> GetCarbonioPasswordAsync(string username, CancellationToken cancellationToken)
  {
    var path = GetCredentialPath(username);
    if (!File.Exists(path))
    {
      return null;
    }

    var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
    var clearBytes = ProtectedData.Unprotect(
      protectedBytes,
      Encoding.UTF8.GetBytes(Purpose),
      DataProtectionScope.CurrentUser);
    return Encoding.UTF8.GetString(clearBytes);
  }

  public Task RemoveCarbonioPasswordAsync(string username, CancellationToken cancellationToken)
  {
    var path = GetCredentialPath(username);
    if (File.Exists(path))
    {
      File.Delete(path);
    }

    return Task.CompletedTask;
  }

  private static string GetCredentialPath(string username)
  {
    var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.ToLowerInvariant())));
    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "CarbonioGoogleCalendarSync",
      "credentials",
      $"{fileName}.bin");
  }
}

public interface IConsolePasswordReader
{
  string ReadPassword(string prompt);
}

public sealed class ConsolePasswordReader : IConsolePasswordReader
{
  public string ReadPassword(string prompt)
  {
    Console.Write(prompt);
    var password = new StringBuilder();

    while (true)
    {
      var key = Console.ReadKey(intercept: true);
      if (key.Key == ConsoleKey.Enter)
      {
        Console.WriteLine();
        return password.ToString();
      }

      if (key.Key == ConsoleKey.Backspace)
      {
        if (password.Length > 0)
        {
          password.Length--;
        }

        continue;
      }

      if (!char.IsControl(key.KeyChar))
      {
        password.Append(key.KeyChar);
      }
    }
  }
}
