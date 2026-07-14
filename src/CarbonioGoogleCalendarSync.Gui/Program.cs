using System.Runtime.InteropServices;

namespace CarbonioGoogleCalendarSync.Gui;

internal static class Program
{
    private const string MutexName = @"Local\CarbonioGoogleCalendarSync.Gui";
    internal static readonly int ShowExistingWindowMessage = RegisterWindowMessage("CarbonioGoogleCalendarSync.ShowExistingWindow");
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            PostMessage(HwndBroadcast, ShowExistingWindowMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }

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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
