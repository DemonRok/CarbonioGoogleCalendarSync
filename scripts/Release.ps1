param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
  $status = git status --porcelain
  if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Working tree is not clean. Commit or discard changes before releasing."
  }

  $propsPath = Join-Path $repoRoot "Directory.Build.props"
  $props = Get-Content -LiteralPath $propsPath -Raw
  $props = $props -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
  Set-Content -LiteralPath $propsPath -Value $props -Encoding UTF8

  dotnet build CarbonioGoogleCalendarSync.sln --configuration Release
  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }

  dotnet test CarbonioGoogleCalendarSync.sln --configuration Release --no-build
  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }

  & (Join-Path $PSScriptRoot "Publish-WinX64.ps1")
  if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
  }

  git add Directory.Build.props
  git commit -m "Bump version to $Version" -m "Update the shared assembly version metadata for the $Version release."
  git tag "v$Version"

  Write-Host "Release prepared locally: v$Version"
  Write-Host "Push with: git push origin main v$Version"
}
finally {
  Pop-Location
}
