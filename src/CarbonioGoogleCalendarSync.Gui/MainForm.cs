using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarbonioGoogleCalendarSync.Gui;

public sealed class MainForm : Form
{
  private const int SwRestore = 9;
  private readonly TextBox _carbonioBaseUrl = new();
  private readonly TextBox _carbonioUsername = new();
  private readonly DataGridView _googleCalendars = new();
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

  private static string AppDataDirectory => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "CarbonioGoogleCalendarSync");

  private static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");

  public MainForm()
  {
    Text = "Carbonio Google Calendar Sync";
    Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "CarbonioGoogleCalendarSync.ico"));
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(980, 620);
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
  }

  protected override void WndProc(ref Message m)
  {
    if (m.Msg == Program.ShowExistingWindowMessage)
    {
      BringWindowToFront();
      return;
    }

    base.WndProc(ref m);
  }

  private void BringWindowToFront()
  {
    if (WindowState == FormWindowState.Minimized)
    {
      WindowState = FormWindowState.Normal;
      ShowWindow(Handle, SwRestore);
    }

    Show();
    Activate();
    BringToFront();
    SetForegroundWindow(Handle);
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
    AddControlRow(form, "Google Calendars", BuildGoogleCalendarsPanel(), 168);
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
    buttons.Controls.Add(MakeButton("Connection Test", async (_, _) => await RunConnectionTestAsync()));
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

    var version = GetDisplayVersion();
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
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));

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

  private Control BuildGoogleCalendarsPanel()
  {
    _googleCalendars.Dock = DockStyle.Fill;
    _googleCalendars.AllowUserToAddRows = false;
    _googleCalendars.AllowUserToDeleteRows = false;
    _googleCalendars.RowHeadersVisible = false;
    _googleCalendars.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    _googleCalendars.MultiSelect = false;
    _googleCalendars.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    _googleCalendars.Columns.Clear();
    _googleCalendars.Columns.Add(new DataGridViewTextBoxColumn
    {
      Name = "Id",
      HeaderText = "Local sync ID",
      ToolTipText = "Arbitrary local name used by the synchronizer, for example primary, work or family.",
      FillWeight = 18
    });
    _googleCalendars.Columns.Add(new DataGridViewTextBoxColumn
    {
      Name = "IcsUrl",
      HeaderText = "Private ICS URL",
      ToolTipText = "Private Google ICS URL. Saved URLs are protected with DPAPI and shown as ********.",
      FillWeight = 32
    });
    _googleCalendars.Columns.Add(new DataGridViewTextBoxColumn
    {
      Name = "IcsStatus",
      HeaderText = "ICS saved",
      ToolTipText = "Shows whether the private ICS URL is already stored in the current Windows user protected store.",
      ReadOnly = true,
      FillWeight = 10
    });
    _googleCalendars.Columns.Add(new DataGridViewTextBoxColumn
    {
      Name = "CarbonioCalendarName",
      HeaderText = "Carbonio target",
      ToolTipText = "Destination Carbonio calendar name. The CalDAV URL is built automatically.",
      FillWeight = 18
    });
    _googleCalendars.Columns.Add(new DataGridViewTextBoxColumn
    {
      Name = "TitlePrefix",
      HeaderText = "Title prefix",
      ToolTipText = "Write only the marker, for example (G). The space before the event title is added automatically.",
      FillWeight = 12
    });
    _googleCalendars.CellBeginEdit += (_, e) =>
    {
      if (_googleCalendars.Columns[e.ColumnIndex].Name != "IcsUrl")
      {
        return;
      }

      var cell = _googleCalendars.Rows[e.RowIndex].Cells[e.ColumnIndex];
      if (string.Equals(Convert.ToString(cell.Value), "********", StringComparison.Ordinal))
      {
        cell.Value = "";
        _googleCalendars.Rows[e.RowIndex].Tag = null;
      }
    };

    var addButton = MakeButton("Add Calendar", (_, _) => AddGoogleCalendarRow("calendar", "", "", "(G)"));
    addButton.Width = 140;
    var removeButton = MakeButton("Remove Calendar", (_, _) => RemoveSelectedGoogleCalendar());
    removeButton.Width = 150;

    var buttons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      Margin = new Padding(0, 6, 0, 0)
    };
    buttons.Controls.Add(addButton);
    buttons.Controls.Add(removeButton);

    var panel = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 2
    };
    panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
    panel.Controls.Add(_googleCalendars, 0, 0);
    panel.Controls.Add(buttons, 0, 1);
    return panel;
  }

  private void LoadConfig()
  {
    try
    {
      MigrateLegacyConfigIfNeeded();
      if (!File.Exists(ConfigPath))
      {
        LoadDefaults();
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
      MigrateGoogleIcsUrlsToDpapi(model);
      _carbonioBaseUrl.Text = model.Carbonio?.BaseUrl ?? "";
      _carbonioUsername.Text = model.Carbonio?.Username ?? "";
      LoadGoogleCalendars(model);
      _pastDays.Value = Clamp(model.Sync?.PastDays ?? 30, _pastDays.Minimum, _pastDays.Maximum);
      _futureDays.Value = Clamp(model.Sync?.FutureDays ?? 365, _futureDays.Minimum, _futureDays.Maximum);
      _deleteRemoved.Checked = model.Sync?.DeleteRemovedEvents ?? true;
      SetPasswordPlaceholderIfCredentialFileExists();
      AppendOutput("Configuration loaded.");
    }
    catch (Exception ex)
    {
      AppendOutput($"Configuration load error: {ex.Message}");
    }
    finally
    {
    }
  }

  private void LoadDefaults()
  {
    _carbonioBaseUrl.Text = "https://webmail.example.local";
    _carbonioUsername.Text = "user.name@example.local";
    LoadGoogleCalendars(new ConfigFileModel
    {
      Google = new GoogleFileModel
      {
        CalendarId = "primary",
        IcsUrl = "https://calendar.google.com/calendar/ical/your-calendar-id/private-token/basic.ics",
        Calendars =
        [
          new GoogleCalendarFileModel(
            Id: "primary",
            IcsUrl: "https://calendar.google.com/calendar/ical/your-calendar-id/private-token/basic.ics",
            TitlePrefix: "(G)",
            CarbonioCalendarName: "Google")
        ]
      },
      Sync = new SyncFileModel { ImportedTitlePrefix = "(G)" }
    });
    _pastDays.Value = 30;
    _futureDays.Value = 365;
    _deleteRemoved.Checked = true;
    _password.Clear();
    _passwordPlaceholderActive = false;
  }

  private void LoadGoogleCalendars(ConfigFileModel model)
  {
    _googleCalendars.Rows.Clear();
    var calendars = model.Google?.Calendars.Count > 0
      ? model.Google.Calendars
      : [];

    if (calendars.Count == 0 && !string.IsNullOrWhiteSpace(model.Google?.IcsUrl))
    {
      calendars =
      [
        new GoogleCalendarFileModel(
          Id: model.Google.CalendarId ?? "primary",
          IcsUrl: model.Google.IcsUrl,
          TitlePrefix: model.Sync?.ImportedTitlePrefix ?? "(G)",
          CarbonioCalendarName: "Google")
      ];
    }

    if (calendars.Count == 0)
    {
      calendars =
      [
        new GoogleCalendarFileModel(
          Id: "primary",
          IcsUrl: "https://calendar.google.com/calendar/ical/your-calendar-id/private-token/basic.ics",
          TitlePrefix: "(G)",
          CarbonioCalendarName: "Google")
      ];
    }

    foreach (var calendar in calendars)
    {
      var storedIcsUrlExists = !string.IsNullOrWhiteSpace(calendar.IcsUrl) ||
        (!string.IsNullOrWhiteSpace(model.Carbonio?.Username) && GuiCredentialStore.GoogleIcsExists(model.Carbonio.Username, calendar.Id));
      AddGoogleCalendarRow(
        calendar.Id,
        calendar.IcsUrl ?? "",
        calendar.CarbonioCalendarName ?? "Google",
        NormalizeTitlePrefixForConfig(calendar.TitlePrefix ?? model.Sync?.ImportedTitlePrefix ?? "(G)"),
        maskIcsUrl: storedIcsUrlExists);
    }
  }

  private void AddGoogleCalendarRow(
    string id,
    string icsUrl,
    string carbonioCalendarName,
    string titlePrefix,
    bool maskIcsUrl = false)
  {
    var rowIndex = _googleCalendars.Rows.Add(
      id,
      maskIcsUrl ? "********" : ToDisplayUrl(icsUrl),
      maskIcsUrl ? "Yes" : "No",
      carbonioCalendarName,
      titlePrefix);
    _googleCalendars.Rows[rowIndex].Tag = new GoogleCalendarRowMetadata(maskIcsUrl ? icsUrl : null);
  }

  private void RemoveSelectedGoogleCalendar()
  {
    if (_googleCalendars.SelectedRows.Count == 0)
    {
      return;
    }

    if (_googleCalendars.Rows.Count <= 1)
    {
      AppendOutput("At least one Google calendar must remain configured.");
      return;
    }

    var row = _googleCalendars.SelectedRows[0];
    var calendarId = Convert.ToString(row.Cells["Id"].Value)?.Trim() ?? "";
    if (!string.IsNullOrWhiteSpace(calendarId))
    {
      if (string.IsNullOrWhiteSpace(_carbonioUsername.Text))
      {
        AppendOutput($"Google calendar row removed, but protected ICS URL could not be removed because Carbonio User is empty: {calendarId}.");
      }
      else
      {
        var store = new GuiCredentialStore();
        store.RemoveGoogleIcsUrl(_carbonioUsername.Text.Trim(), calendarId);
        AppendOutput($"Protected Google ICS URL removed for calendar {calendarId}.");
      }
    }

    _googleCalendars.Rows.RemoveAt(row.Index);
  }

  private List<GoogleCalendarFileModel> ReadGoogleCalendars()
  {
    var calendars = new List<GoogleCalendarFileModel>();
    foreach (DataGridViewRow row in _googleCalendars.Rows)
    {
      if (row.IsNewRow)
      {
        continue;
      }

      var id = Convert.ToString(row.Cells["Id"].Value)?.Trim() ?? "";
      var displayedIcsUrl = Convert.ToString(row.Cells["IcsUrl"].Value)?.Trim() ?? "";
      var metadata = row.Tag as GoogleCalendarRowMetadata;
      var icsUrl = string.Equals(displayedIcsUrl, "********", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(metadata?.IcsUrl)
        ? metadata.IcsUrl
        : NormalizeUrl(displayedIcsUrl);
      var carbonioCalendarName = Convert.ToString(row.Cells["CarbonioCalendarName"].Value)?.Trim() ?? "";
      var titlePrefix = NormalizeTitlePrefixForConfig(Convert.ToString(row.Cells["TitlePrefix"].Value));

      if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(icsUrl))
      {
        continue;
      }

      calendars.Add(new GoogleCalendarFileModel(id, null, titlePrefix, carbonioCalendarName));
    }

    return calendars;
  }

  private bool SaveConfig()
  {
    try
    {
      var validationError = ValidateConfigurationInput();
      if (validationError is not null)
      {
        AppendOutput($"Configuration validation error: {validationError}");
        return false;
      }

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
      model.Carbonio.CalendarName = null;
      model.Carbonio.CalendarUrl = null;
      SaveGoogleIcsUrlsFromGrid(model.Carbonio.Username);
      model.Google.Calendars = ReadGoogleCalendars();
      var firstCalendar = model.Google.Calendars.FirstOrDefault();
      model.Google.CalendarId = null;
      model.Google.IcsUrl = null;
      model.Sync.Direction = "GoogleToCarbonio";
      model.Sync.PastDays = (int)_pastDays.Value;
      model.Sync.FutureDays = (int)_futureDays.Value;
      model.Sync.DeleteRemovedEvents = _deleteRemoved.Checked;
      model.Sync.StateDatabasePath = string.IsNullOrWhiteSpace(model.Sync.StateDatabasePath) ? "state/sync-state.db" : model.Sync.StateDatabasePath;
      model.Sync.ImportedTitlePrefix = firstCalendar?.TitlePrefix ?? "(G)";
      model.Logging.Directory = string.IsNullOrWhiteSpace(model.Logging.Directory) ? "logs" : model.Logging.Directory;
      model.Logging.MinimumLevel = string.IsNullOrWhiteSpace(model.Logging.MinimumLevel) ? "Information" : model.Logging.MinimumLevel;
      model.Logging.RetentionDays = model.Logging.RetentionDays <= 0 ? 30 : model.Logging.RetentionDays;
      model.Http.TimeoutSeconds = model.Http.TimeoutSeconds <= 0 ? 60 : model.Http.TimeoutSeconds;

      File.WriteAllText(ConfigPath, JsonSerializer.Serialize(model, JsonOptions()));
      LoadGoogleCalendars(model);
      AppendOutput("Configuration saved.");
      return true;
    }
    catch (Exception ex)
    {
      AppendOutput($"Configuration save error: {ex.Message}");
      return false;
    }
  }

  private string? ValidateConfigurationInput()
  {
    if (!Uri.TryCreate(_carbonioBaseUrl.Text.Trim(), UriKind.Absolute, out var baseUri) ||
        baseUri.Scheme != Uri.UriSchemeHttps)
    {
      return "Carbonio Base URL must be an absolute HTTPS URL.";
    }

    if (string.IsNullOrWhiteSpace(_carbonioUsername.Text))
    {
      return "Carbonio User is required.";
    }

    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (DataGridViewRow row in _googleCalendars.Rows)
    {
      if (row.IsNewRow)
      {
        continue;
      }

      var id = Convert.ToString(row.Cells["Id"].Value)?.Trim() ?? "";
      var displayedIcsUrl = Convert.ToString(row.Cells["IcsUrl"].Value)?.Trim() ?? "";
      var carbonioTarget = Convert.ToString(row.Cells["CarbonioCalendarName"].Value)?.Trim() ?? "";
      var hasProtectedIcs = GuiCredentialStore.GoogleIcsExists(_carbonioUsername.Text.Trim(), id);
      if (string.IsNullOrWhiteSpace(id))
      {
        return "Each Google calendar row must have a Local sync ID.";
      }

      if (!ids.Add(id))
      {
        return $"Duplicate Local sync ID: {id}.";
      }

      if (string.IsNullOrWhiteSpace(carbonioTarget))
      {
        return $"Carbonio target is required for Google calendar {id}.";
      }

      if (!string.IsNullOrWhiteSpace(displayedIcsUrl) &&
          !string.Equals(displayedIcsUrl, "********", StringComparison.Ordinal) &&
          (!Uri.TryCreate(displayedIcsUrl, UriKind.Absolute, out var icsUri) ||
          icsUri.Scheme != Uri.UriSchemeHttps))
      {
        return $"Private ICS URL must be an absolute HTTPS URL for Google calendar {id}.";
      }

      if ((string.IsNullOrWhiteSpace(displayedIcsUrl) ||
          string.Equals(displayedIcsUrl, "********", StringComparison.Ordinal)) &&
          !hasProtectedIcs)
      {
        return $"Private ICS URL is required for Google calendar {id}.";
      }
    }

    return ids.Count == 0 ? "At least one Google calendar must be configured." : null;
  }

  private void ImportConfig()
  {
    using var dialog = new OpenFileDialog
    {
      Title = "Import configuration",
      Filter = "Encrypted sync export (*.cgsync)|*.cgsync|JSON configuration (*.json)|*.json|All files (*.*)|*.*",
      FileName = "CarbonioGoogleCalendarSync.cgsync"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      var json = File.ReadAllText(dialog.FileName);
      var export = JsonSerializer.Deserialize<EncryptedExportFile>(json, JsonOptions());
      if (export?.Format == "CarbonioGoogleCalendarSync.EncryptedExport.v1")
      {
        var passphrase = PromptSecret("Import encrypted configuration", "Export password:");
        if (passphrase is null)
        {
          return;
        }

        var payload = DecryptExport(export, passphrase);
        Directory.CreateDirectory(AppDataDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(payload.Config, JsonOptions()));
        SaveImportedSecrets(payload);
      }
      else
      {
        JsonSerializer.Deserialize<ConfigFileModel>(json, JsonOptions());
        Directory.CreateDirectory(AppDataDirectory);
        File.Copy(dialog.FileName, ConfigPath, overwrite: true);
      }

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
      Filter = "Encrypted sync export (*.cgsync)|*.cgsync|All files (*.*)|*.*",
      FileName = "CarbonioGoogleCalendarSync.cgsync"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      SaveConfig();
      var passphrase = PromptSecret("Export encrypted configuration", "Export password:");
      if (passphrase is null)
      {
        return;
      }

      var payload = BuildExportPayload();
      var encrypted = EncryptExport(payload, passphrase);
      File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(encrypted, JsonOptions()));
      AppendOutput($"Encrypted configuration exported to {dialog.FileName}.");
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
    if (!SaveConfig())
    {
      return;
    }

    await RunProcessAsync(GetEnginePath(), arguments, Directory.GetCurrentDirectory());
  }

  private async Task RunConnectionTestAsync()
  {
    if (!SaveConfig())
    {
      return;
    }

    ToggleOperationButtons(false);
    var fileName = GetEnginePath();
    var arguments = "connection-test";
    AppendOutput($"> {fileName} {arguments}");

    try
    {
      var result = await CaptureProcessAsync(
        fileName,
        BuildEngineArguments(fileName, arguments),
        Directory.GetCurrentDirectory());
      var output = result.Output.Trim();
      if (!string.IsNullOrWhiteSpace(output))
      {
        AppendOutput(output);
      }

      AppendOutput($"Exit code: {result.ExitCode}");

      var message = string.IsNullOrWhiteSpace(output)
        ? $"Connection test completed with exit code {result.ExitCode}."
        : output;
      message = $"{message}{Environment.NewLine}{Environment.NewLine}Exit code: {result.ExitCode}";

      MessageBox.Show(
        this,
        TruncateDialogText(message),
        result.ExitCode == 0 ? "Connection Test" : "Connection Test Failed",
        MessageBoxButtons.OK,
        result.ExitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }
    catch (Exception ex)
    {
      AppendOutput($"Connection test error: {ex.Message}");
      MessageBox.Show(
        this,
        ex.Message,
        "Connection Test Failed",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    }
    finally
    {
      ToggleOperationButtons(true);
    }
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

  private void SaveGoogleIcsUrlsFromGrid(string username)
  {
    if (string.IsNullOrWhiteSpace(username))
    {
      return;
    }

    var store = new GuiCredentialStore();
    foreach (DataGridViewRow row in _googleCalendars.Rows)
    {
      if (row.IsNewRow)
      {
        continue;
      }

      var id = Convert.ToString(row.Cells["Id"].Value)?.Trim() ?? "";
      var displayedIcsUrl = Convert.ToString(row.Cells["IcsUrl"].Value)?.Trim() ?? "";
      if (string.IsNullOrWhiteSpace(id) ||
          string.IsNullOrWhiteSpace(displayedIcsUrl) ||
          string.Equals(displayedIcsUrl, "********", StringComparison.Ordinal))
      {
        continue;
      }

      store.SaveGoogleIcsUrl(username, id, NormalizeUrl(displayedIcsUrl));
      row.Cells["IcsUrl"].Value = "********";
      row.Cells["IcsStatus"].Value = "Yes";
      row.Tag = new GoogleCalendarRowMetadata(NormalizeUrl(displayedIcsUrl));
    }
  }

  private ExportPayload BuildExportPayload()
  {
    var config = JsonSerializer.Deserialize<ConfigFileModel>(File.ReadAllText(ConfigPath), JsonOptions()) ?? new ConfigFileModel();
    var store = new GuiCredentialStore();
    var googleSecrets = new List<GoogleIcsSecret>();
    var username = config.Carbonio?.Username ?? "";
    foreach (var calendar in config.Google?.Calendars ?? [])
    {
      if (string.IsNullOrWhiteSpace(username))
      {
        continue;
      }

      var icsUrl = store.GetGoogleIcsUrl(username, calendar.Id);
      if (!string.IsNullOrWhiteSpace(icsUrl))
      {
        googleSecrets.Add(new GoogleIcsSecret(calendar.Id, icsUrl));
      }
    }

    var carbonioPassword = string.IsNullOrWhiteSpace(username)
      ? null
      : store.GetCarbonioPassword(username);
    return new ExportPayload(config, googleSecrets, carbonioPassword);
  }

  private void SaveImportedSecrets(ExportPayload payload)
  {
    var username = payload.Config.Carbonio?.Username;
    if (string.IsNullOrWhiteSpace(username))
    {
      return;
    }

    var store = new GuiCredentialStore();
    foreach (var secret in payload.GoogleIcsUrls)
    {
      store.SaveGoogleIcsUrl(username, secret.CalendarId, secret.IcsUrl);
    }

    if (!string.IsNullOrWhiteSpace(payload.CarbonioPassword))
    {
      store.SaveCarbonioPassword(username, payload.CarbonioPassword);
    }
  }

  private static EncryptedExportFile EncryptExport(ExportPayload payload, string passphrase)
  {
    var salt = RandomNumberGenerator.GetBytes(16);
    var nonce = RandomNumberGenerator.GetBytes(12);
    using var kdf = new Rfc2898DeriveBytes(passphrase, salt, 210_000, HashAlgorithmName.SHA256);
    var key = kdf.GetBytes(32);
    var plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions()));
    var cipherBytes = new byte[plainBytes.Length];
    var tag = new byte[16];
    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

    return new EncryptedExportFile(
      "CarbonioGoogleCalendarSync.EncryptedExport.v1",
      "AES-256-GCM",
      210_000,
      Convert.ToBase64String(salt),
      Convert.ToBase64String(nonce),
      Convert.ToBase64String(tag),
      Convert.ToBase64String(cipherBytes));
  }

  private static ExportPayload DecryptExport(EncryptedExportFile export, string passphrase)
  {
    var salt = Convert.FromBase64String(export.Salt);
    var nonce = Convert.FromBase64String(export.Nonce);
    var tag = Convert.FromBase64String(export.Tag);
    var cipherBytes = Convert.FromBase64String(export.CipherText);
    using var kdf = new Rfc2898DeriveBytes(passphrase, salt, export.Iterations, HashAlgorithmName.SHA256);
    var key = kdf.GetBytes(32);
    var plainBytes = new byte[cipherBytes.Length];
    using var aes = new AesGcm(key, tag.Length);
    aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
    return JsonSerializer.Deserialize<ExportPayload>(Encoding.UTF8.GetString(plainBytes), JsonOptions()) ??
      throw new InvalidOperationException("Encrypted export payload is invalid.");
  }

  private static string? PromptSecret(string title, string label)
  {
    using var form = new Form
    {
      Text = title,
      StartPosition = FormStartPosition.CenterParent,
      FormBorderStyle = FormBorderStyle.FixedDialog,
      MinimizeBox = false,
      MaximizeBox = false,
      ClientSize = new Size(420, 116)
    };
    var textLabel = new Label
    {
      Text = label,
      Left = 12,
      Top = 16,
      Width = 390
    };
    var input = new TextBox
    {
      Left = 12,
      Top = 40,
      Width = 390,
      UseSystemPasswordChar = true
    };
    var ok = new Button
    {
      Text = "OK",
      DialogResult = DialogResult.OK,
      Left = 246,
      Top = 76,
      Width = 75
    };
    var cancel = new Button
    {
      Text = "Cancel",
      DialogResult = DialogResult.Cancel,
      Left = 327,
      Top = 76,
      Width = 75
    };
    form.Controls.Add(textLabel);
    form.Controls.Add(input);
    form.Controls.Add(ok);
    form.Controls.Add(cancel);
    form.AcceptButton = ok;
    form.CancelButton = cancel;
    return form.ShowDialog() == DialogResult.OK ? input.Text : null;
  }

  private void MigrateGoogleIcsUrlsToDpapi(ConfigFileModel model)
  {
    if (model.Google is null ||
        string.IsNullOrWhiteSpace(model.Carbonio?.Username))
    {
      return;
    }

    var username = model.Carbonio.Username;
    var changed = false;
    var calendars = model.Google.Calendars;
    if (calendars.Count == 0 && !string.IsNullOrWhiteSpace(model.Google.IcsUrl))
    {
      calendars.Add(new GoogleCalendarFileModel(
        model.Google.CalendarId ?? "primary",
        model.Google.IcsUrl,
        model.Sync?.ImportedTitlePrefix ?? "(G)",
        model.Carbonio?.CalendarName));
      changed = true;
    }

    var legacyCalendarName = model.Carbonio?.CalendarName;
    if (calendars.Count > 0 && !string.IsNullOrWhiteSpace(legacyCalendarName))
    {
      for (var index = 0; index < calendars.Count; index++)
      {
        var calendar = calendars[index];
        if (string.IsNullOrWhiteSpace(calendar.CarbonioCalendarName))
        {
          calendars[index] = calendar with { CarbonioCalendarName = legacyCalendarName };
          changed = true;
        }
      }
    }

    var store = new GuiCredentialStore();
    for (var index = 0; index < calendars.Count; index++)
    {
      var calendar = calendars[index];
      if (string.IsNullOrWhiteSpace(calendar.IcsUrl))
      {
        continue;
      }

      store.SaveGoogleIcsUrl(username, calendar.Id, calendar.IcsUrl!);
      calendars[index] = calendar with { IcsUrl = null };
      changed = true;
    }

    if (!string.IsNullOrWhiteSpace(model.Google.CalendarId) ||
        !string.IsNullOrWhiteSpace(model.Google.IcsUrl))
    {
      model.Google.CalendarId = null;
      model.Google.IcsUrl = null;
      changed = true;
    }

    if (model.Carbonio is not null &&
        (!string.IsNullOrWhiteSpace(model.Carbonio.CalendarName) ||
        !string.IsNullOrWhiteSpace(model.Carbonio.CalendarUrl)))
    {
      model.Carbonio.CalendarName = null;
      model.Carbonio.CalendarUrl = null;
      changed = true;
    }

    if (changed)
    {
      File.WriteAllText(ConfigPath, JsonSerializer.Serialize(model, JsonOptions()));
      AppendOutput("Google ICS URLs migrated to the Windows user protected store.");
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
    arguments = BuildEngineArguments(fileName, arguments);

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

  private static string BuildEngineArguments(string fileName, string arguments)
  {
    return fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
      ? $"run --project .\\src\\CarbonioGoogleCalendarSync -- {arguments}"
      : arguments;
  }

  private static string TruncateDialogText(string text)
  {
    const int maxLength = 6000;
    return text.Length <= maxLength
      ? text
      : text[..maxLength] + Environment.NewLine + "... output truncated ...";
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

  [DllImport("user32.dll")]
  private static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

  private static void AddControlRow(TableLayoutPanel form, string label, Control control, int height)
  {
    var row = form.Controls.Count / 2;
    form.RowCount = row + 1;
    form.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
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

  private static string GetDisplayVersion()
  {
    var assembly = Assembly.GetExecutingAssembly();
    var informationalVersion = assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion;
    if (!string.IsNullOrWhiteSpace(informationalVersion))
    {
      var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
      return metadataSeparator > 0
        ? informationalVersion[..metadataSeparator]
        : informationalVersion;
    }

    return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
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

  private static string ToDisplayUrl(string value)
  {
    return value.Replace("%40", "@", StringComparison.OrdinalIgnoreCase);
  }

  private static string NormalizeTitlePrefixForConfig(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  private static decimal Clamp(int value, decimal min, decimal max)
  {
    return Math.Min(Math.Max(value, min), max);
  }

  private static JsonSerializerOptions JsonOptions()
  {
    return new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
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
  }

  private sealed record GoogleFileModel
  {
    public string? CalendarId { get; set; }
    public string? IcsUrl { get; set; }
    public List<GoogleCalendarFileModel> Calendars { get; set; } = [];
  }

  private sealed record GoogleCalendarFileModel(
    string Id,
    string? IcsUrl,
    string? TitlePrefix,
    string? CarbonioCalendarName = null,
    string? CarbonioCalendarUrl = null);

  private sealed record GoogleCalendarRowMetadata(string? IcsUrl);

  private sealed record ExportPayload(
    ConfigFileModel Config,
    List<GoogleIcsSecret> GoogleIcsUrls,
    string? CarbonioPassword);

  private sealed record GoogleIcsSecret(string CalendarId, string IcsUrl);

  private sealed record EncryptedExportFile(
    string Format,
    string Algorithm,
    int Iterations,
    string Salt,
    string Nonce,
    string Tag,
    string CipherText);

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
    private const string GoogleIcsPurpose = "CarbonioGoogleCalendarSync.GoogleIcsUrl";

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

    public void SaveCarbonioPassword(string username, string password)
    {
      var path = GetCredentialPath(username);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var protectedBytes = ProtectedData.Protect(
        Encoding.UTF8.GetBytes(password),
        Encoding.UTF8.GetBytes(Purpose),
        DataProtectionScope.CurrentUser);
      File.WriteAllBytes(path, protectedBytes);
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

    public string? GetCarbonioPassword(string username)
    {
      var path = GetCredentialPath(username);
      if (!File.Exists(path))
      {
        return null;
      }

      var protectedBytes = File.ReadAllBytes(path);
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

    public void SaveGoogleIcsUrl(string username, string calendarId, string icsUrl)
    {
      var path = GetGoogleIcsPath(username, calendarId);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var protectedBytes = ProtectedData.Protect(
        Encoding.UTF8.GetBytes(icsUrl),
        Encoding.UTF8.GetBytes(GoogleIcsPurpose),
        DataProtectionScope.CurrentUser);
      File.WriteAllBytes(path, protectedBytes);
    }

    public string? GetGoogleIcsUrl(string username, string calendarId)
    {
      var path = GetGoogleIcsPath(username, calendarId);
      if (!File.Exists(path))
      {
        return null;
      }

      var protectedBytes = File.ReadAllBytes(path);
      var clearBytes = ProtectedData.Unprotect(
        protectedBytes,
        Encoding.UTF8.GetBytes(GoogleIcsPurpose),
        DataProtectionScope.CurrentUser);
      return Encoding.UTF8.GetString(clearBytes);
    }

    public static bool GoogleIcsExists(string username, string calendarId)
    {
      return File.Exists(GetGoogleIcsPath(username, calendarId));
    }

    public void RemoveGoogleIcsUrl(string username, string calendarId)
    {
      var path = GetGoogleIcsPath(username, calendarId);
      if (File.Exists(path))
      {
        File.Delete(path);
      }
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

    private static string GetGoogleIcsPath(string username, string calendarId)
    {
      var key = $"{username.ToLowerInvariant()}|{calendarId.ToLowerInvariant()}";
      var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
      return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CarbonioGoogleCalendarSync",
        "credentials",
        "google-ics",
        $"{fileName}.bin");
    }
  }
}
