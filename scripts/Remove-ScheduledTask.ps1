param(
  [string]$TaskName = "CarbonioGoogleCalendarSync"
)

$ErrorActionPreference = "Stop"

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -eq $task) {
  Write-Host "Task pianificato non presente: $TaskName"
  return
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Task pianificato rimosso correttamente: $TaskName"
