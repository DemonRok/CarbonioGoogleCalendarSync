param(
  [string]$TaskName = "CarbonioGoogleCalendarSync",
  [string]$ExecutablePath = "",
  [string]$WorkingDirectory = "",
  [int]$IntervalMinutes = 15,
  [int]$ExecutionTimeLimitMinutes = 10
)

$ErrorActionPreference = "Stop"

function Normalize-ProviderPathString {
  param(
    [string]$PathValue
  )

  if ([string]::IsNullOrWhiteSpace($PathValue)) {
    return $PathValue
  }

  return $PathValue -replace '^Microsoft\.PowerShell\.Core\\FileSystem::', ''
}

function Resolve-ProviderPath {
  param(
    [string]$PathValue
  )

  $PathValue = Normalize-ProviderPathString $PathValue
  return (Resolve-Path -LiteralPath $PathValue -ErrorAction Stop).ProviderPath
}

$scriptDirectory = Normalize-ProviderPathString (Split-Path -Parent $MyInvocation.MyCommand.Path)
$parentDirectory = Resolve-ProviderPath (Join-Path $scriptDirectory "..")
$repoRoot = $parentDirectory
$publishedRoot = if (Test-Path -LiteralPath (Join-Path $parentDirectory "CarbonioGoogleCalendarSync.exe")) {
  $parentDirectory
}
else {
  Join-Path $repoRoot "publish\win-x64"
}

function Resolve-AppPath {
  param(
    [string]$PathValue,
    [string]$FallbackFromCurrent,
    [string]$FallbackFromRepo
  )

  $PathValue = Normalize-ProviderPathString $PathValue
  $FallbackFromCurrent = Normalize-ProviderPathString $FallbackFromCurrent
  $FallbackFromRepo = Normalize-ProviderPathString $FallbackFromRepo

  if (-not [string]::IsNullOrWhiteSpace($PathValue)) {
    if (Test-Path -LiteralPath $PathValue) {
      return $PathValue
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
      return $PathValue
    }

    $fromCurrent = Join-Path (Get-Location) $PathValue
    if (Test-Path -LiteralPath $fromCurrent) {
      return $fromCurrent
    }

    return Join-Path $publishedRoot $PathValue
  }

  if (-not [string]::IsNullOrWhiteSpace($FallbackFromCurrent) -and (Test-Path -LiteralPath $FallbackFromCurrent)) {
    return $FallbackFromCurrent
  }

  return $FallbackFromRepo
}

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
  $ExecutablePath = Resolve-AppPath `
    -PathValue "" `
    -FallbackFromCurrent (Join-Path (Get-Location) "CarbonioGoogleCalendarSync.exe") `
    -FallbackFromRepo (Join-Path $publishedRoot "CarbonioGoogleCalendarSync.exe")
}

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
  $currentExecutable = Join-Path (Get-Location) "CarbonioGoogleCalendarSync.exe"
  $currentWorkingDirectory = if (Test-Path -LiteralPath $currentExecutable) {
    (Get-Location).Path
  }
  else {
    ""
  }

  $WorkingDirectory = Resolve-AppPath `
    -PathValue "" `
    -FallbackFromCurrent $currentWorkingDirectory `
    -FallbackFromRepo $publishedRoot
}
else {
  $WorkingDirectory = Resolve-AppPath `
    -PathValue $WorkingDirectory `
    -FallbackFromCurrent (Get-Location).Path `
    -FallbackFromRepo $publishedRoot
}

$ExecutablePath = Resolve-AppPath `
  -PathValue $ExecutablePath `
  -FallbackFromCurrent (Join-Path (Get-Location) "CarbonioGoogleCalendarSync.exe") `
  -FallbackFromRepo (Join-Path $publishedRoot "CarbonioGoogleCalendarSync.exe")

$resolvedExecutable = Resolve-ProviderPath $ExecutablePath
$resolvedWorkingDirectory = Resolve-ProviderPath $WorkingDirectory

if ($IntervalMinutes -lt 15) {
  throw "IntervalMinutes deve essere almeno 15"
}

if ($ExecutionTimeLimitMinutes -lt 1) {
  throw "ExecutionTimeLimitMinutes deve essere maggiore di zero"
}

& $resolvedExecutable config validate
if ($LASTEXITCODE -ne 0) {
  throw "Configurazione non valida o non trovata nel profilo utente AppData."
}

$executionLimit = "PT$($ExecutionTimeLimitMinutes)M"
$interval = "PT$($IntervalMinutes)M"
$author = [System.Security.SecurityElement]::Escape("$env:USERDOMAIN\$env:USERNAME")
$userId = [System.Security.SecurityElement]::Escape("$env:USERDOMAIN\$env:USERNAME")
$command = [System.Security.SecurityElement]::Escape((Get-Command powershell.exe).Source)
$syncArguments = [System.Security.SecurityElement]::Escape("-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command `"& '$resolvedExecutable' sync`"")
$workingDirectory = [System.Security.SecurityElement]::Escape($resolvedWorkingDirectory)

$taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>$author</Author>
    <Description>Sincronizza Google Calendar verso Carbonio</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>$userId</UserId>
      <Repetition>
        <Interval>$interval</Interval>
        <Duration>P9999D</Duration>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$userId</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>$executionLimit</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$command</Command>
      <Arguments>$syncArguments</Arguments>
      <WorkingDirectory>$workingDirectory</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@

$task = Register-ScheduledTask `
  -TaskName $TaskName `
  -Xml $taskXml `
  -Force

Write-Host "Attivita' pianificata installata: $($task.TaskName)"
Write-Host "Eseguibile: $resolvedExecutable"
Write-Host "Directory lavoro: $resolvedWorkingDirectory"
Write-Host "Intervallo minuti: $IntervalMinutes"
Write-Host "Task timeout minutes: $ExecutionTimeLimitMinutes"
Write-Host "Trigger: al login dell'utente, poi ogni $IntervalMinutes minuti"
Write-Host "Esecuzione nascosta: si"
