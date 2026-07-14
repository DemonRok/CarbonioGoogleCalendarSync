using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarbonioGoogleCalendarSync.Gui;

public sealed class MainForm : Form
{
  private readonly TextBox _carbonioBaseUrl = new();
  private readonly TextBox _carbonioUsername = new();
  private readonly TextBox _carbonioCalendarName = new();
  private readonly TextBox _carbonioCalendarUrl = new();
  private readonly CheckBox _allowNonGoogleCalendar = new();
  private readonly TextBox _googleCalendarId = new();
  private readonly TextBox _googleIcsUrl = new();
  private readonly TextBox _importedTitlePrefix = new();
  private readonly NumericUpDown _pastDays = new();
  private readonly NumericUpDown _futureDays = new();
  private readonly CheckBox _deleteRemoved = new();
  private readonly TextBox _password = new();
  private readonly TextBox _output = new();
  private readonly Button _dryRunButton = new();
  private readonly Button _syncButton = new();
  private readonly Button _taskActionButton = new();
  private readonly TabControl _tabs = new();
  private TabPage? _operationsTab;
  private TabPage? _configurationTab;
  private TabPage? _infoTab;
  private readonly NumericUpDown _taskIntervalMinutes = new();
  private readonly NumericUpDown _taskTimeoutMinutes = new();
  private readonly Label _taskStatusDot = new();
  private readonly Label _taskStatusText = new();
  private bool _passwordPlaceholderActive;
  private bool _googleIcsPlaceholderActive;
  private bool _loadingConfig;

  private static string AppDataDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "CarbonioGoogleCalendarSync");

  private static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");

  public MainForm()
  {
    Text = "Carbonio Google Calendar Sync";
    Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "CarbonioGoogleCalendarSync.ico"));
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(980, 720);
    Font = new Font("Segoe UI", 9F);

    _tabs.Dock = DockStyle.Fill;
    _operationsTab = BuildOperationsTab();
    _configurationTab = BuildConfigurationTab();
    _infoTab = BuildInfoTab();
    _tabs.TabPages.Add(_operationsTab);
    _tabs.TabPages.Add(_configurationTab);
    _tabs.TabPages.Add(_infoTab);
    Controls.Add(_tabs);

    Load += (_, _) => LoadConfig();
    Shown += async (_, _) => await RefreshTaskStatusAsync();
    _password.Enter += (_, _) => ClearPasswordPlaceholder();
    _googleIcsUrl.Enter += (_, _) => ClearGoogleIcsPlaceholder();
    _carbonioBaseUrl.TextChanged += (_, _) => RefreshCalDavUrl();
    _carbonioUsername.TextChanged += (_, _) => RefreshCalDavUrl();
    _carbonioCalendarName.TextChanged += (_, _) => RefreshCalDavUrl();
    _allowNonGoogleCalendar.CheckedChanged += (_, _) => ApplyCalendarMode();
  }

  private TabPage BuildConfigurationTab()
  {
    var page = new TabPage("Configuration");
    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Padding = new Padding(12)
    };
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));

    var form = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 0,
      AutoScroll = true
    };
    form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
    form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    AddTextRow(form, "Carbonio Base URL", _carbonioBaseUrl);
    AddTextRow(form, "Carbonio User", _carbonioUsername);
    AddPasswordRow(form, _password);
    AddCheckRow(form, "Allow non-dedicated calendar", _allowNonGoogleCalendar);
    AddTextRow(form, "Carbonio Calendar", _carbonioCalendarName);
    AddTextRow(form, "Carbonio CalDAV URL", _carbonioCalendarUrl);
    AddTextRow(form, "Google Calendar ID", _googleCalendarId);
    AddTextRow(form, "URL ICS Google", _googleIcsUrl);
    AddTextRow(form, "Imported title prefix", _importedTitlePrefix);
    AddNumberRow(form, "Past days", _pastDays, 0, 3650);
    AddNumberRow(form, "Future days", _futureDays, 1, 3650);
    AddCheckRow(form, "Delete removed", _deleteRemoved);

    var buttons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      Padding = new Padding(0, 10, 0, 0),
      WrapContents = true
    };
    buttons.Controls.Add(MakeButton("Reload", (_, _) => LoadConfig()));
    buttons.Controls.Add(MakeButton("Save Config", (_, _) => SaveConfig()));
    buttons.Controls.Add(MakeButton("Import Config", (_, _) => ImportConfig()));
    buttons.Controls.Add(MakeButton("Export Config", (_, _) => ExportConfig()));
    buttons.Controls.Add(MakeButton("Connection Test", async (_, _) => await RunCommandAsync("carbonio-test")));
    buttons.Controls.Add(MakeButton("Save CalDAV Password", async (_, _) => await SavePasswordAsync()));
    buttons.Controls.Add(MakeButton("Remove CalDAV Password", async (_, _) => await RemovePasswordAsync()));

    var hint = new Label
    {
      Dock = DockStyle.Fill,
      Text = "Secrets stay local: config.json, ICS URL and DPAPI password are not versioned.",
      ForeColor = Color.DimGray,
      TextAlign = ContentAlignment.MiddleLeft
    };

    root.Controls.Add(form, 0, 0);
    root.Controls.Add(hint, 0, 1);
    root.Controls.Add(buttons, 0, 2);
    page.Controls.Add(root);
    return page;
  }

  private TabPage BuildInfoTab()
  {
    var page = new TabPage("Info");
    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Padding = new Padding(18)
    };
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    var title = new Label
    {
      Dock = DockStyle.Fill,
      Text =
        "Carbonio Google Calendar Sync" + Environment.NewLine +
        $"Version {version}" + Environment.NewLine +
        "Google Calendar ICS to Carbonio CalDAV synchronization",
      Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
      TextAlign = ContentAlignment.MiddleLeft
    };

    var details = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 5
    };
    details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
    details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    AddInfoRow(details, "License", "MIT License");
    AddInfoRow(details, "Copyright", "Copyright (c) 2026 Mauro Bettinelli");
    AddInfoRow(details, "Configuration", ConfigPath);
    AddInfoRow(details, "AppData", AppDataDirectory);
    AddInfoRow(details, "Executable", AppContext.BaseDirectory);

    var buttons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = true
    };
    buttons.Controls.Add(MakeButton("Open AppData", (_, _) => OpenAppDataFolder()));
    buttons.Controls.Add(MakeButton("Open Logs", (_, _) => OpenLogsFolder()));
    buttons.Controls.Add(MakeButton("Open Repository", (_, _) => OpenUrl("https://github.com/DemonRok/CarbonioGoogleCalendarSync")));
    buttons.Controls.Add(MakeButton("Report Issue", (_, _) => OpenUrl("https://github.com/DemonRok/CarbonioGoogleCalendarSync/issues/new")));
    buttons.Controls.Add(MakeButton("Open Releases", (_, _) => OpenUrl("https://github.com/DemonRok/CarbonioGoogleCalendarSync/releases")));
    buttons.Controls.Add(MakeButton("Open License", (_, _) => OpenLocalFile(Path.Combine(AppContext.BaseDirectory, "LICENSE"))));

    root.Controls.Add(title, 0, 0);
    root.Controls.Add(details, 0, 1);
    root.Controls.Add(buttons, 0, 2);
    page.Controls.Add(root);
    return page;
  }

  private TabPage BuildOperationsTab()
  {
    var page = new TabPage("Operations");
    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 4,
      Padding = new Padding(8)
    };
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    var primaryButtons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      Margin = new Padding(5, 0, 0, 0)
    };
    _dryRunButton.Text = "Dry-run";
    _dryRunButton.Width = 120;
    _dryRunButton.Height = 34;
    _dryRunButton.Margin = new Padding(0, 0, 8, 0);
    _dryRunButton.Click += async (_, _) => await RunCommandAsync("sync --dry-run");
    _syncButton.Text = "Sync";
    _syncButton.Width = 120;
    _syncButton.Height = 34;
    _syncButton.Margin = new Padding(0, 0, 8, 0);
    _syncButton.Click += async (_, _) => await RunCommandAsync("sync");
    primaryButtons.Controls.Add(_syncButton);
    primaryButtons.Controls.Add(_dryRunButton);

    var secondaryButtons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      Margin = new Padding(5, 0, 0, 0)
    };
    secondaryButtons.Controls.Add(MakeButton("Delete Imported", async (_, _) => await PurgeImportedAsync()));
    secondaryButtons.Controls.Add(MakeButton("Clear Logs", (_, _) => ClearLogs()));
    secondaryButtons.Controls.Add(MakeButton("Open AppData", (_, _) => OpenAppDataFolder()));
    _taskActionButton.Text = "Checking task...";
    _taskActionButton.Width = 150;
    _taskActionButton.Height = 34;
    _taskActionButton.Margin = new Padding(0, 0, 8, 0);
    secondaryButtons.Controls.Add(_taskActionButton);
    secondaryButtons.Controls.Add(BuildTaskStatusPanel());

    var taskPanel = BuildTaskPanel();

    _output.Dock = DockStyle.Fill;
    _output.Multiline = true;
    _output.ScrollBars = ScrollBars.Vertical;
    _output.ReadOnly = true;
    _output.Font = new Font("Consolas", 9F);

    root.Controls.Add(primaryButtons, 0, 0);
    root.Controls.Add(secondaryButtons, 0, 1);
    root.Controls.Add(taskPanel, 0, 2);
    root.Controls.Add(_output, 0, 3);
    page.Controls.Add(root);
    return page;
  }

  private Control BuildTaskPanel()
  {
    var panel = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 3,
      RowCount = 2,
      Margin = new Padding(5, 0, 0, 0)
    };
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
    panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

    _taskIntervalMinutes.Minimum = 15;
    _taskIntervalMinutes.Maximum = 1440;
    _taskIntervalMinutes.Value = 15;
    _taskIntervalMinutes.Width = 100;
    _taskIntervalMinutes.Margin = new Padding(0, 3, 0, 0);

    _taskTimeoutMinutes.Minimum = 1;
    _taskTimeoutMinutes.Maximum = 1440;
    _taskTimeoutMinutes.Value = 10;
    _taskTimeoutMinutes.Width = 100;
    _taskTimeoutMinutes.Margin = new Padding(0, 3, 0, 0);

    panel.Controls.Add(BuildTaskNumberField("Interval minutes", _taskIntervalMinutes), 0, 0);

    var timeoutValue = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      Margin = new Padding(0)
    };
    timeoutValue.Controls.Add(_taskTimeoutMinutes);
    timeoutValue.Controls.Add(new Label { Text = "min", AutoSize = true, Margin = new Padding(6, 7, 0, 0) });
    panel.Controls.Add(BuildTaskNumberField("Task timeout", timeoutValue), 1, 0);

    panel.Controls.Add(new Label
    {
      Text = "The task starts at user login, repeats at the selected interval, and is stopped by Windows after the task timeout.",
      Dock = DockStyle.Fill,
      ForeColor = Color.DimGray,
      TextAlign = ContentAlignment.MiddleLeft
    }, 0, 1);
    panel.SetColumnSpan(panel.Controls[^1], 3);

    return panel;
  }

  private static Control BuildTaskNumberField(string label, Control value)
  {
    var field = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 2,
      Margin = new Padding(0, 0, 18, 0)
    };
    field.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
    field.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    field.Controls.Add(new Label
    {
      Text = label,
      Width = 100,
      TextAlign = ContentAlignment.BottomCenter,
      Margin = new Padding(0)
    }, 0, 0);

    field.Controls.Add(value, 0, 1);
    return field;
  }

  private Control BuildTaskStatusPanel()
  {
    _taskStatusDot.Text = "●";
    _taskStatusDot.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
    _taskStatusDot.ForeColor = Color.Firebrick;
    _taskStatusDot.Dock = DockStyle.Fill;
    _taskStatusDot.TextAlign = ContentAlignment.MiddleCenter;

    _taskStatusText.Text = "Task not checked";
    _taskStatusText.Dock = DockStyle.Fill;
    _taskStatusText.TextAlign = ContentAlignment.MiddleLeft;
    _taskStatusText.AutoEllipsis = false;

    var statusPanel = new TableLayoutPanel
    {
      Width = 220,
      ColumnCount = 2,
      RowCount = 1,
      Height = 34,
      Margin = new Padding(8, 0, 0, 0),
      GrowStyle = TableLayoutPanelGrowStyle.FixedSize
    };
    statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
    statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
    statusPanel.Controls.Add(_taskStatusDot, 0, 0);
    statusPanel.Controls.Add(_taskStatusText, 1, 0);
    return statusPanel;
  }

  private void LoadConfig()
  {
    try
    {
      _loadingConfig = true;
      MigrateLegacyConfigIfNeeded();
      if (!File.Exists(ConfigPath))
      {
        LoadDefaults();
        ApplyCalendarMode();
        if (_configurationTab is not null)
        {
          _tabs.SelectedTab = _configurationTab;
        }

        AppendOutput("config.json not found. Sample defaults loaded: complete the fields and click Save Config.");
        return;
      }

      if (_operationsTab is not null)
      {
        _tabs.SelectedTab = _operationsTab;
      }

      var model = JsonSerializer.Deserialize<ConfigFileModel>(File.ReadAllText(ConfigPath), JsonOptions()) ?? new ConfigFileModel();
      _carbonioBaseUrl.Text = model.Carbonio?.BaseUrl ?? "";
      _carbonioUsername.Text = model.Carbonio?.Username ?? "";
      _carbonioCalendarName.Text = model.Carbonio?.CalendarName ?? "Google";
      _carbonioCalendarUrl.Text = ToDisplayCalDavUrl(model.Carbonio?.CalendarUrl ?? "");
      _allowNonGoogleCalendar.Checked = model.Carbonio?.AllowNonGoogleCalendar ?? false;
      _googleCalendarId.Text = model.Google?.CalendarId ?? "primary";
      SetGoogleIcsPlaceholderIfConfigured(model.Google?.IcsUrl);
      _importedTitlePrefix.Text = model.Sync?.ImportedTitlePrefix ?? "(G) ";
      _pastDays.Value = Clamp(model.Sync?.PastDays ?? 30, _pastDays.Minimum, _pastDays.Maximum);
      _futureDays.Value = Clamp(model.Sync?.FutureDays ?? 365, _futureDays.Minimum, _futureDays.Maximum);
      _deleteRemoved.Checked = model.Sync?.DeleteRemovedEvents ?? true;
      SetPasswordPlaceholderIfCredentialFileExists();
      ApplyCalendarMode();
      AppendOutput("Configuration loaded.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Configuration load error: {ex.Message}");
    }
    finally
    {
      _loadingConfig = false;
      RefreshCalDavUrl();
    }
  }

  private void LoadDefaults()
  {
    _carbonioBaseUrl.Text = "https://webmail.example.local";
    _carbonioUsername.Text = "user.name@example.local";
    _carbonioCalendarName.Text = "Google";
    _allowNonGoogleCalendar.Checked = false;
    _carbonioCalendarUrl.Text = BuildCalDavUrl(_carbonioBaseUrl.Text, _carbonioUsername.Text, _carbonioCalendarName.Text);
    _googleCalendarId.Text = "primary";
    _googleIcsUrl.Text = "https://calendar.google.com/calendar/ical/your-calendar-id/private-token/basic.ics";
    _googleIcsPlaceholderActive = false;
    _importedTitlePrefix.Text = "(G) ";
    _pastDays.Value = 30;
    _futureDays.Value = 365;
    _deleteRemoved.Checked = true;
    _password.Clear();
    _passwordPlaceholderActive = false;
  }

  private void SaveConfig()
  {
    try
    {
      Directory.CreateDirectory(AppDataDirectory);
      var model = File.Exists(ConfigPath)
        ? JsonSerializer.Deserialize<ConfigFileModel>(File.ReadAllText(ConfigPath), JsonOptions()) ?? new ConfigFileModel()
        : new ConfigFileModel();

      model.Carbonio ??= new CarbonioFileModel();
      model.Google ??= new GoogleFileModel();
      model.Sync ??= new SyncFileModel();
      model.Logging ??= new LoggingFileModel();
      model.Http ??= new HttpFileModel();

      model.Carbonio.BaseUrl = _carbonioBaseUrl.Text.Trim();
      model.Carbonio.Username = _carbonioUsername.Text.Trim();
      model.Carbonio.CalendarName = _carbonioCalendarName.Text.Trim();
      model.Carbonio.CalendarUrl = NormalizeCalDavUrl(_carbonioCalendarUrl.Text.Trim());
      model.Carbonio.AllowNonGoogleCalendar = _allowNonGoogleCalendar.Checked;
      model.Google.CalendarId = _googleCalendarId.Text.Trim();
      if (!_googleIcsPlaceholderActive)
      {
        model.Google.IcsUrl = NormalizeUrl(_googleIcsUrl.Text.Trim());
      }
      model.Sync.Direction = "GoogleToCarbonio";
      model.Sync.PastDays = (int)_pastDays.Value;
      model.Sync.FutureDays = (int)_futureDays.Value;
      model.Sync.DeleteRemovedEvents = _deleteRemoved.Checked;
      model.Sync.StateDatabasePath = string.IsNullOrWhiteSpace(model.Sync.StateDatabasePath) ? "state/sync-state.db" : model.Sync.StateDatabasePath;
      model.Sync.ImportedTitlePrefix = _importedTitlePrefix.Text;
      model.Logging.Directory = string.IsNullOrWhiteSpace(model.Logging.Directory) ? "logs" : model.Logging.Directory;
      model.Logging.MinimumLevel = string.IsNullOrWhiteSpace(model.Logging.MinimumLevel) ? "Information" : model.Logging.MinimumLevel;
      model.Logging.RetentionDays = model.Logging.RetentionDays <= 0 ? 30 : model.Logging.RetentionDays;
      model.Http.TimeoutSeconds = model.Http.TimeoutSeconds <= 0 ? 60 : model.Http.TimeoutSeconds;

      File.WriteAllText(ConfigPath, JsonSerializer.Serialize(model, JsonOptions()));
      SetGoogleIcsPlaceholderIfConfigured(model.Google.IcsUrl);
      AppendOutput("Configuration saved.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Configuration save error: {ex.Message}");
    }
  }

  private void ImportConfig()
  {
    using var dialog = new OpenFileDialog
    {
      Title = "Import configuration",
      Filter = "JSON configuration (*.json)|*.json|All files (*.*)|*.*",
      FileName = "config.json"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      var json = File.ReadAllText(dialog.FileName);
      JsonSerializer.Deserialize<ConfigFileModel>(json, JsonOptions());
      Directory.CreateDirectory(AppDataDirectory);
      File.Copy(dialog.FileName, ConfigPath, overwrite: true);
      LoadConfig();
      AppendOutput($"Configuration imported from {dialog.FileName}.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Configuration import error: {ex.Message}");
    }
  }

  private void ExportConfig()
  {
    if (!File.Exists(ConfigPath))
    {
      AppendOutput("No configuration to export.");
      return;
    }

    using var dialog = new SaveFileDialog
    {
      Title = "Export configuration",
      Filter = "JSON configuration (*.json)|*.json|All files (*.*)|*.*",
      FileName = "config.json"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      File.Copy(ConfigPath, dialog.FileName, overwrite: true);
      AppendOutput($"Configuration exported to {dialog.FileName}.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Configuration export error: {ex.Message}");
    }
  }

  private async Task SavePasswordAsync()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(_carbonioUsername.Text))
      {
        AppendOutput("Carbonio user is missing.");
        return;
      }

      if (string.IsNullOrEmpty(_password.Text))
      {
        AppendOutput("Carbonio password is empty.");
        return;
      }

      if (_passwordPlaceholderActive)
      {
        AppendOutput("CalDAV password already exists in the DPAPI store. Enter a new password only if you want to replace it.");
        return;
      }

      var store = new GuiCredentialStore();
      await store.SaveCarbonioPasswordAsync(_carbonioUsername.Text.Trim(), _password.Text, CancellationToken.None);
      SetPasswordPlaceholder();
      AppendOutput("Carbonio CalDAV password saved with DPAPI for the current Windows user.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Password save error: {ex.Message}");
    }
  }

  private async Task RemovePasswordAsync()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(_carbonioUsername.Text))
      {
        AppendOutput("Carbonio user is missing.");
        return;
      }

      var store = new GuiCredentialStore();
      await store.RemoveCarbonioPasswordAsync(_carbonioUsername.Text.Trim(), CancellationToken.None);
      _password.Clear();
      _passwordPlaceholderActive = false;
      AppendOutput("Carbonio CalDAV password removed from the DPAPI store.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Password removal error: {ex.Message}");
    }
  }

  private async Task RunCommandAsync(string arguments)
  {
    SaveConfig();
    await RunProcessAsync(GetEnginePath(), arguments, Directory.GetCurrentDirectory());
  }

  private async Task RunPowerShellScriptAsync(string scriptName)
  {
    var scriptPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "scripts", scriptName));
    if (!File.Exists(scriptPath))
    {
      AppendOutput($"Script not found: {scriptPath}");
      return;
    }

    await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"", Directory.GetCurrentDirectory());
  }

  private async Task InstallScheduledTaskAsync()
  {
    var arguments = string.Join(" ", [
      $"-IntervalMinutes {(int)_taskIntervalMinutes.Value}",
      $"-ExecutionTimeLimitMinutes {(int)_taskTimeoutMinutes.Value}"
    ]);
    await RunPowerShellScriptAsync("Install-ScheduledTask.ps1", arguments);
    await RefreshTaskStatusAsync();
  }

  private async Task RemoveScheduledTaskAsync()
  {
    await RunPowerShellScriptAsync("Remove-ScheduledTask.ps1");
    await RefreshTaskStatusAsync();
  }

  private async Task PurgeImportedAsync()
  {
    var confirm = MessageBox.Show(
      "Delete from Carbonio all Google-imported events marked by the synchronizer?",
      "Confirm imported event deletion",
      MessageBoxButtons.YesNo,
      MessageBoxIcon.Warning);
    if (confirm != DialogResult.Yes)
    {
      return;
    }

    await RunCommandAsync("purge-google");
  }

  private void ClearLogs()
  {
    var confirm = MessageBox.Show(
      "Delete application log files?",
      "Confirm log cleanup",
      MessageBoxButtons.YesNo,
      MessageBoxIcon.Warning);
    if (confirm != DialogResult.Yes)
    {
      return;
    }

    try
    {
      var logsDirectory = GetLogsDirectory();
      if (!Directory.Exists(logsDirectory))
      {
        AppendOutput("Log folder does not exist.");
        return;
      }

      foreach (var file in Directory.EnumerateFiles(logsDirectory, "*.log"))
      {
        File.Delete(file);
      }

      AppendOutput($"Logs removed from {logsDirectory}.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Log cleanup error: {ex.Message}");
    }
  }

  private void OpenAppDataFolder()
  {
    try
    {
      Directory.CreateDirectory(AppDataDirectory);
      Process.Start(new ProcessStartInfo
      {
        FileName = AppDataDirectory,
        UseShellExecute = true
      });
      AppendOutput($"AppData folder opened: {AppDataDirectory}");
    }
    catch (Exception ex)
    {
      AppendOutput($"AppData open error: {ex.Message}");
    }
  }

  private void OpenLogsFolder()
  {
    try
    {
      var logsDirectory = GetLogsDirectory();
      Directory.CreateDirectory(logsDirectory);
      Process.Start(new ProcessStartInfo
      {
        FileName = logsDirectory,
        UseShellExecute = true
      });
      AppendOutput($"Logs folder opened: {logsDirectory}");
    }
    catch (Exception ex)
    {
      AppendOutput($"Logs open error: {ex.Message}");
    }
  }

  private static void OpenUrl(string url)
  {
    Process.Start(new ProcessStartInfo
    {
      FileName = url,
      UseShellExecute = true
    });
  }

  private void OpenLocalFile(string path)
  {
    try
    {
      if (!File.Exists(path))
      {
        AppendOutput($"File not found: {path}");
        return;
      }

      Process.Start(new ProcessStartInfo
      {
        FileName = path,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      AppendOutput($"File open error: {ex.Message}");
    }
  }

  private async Task RunPowerShellScriptAsync(string scriptName, string scriptArguments)
  {
    var scriptPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "scripts", scriptName));
    if (!File.Exists(scriptPath))
    {
      AppendOutput($"Script not found: {scriptPath}");
      return;
    }

    await RunProcessAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {scriptArguments}", Directory.GetCurrentDirectory());
  }

  private async Task RefreshTaskStatusAsync()
  {
    var result = await CaptureProcessAsync(
      "powershell.exe",
      "-NoProfile -Command \"if (Get-ScheduledTask -TaskName 'CarbonioGoogleCalendarSync' -ErrorAction SilentlyContinue) { 'enabled' } else { 'disabled' }\"",
      Directory.GetCurrentDirectory());

    if (result.ExitCode == 0 && result.Output.Contains("enabled", StringComparison.OrdinalIgnoreCase))
    {
      _taskStatusDot.ForeColor = Color.ForestGreen;
      _taskStatusText.Text = "Scheduled task enabled";
      ConfigureTaskActionButton(isTaskEnabled: true);
      return;
    }

    _taskStatusDot.ForeColor = Color.Firebrick;
    _taskStatusText.Text = "Scheduled task disabled";
    ConfigureTaskActionButton(isTaskEnabled: false);
  }

  private void ConfigureTaskActionButton(bool isTaskEnabled)
  {
    _taskActionButton.Click -= TaskActionButtonInstallClick;
    _taskActionButton.Click -= TaskActionButtonRemoveClick;

    if (isTaskEnabled)
    {
      _taskActionButton.Text = "Remove Task";
      _taskActionButton.Click += TaskActionButtonRemoveClick;
    }
    else
    {
      _taskActionButton.Text = "Install Task";
      _taskActionButton.Click += TaskActionButtonInstallClick;
    }
  }

  private async void TaskActionButtonInstallClick(object? sender, EventArgs e)
  {
    await InstallScheduledTaskAsync();
  }

  private async void TaskActionButtonRemoveClick(object? sender, EventArgs e)
  {
    await RemoveScheduledTaskAsync();
  }

  private static string GetEnginePath()
  {
    var localExe = Path.Combine(AppContext.BaseDirectory, "CarbonioGoogleCalendarSync.exe");
    if (File.Exists(localExe))
    {
      return localExe;
    }

    var publishExe = Path.Combine(Directory.GetCurrentDirectory(), "publish", "win-x64", "CarbonioGoogleCalendarSync.exe");
    if (File.Exists(publishExe))
    {
      return publishExe;
    }

    return "dotnet";
  }

  private async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
  {
    ToggleOperationButtons(false);
    AppendOutput($"> {fileName} {arguments}");

    if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    {
      arguments = $"run --project .\\src\\CarbonioGoogleCalendarSync -- {arguments}";
    }

    var startInfo = new ProcessStartInfo(fileName, arguments)
    {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    try
    {
      using var process = Process.Start(startInfo);
      if (process is null)
      {
        AppendOutput("Unable to start the process.");
        return;
      }

      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync();
      AppendOutput(await stdout);
      var error = await stderr;
      if (!string.IsNullOrWhiteSpace(error))
      {
        AppendOutput(error);
      }

      AppendOutput($"Exit code: {process.ExitCode}");
    }
    catch (Exception ex)
    {
      AppendOutput($"Command execution error: {ex.Message}");
    }
    finally
    {
      ToggleOperationButtons(true);
    }
  }

  private static async Task<(int ExitCode, string Output)> CaptureProcessAsync(string fileName, string arguments, string workingDirectory)
  {
    var startInfo = new ProcessStartInfo(fileName, arguments)
    {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    using var process = Process.Start(startInfo);
    if (process is null)
    {
      return (-1, "");
    }

    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, (await stdout) + (await stderr));
  }

  private void ToggleOperationButtons(bool enabled)
  {
    _dryRunButton.Enabled = enabled;
    _syncButton.Enabled = enabled;
  }

  private void AppendOutput(string text)
  {
    if (InvokeRequired)
    {
      BeginInvoke(() => AppendOutput(text));
      return;
    }

    if (string.IsNullOrEmpty(text))
    {
      return;
    }

    var normalizedText = text
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace("\r", "\n", StringComparison.Ordinal)
      .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    _output.AppendText(normalizedText.TrimEnd() + Environment.NewLine);
  }

  private static void AddTextRow(TableLayoutPanel form, string label, TextBox textBox)
  {
    textBox.Dock = DockStyle.Fill;
    AddRow(form, label, textBox);
  }

  private static void AddPasswordRow(TableLayoutPanel form, TextBox password)
  {
    password.Dock = DockStyle.Fill;
    password.UseSystemPasswordChar = true;
    AddRow(form, "Carbonio CalDAV Password", password);
  }

  private static void AddNumberRow(TableLayoutPanel form, string label, NumericUpDown input, decimal min, decimal max)
  {
    input.Minimum = min;
    input.Maximum = max;
    input.Dock = DockStyle.Left;
    input.Width = 120;
    AddRow(form, label, input);
  }

  private static void AddCheckRow(TableLayoutPanel form, string label, CheckBox input)
  {
    input.Dock = DockStyle.Left;
    AddRow(form, label, input);
  }

  private static void AddRow(TableLayoutPanel form, string label, Control control)
  {
    var row = form.Controls.Count / 2;
    form.RowCount = row + 1;
    form.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    form.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
    form.Controls.Add(control, 1, row);
  }

  private static void AddInfoRow(TableLayoutPanel form, string label, string value)
  {
    var row = form.Controls.Count / 2;
    form.RowCount = row + 1;
    form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
    form.Controls.Add(new Label
    {
      Text = label,
      Dock = DockStyle.Fill,
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(SystemFonts.MessageBoxFont ?? DefaultFont, FontStyle.Bold)
    }, 0, row);
    form.Controls.Add(new Label
    {
      Text = value,
      Dock = DockStyle.Fill,
      TextAlign = ContentAlignment.MiddleLeft,
      AutoEllipsis = true
    }, 1, row);
  }

  private void SetPasswordPlaceholderIfCredentialFileExists()
  {
    if (string.IsNullOrWhiteSpace(_carbonioUsername.Text))
    {
      _password.Clear();
      _passwordPlaceholderActive = false;
      return;
    }

    if (GuiCredentialStore.Exists(_carbonioUsername.Text.Trim()))
    {
      SetPasswordPlaceholder();
    }
    else
    {
      _password.Clear();
      _passwordPlaceholderActive = false;
    }
  }

  private void SetPasswordPlaceholder()
  {
    _passwordPlaceholderActive = true;
    _password.Text = "********";
  }

  private void ClearPasswordPlaceholder()
  {
    if (!_passwordPlaceholderActive)
    {
      return;
    }

    _passwordPlaceholderActive = false;
    _password.Clear();
  }

  private void SetGoogleIcsPlaceholderIfConfigured(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      _googleIcsUrl.Clear();
      _googleIcsPlaceholderActive = false;
      return;
    }

    _googleIcsPlaceholderActive = true;
    _googleIcsUrl.Text = "********";
  }

  private void ClearGoogleIcsPlaceholder()
  {
    if (!_googleIcsPlaceholderActive)
    {
      return;
    }

    _googleIcsPlaceholderActive = false;
    _googleIcsUrl.Clear();
  }

  private static Button MakeButton(string text, EventHandler handler)
  {
    var button = new Button { Text = text, Width = 180, Height = 34, Margin = new Padding(0, 0, 8, 0) };
    button.Click += handler;
    return button;
  }

  private static string NormalizeCalDavUrl(string value)
  {
    return NormalizeUrl(value);
  }

  private static string NormalizeUrl(string value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return value;
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
    {
      return value;
    }

    var segments = uri.Segments
      .Select(segment => segment.Replace("@", "%40", StringComparison.Ordinal))
      .ToArray();
    var builder = new UriBuilder(uri)
    {
      Path = string.Concat(segments).TrimStart('/')
    };
    return builder.Uri.AbsoluteUri;
  }

  private static string BuildCalDavUrl(string baseUrl, string username, string calendarName)
  {
    if (string.IsNullOrWhiteSpace(baseUrl) ||
        string.IsNullOrWhiteSpace(username) ||
        string.IsNullOrWhiteSpace(calendarName))
    {
      return "";
    }

    var cleanBaseUrl = baseUrl.TrimEnd('/');
    return $"{cleanBaseUrl}/dav/{username}/{Uri.EscapeDataString(calendarName)}/";
  }

  private void ApplyCalendarMode()
  {
    var allowNonDedicated = _allowNonGoogleCalendar.Checked;
    _carbonioCalendarName.ReadOnly = !allowNonDedicated;
    if (!allowNonDedicated)
    {
      _carbonioCalendarName.Text = "Google";
    }

    _carbonioCalendarUrl.ReadOnly = true;
    RefreshCalDavUrl();
  }

  private void RefreshCalDavUrl()
  {
    if (_loadingConfig)
    {
      return;
    }

    _carbonioCalendarUrl.Text = BuildCalDavUrl(
      _carbonioBaseUrl.Text.Trim(),
      _carbonioUsername.Text.Trim(),
      _carbonioCalendarName.Text.Trim());
  }

  private static string ToDisplayCalDavUrl(string value)
  {
    return ToDisplayUrl(value);
  }

  private static string ToDisplayUrl(string value)
  {
    return value.Replace("%40", "@", StringComparison.OrdinalIgnoreCase);
  }

  private static decimal Clamp(int value, decimal min, decimal max)
  {
    return Math.Min(Math.Max(value, min), max);
  }

  private static JsonSerializerOptions JsonOptions()
  {
    return new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
  }

  private static string GetLogsDirectory()
  {
    if (!File.Exists(ConfigPath))
    {
      return Path.Combine(AppContext.BaseDirectory, "logs");
    }

    var model = JsonSerializer.Deserialize<ConfigFileModel>(File.ReadAllText(ConfigPath), JsonOptions()) ?? new ConfigFileModel();
    var configuredDirectory = string.IsNullOrWhiteSpace(model.Logging?.Directory) ? "logs" : model.Logging.Directory;
    if (Path.IsPathRooted(configuredDirectory))
    {
      return configuredDirectory;
    }

    return Path.Combine(AppContext.BaseDirectory, configuredDirectory);
  }

  private static void MigrateLegacyConfigIfNeeded()
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

  private sealed record ConfigFileModel
  {
    public CarbonioFileModel? Carbonio { get; set; }
    public GoogleFileModel? Google { get; set; }
    public SyncFileModel? Sync { get; set; }
    public LoggingFileModel? Logging { get; set; }
    public HttpFileModel? Http { get; set; }
  }

  private sealed record CarbonioFileModel
  {
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? CalendarName { get; set; }
    public string? CalendarUrl { get; set; }
    public bool AllowNonGoogleCalendar { get; set; }
  }

  private sealed record GoogleFileModel
  {
    public string? CalendarId { get; set; }
    public string? IcsUrl { get; set; }
  }

  private sealed record SyncFileModel
  {
    public string? Direction { get; set; }
    public int PastDays { get; set; }
    public int FutureDays { get; set; }
    public bool DeleteRemovedEvents { get; set; }
    public bool DryRun { get; set; }
    public string? StateDatabasePath { get; set; }
    public string? ImportedTitlePrefix { get; set; }
  }

  private sealed record LoggingFileModel
  {
    public string? Directory { get; set; }
    public string? MinimumLevel { get; set; }
    public int RetentionDays { get; set; }
  }

  private sealed record HttpFileModel
  {
    public int TimeoutSeconds { get; set; }
  }

  private sealed class GuiCredentialStore
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

    public static bool Exists(string username)
    {
      return File.Exists(GetCredentialPath(username));
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
}
