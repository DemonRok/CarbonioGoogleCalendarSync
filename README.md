# CarbonioGoogleCalendarSync

Windows application with a GUI and console engine to synchronize one or more Google calendars one-way into a Carbonio calendar.

## Status

The project synchronizes Google Calendar to a Carbonio calendar using:

- Google private ICS/iCal URL as the source, with support for multiple source calendars.
- Carbonio CalDAV over HTTPS as the destination.
- Local SQLite state to avoid duplicates and manage updates/deletes.
- Windows DPAPI to store the Carbonio password for the current Windows user.
- Visible marker in imported event titles, default `(G)`.
- Application logs with machine name and Windows execution user.
- Custom application icon `CarbonioGoogleCalendarSync.ico` embedded in the executables and GUI window.

## Configuration

The real `config.json` file must not be versioned. It is stored in the Windows user profile:

```text
%AppData%\CarbonioGoogleCalendarSync\config.json
```

Create it with:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- config init
```

The repository only keeps `config.example.json`, with fake values and no secrets.

## Carbonio Credential

The password is not saved in the configuration file. For scheduled execution it is stored with DPAPI, bound to the current Windows user:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- credentials set-carbonio
```

To remove it:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- credentials remove-carbonio
```

## CalDAV Test

This command verifies the calendar, creates a test event, reads it back, updates it with ETag and deletes it:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- carbonio-test
```

To keep the test event:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- carbonio-test --keep-test-event
```

The real test writes to the configured Carbonio calendar, so do not run it without consent.

## Exit Codes

- `0`: success
- `1`: generic error
- `2`: invalid configuration
- `3`: authentication failed
- `4`: calendar not found
- `5`: synchronization already running
- `6`: completed with conflicts
- `7`: temporary network error

## Google Calendar

Google reading uses the calendar private ICS/iCal URL. Google Cloud, OAuth and `google-client.json` are not required.

The ICS URL is a secret: it is stored in the Windows user protected store with DPAPI, not in `config.json`.

Steps:

1. Open Google Calendar in the browser.
2. Open the settings of the calendar to synchronize.
3. Look for "Secret address in iCal format".
4. Copy the URL.
5. Save it in the protected store with:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- config set-google-ics
```

6. Run a dry-run:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- sync --dry-run
```

The dry-run reads events in the configured window, converts them to iCalendar and prints a summary. It does not execute `PUT`, `DELETE` or any other write operation on Carbonio.

### Multiple Google Calendars

For multiple Google calendars, add one row per calendar in the GUI or edit `Google:Calendars` in the user configuration. The private ICS URLs are saved separately in the protected store:

```json
{
  "Google": {
    "Calendars": [
      {
        "Id": "primary",
        "TitlePrefix": "(G)",
        "CarbonioCalendarName": "Google"
      },
      {
        "Id": "work",
        "TitlePrefix": "(W)",
        "CarbonioCalendarName": "Work"
      }
    ]
  }
}
```

Each calendar needs a stable `Id`, its own private ICS URL saved through the GUI, and optionally a title prefix. The `Id` is a local synchronizer name, not a Google value: choose something short and unique, such as `primary`, `work` or `family`, and do not rename it later unless you want the state to be treated as a different source calendar.

`TitlePrefix` should contain only the marker, for example `(G)`. The synchronizer automatically adds the space before the event title, producing `(G) Event title`.

`CarbonioCalendarName` is the destination Carbonio calendar for that Google source. The CalDAV URL is generated automatically from Carbonio Base URL, Carbonio User and the selected calendar name.

The GUI edits the complete configured Google calendar list. Saved private ICS URLs are shown as `********`; edit the cell only when you want to replace that URL.

## Real Synchronization

First verify the plan:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- sync --dry-run
```

When the plan is correct, run the real synchronization:

```powershell
dotnet run --project .\src\CarbonioGoogleCalendarSync -- sync
```

When `StateDatabasePath` is relative, local state is saved under the Windows user profile at `%AppData%\CarbonioGoogleCalendarSync\state\sync-state.db`. This avoids having different databases between source execution and published execution. The synchronizer creates and updates only events marked with `X-CARBONIO-GOOGLE-SYNC:TRUE`; deletions are limited to events already present in local state and still verified on Carbonio as managed events.

On first run after the update, the app tries to migrate the old `state\sync-state.db` automatically, choosing the database with the most active events.

Imported events also have a configurable title prefix, default:

```text
(G) Event title
```

This makes Google appointments recognizable even inside a non-dedicated Carbonio calendar.

## Delete Imported Events

To delete from Carbonio only events imported from Google:

```powershell
.\publish\win-x64\CarbonioGoogleCalendarSync.exe purge-google
```

Deletion is protected: only events present in local state and marked with `X-CARBONIO-GOOGLE-SYNC:TRUE` are deleted.

The same function is available in the GUI with **Delete Imported**.

## Publishing

Publish the Windows x64 self-contained executable first:

```powershell
.\scripts\Publish-WinX64.ps1
```

The script publishes to `publish\win-x64`. The user configuration remains under `%AppData%\CarbonioGoogleCalendarSync\config.json`.
The publish output does not include `.pdb` files, `config.json` or local state. Application logs remain in the `logs` folder next to the executable.

Verify the published binary:

```powershell
.\publish\win-x64\CarbonioGoogleCalendarSync.exe config validate
.\publish\win-x64\CarbonioGoogleCalendarSync.exe sync --dry-run
```

Start the management GUI:

```powershell
.\publish\win-x64\CarbonioGoogleCalendarSync.Gui.exe
```

The GUI lets you edit the user `config.json`, import/export configuration, save and remove the Carbonio CalDAV password with DPAPI, run dry-run/sync/connection test, and install or remove the scheduled task.

## Management GUI

The main interface for normal use is:

```powershell
.\publish\win-x64\CarbonioGoogleCalendarSync.Gui.exe
```

From the GUI you can:

- configure Carbonio without writing JSON by hand;
- enter the Carbonio user with the normal `@`;
- automatically generate CalDAV URLs internally from Carbonio Base URL, Carbonio User and each row's Carbonio target;
- save/use generated CalDAV URLs internally with the correct `%40` escaping;
- configure one or more Google ICS URLs while showing the normal `@` in the GUI;
- add and remove Google calendars from the configuration grid;
- choose the destination Carbonio calendar for each Google source calendar;
- save/use the ICS URL internally with the correct escaping when needed;
- hide the saved Google ICS URL by showing `********`, because it is a private URL;
- configure the title prefix for imported events, default `(G)`;
- import or export the configuration file;
- save the Carbonio CalDAV password in DPAPI;
- remove the saved CalDAV password;
- see `********` when the password is already saved;
- run dry-run, real synchronization and Carbonio connection test;
- delete only events imported from Google;
- clear application logs;
- open the application AppData folder directly;
- view application information, license, useful paths, GitHub issues and releases from the **Info** tab;
- manage the scheduled task.

If the user `config.json` does not exist, the GUI tries to automatically migrate an old `config.json` found in the program or project folder. If nothing is found, it loads editable sample defaults and creates the file when you click **Save Config**.

The GUI visually shows **Operations** first and **Configuration** second. On startup:

- if the user `config.json` exists, it opens **Operations**;
- if the user `config.json` does not exist, it opens **Configuration**.

## Scheduled Task

The scheduled task does not start at a fixed clock time. It is created with:

- first run at Windows user login;
- repetition every N minutes, minimum 15 minutes;
- configurable maximum Windows task timeout;
- no parallel executions.

In the GUI, on the **Operations** tab, the task status is shown next to the action buttons with:

- red dot: task not present/not enabled;
- green dot: task present.

The task button is contextual:

- if the task is not present, it shows **Install Task**;
- if the task is present, it shows **Remove Task**.

The **Operations** tab separates main actions from tools:

- first row: **Sync** and **Dry-run**;
- second row: **Delete Imported**, **Clear Logs**, **Open AppData**, task button and task status.

The Carbonio connection test is in the **Configuration** tab as **Connection Test**, together with the CalDAV settings.

From scripts:

```powershell
.\scripts\Install-ScheduledTask.ps1 -IntervalMinutes 15 -ExecutionTimeLimitMinutes 10
.\scripts\Remove-ScheduledTask.ps1
```

The scripts do not contain passwords and work when launched from the repo root, from the `scripts` folder, from the published `publish\win-x64\scripts` folder, or from a UNC network share. The task uses the current Windows user and validates configuration from the `%AppData%` user profile; before installing it, make sure configuration and CalDAV password have been saved from the GUI or with `credentials set-carbonio` using the same user.

Script output is normalized by the GUI to preserve correct line breaks in the operations output window.

## Logs

Console engine logs always include:

- date/time;
- level;
- machine name;
- Windows execution user;
- message and optional exception.

The technical `gui-startup.log` file is no longer generated. The GUI writes only `gui-error.log` for unhandled errors, including machine name and user.

If `Logging:Directory` is relative, logs are saved under the executable folder:

```text
publish\win-x64\logs
```

From the GUI you can use **Clear Logs** to delete `.log` files and **Open AppData** to open the application user folder, where configuration, state and credentials remain.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
