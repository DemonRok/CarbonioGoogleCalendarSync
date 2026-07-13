param(
  [string]$Configuration = "Release",
  [string]$OutputDirectory = ".\publish\win-x64"
)

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\CarbonioGoogleCalendarSync\CarbonioGoogleCalendarSync.csproj"
$guiProjectPath = Join-Path $repoRoot "src\CarbonioGoogleCalendarSync.Gui\CarbonioGoogleCalendarSync.Gui.csproj"
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
  $OutputDirectory
}
else {
  Join-Path $repoRoot $OutputDirectory
}

dotnet publish $projectPath `
  -c $Configuration `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $resolvedOutputDirectory

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

dotnet publish $guiProjectPath `
  -c $Configuration `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $resolvedOutputDirectory

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

$publishedConfig = Join-Path $resolvedOutputDirectory "config.json"
if (Test-Path -LiteralPath $publishedConfig) {
  Remove-Item -LiteralPath $publishedConfig -Force
  Write-Host "config.json rimosso dalla publish: la configurazione utente resta in AppData."
}

$residualPaths = @(
  "state",
  "gui-startup.log",
  "gui-error.log",
  "CarbonioGoogleCalendarSync.pdb",
  "CarbonioGoogleCalendarSync.Gui.pdb",
  "Google_to_Carbonio_Import_Transparent.ico"
)

foreach ($relativePath in $residualPaths) {
  $path = Join-Path $resolvedOutputDirectory $relativePath
  if (Test-Path -LiteralPath $path) {
    Remove-Item -LiteralPath $path -Recurse -Force
    Write-Host "$relativePath rimosso dalla publish."
  }
}

$publishedScripts = Join-Path $resolvedOutputDirectory "scripts"
if (Test-Path -LiteralPath $publishedScripts) {
  Remove-Item -LiteralPath $publishedScripts -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $repoRoot "scripts") -Destination $publishedScripts -Recurse -Force
Write-Host "scripts copiati in $resolvedOutputDirectory"

Copy-Item -LiteralPath (Join-Path $repoRoot "assets\CarbonioGoogleCalendarSync.ico") -Destination (Join-Path $resolvedOutputDirectory "CarbonioGoogleCalendarSync.ico") -Force
Write-Host "icona copiata in $resolvedOutputDirectory"

Write-Host "Pubblicazione completata in $resolvedOutputDirectory"
