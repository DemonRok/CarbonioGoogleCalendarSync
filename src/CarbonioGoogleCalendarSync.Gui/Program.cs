namespace CarbonioGoogleCalendarSync.Gui;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        try
        {
            Application.ThreadException += (_, e) => LogAndShow(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogAndShow(ex);
                }
            };

            var publishedEnginePath = Path.Combine(AppContext.BaseDirectory, "CarbonioGoogleCalendarSync.exe");
            if (File.Exists(publishedEnginePath))
            {
                Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            LogAndShow(ex);
        }
    }

    private static void LogAndShow(Exception ex)
    {
        var message =
            $"Time: {DateTime.Now:O}{Environment.NewLine}" +
            $"Machine: {Environment.MachineName}{Environment.NewLine}" +
            $"User: {Environment.UserName}{Environment.NewLine}" +
            ex + Environment.NewLine;
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "gui-error.log"), message + Environment.NewLine);
        MessageBox.Show(ex.ToString(), "GUI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
