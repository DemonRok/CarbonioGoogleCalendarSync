namespace CarbonioGoogleCalendarSync;

public static class ExitCodes
{
  public const int Success = 0;
  public const int GenericError = 1;
  public const int ConfigurationInvalid = 2;
  public const int AuthenticationFailed = 3;
  public const int CalendarNotFound = 4;
  public const int AlreadyRunning = 5;
  public const int CompletedWithConflicts = 6;
  public const int TemporaryNetworkError = 7;
}
