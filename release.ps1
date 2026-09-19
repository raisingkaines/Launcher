<#
.SYNOPSIS
    Builds a Free Realms Legacy release and publishes it to GitHub, so every installed
    launcher picks it up on next launch.

.DESCRIPTION
    Three steps, in the order Velopack needs them:

      1. Download the current published release, so this build can be a delta against it
         and players download a few MB instead of 30.
      2. Publish the app and pack it.
      3. Upload the whole releases folder to a new GitHub release.

    Needs a GitHub token with "repo" scope in $env:GITHUB_TOKEN. Create one at
    https://github.com/settings/tokens - never paste it into this file.

.EXAMPLE
    $env:GITHUB_TOKEN = "ghp_..."
    .\release.ps1 -Version 1.2.0

.EXAMPLE
    .\release.ps1 -Version 1.2.1 -Draft      # upload but do not publish yet
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$RepoUrl = 'https://github.com/raisingkaines/Launcher',

    [string]$Channel = 'win',

    # Velopack copies the whole app through the temp folder. C: is usually the tight one
    # here, so keep it beside the repo instead.
    [string]$TempDir = (Join-Path $PSScriptRoot '.build-temp'),

    # Upload as a draft instead of publishing to players immediately.
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'

function Step($message) { Write-Host "`n=== $message" -ForegroundColor Cyan }

$token = $env:GITHUB_TOKEN

if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Set $env:GITHUB_TOKEN to a GitHub token with "repo" scope before running this.'
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk is not installed. Run: dotnet tool install -g vpk'
}

$root        = $PSScriptRoot
$releasesDir = Join-Path $root 'releases'
$publishDir  = Join-Path $root 'publish'

New-Item -ItemType Directory -Force -Path $TempDir, $releasesDir | Out-Null
$env:TEMP = $TempDir
$env:TMP  = $TempDir

Step "Fetching the current published release (for delta packaging)"
# Fails harmlessly the very first time, when nothing is published yet.
vpk download github -o $releasesDir --repoUrl $RepoUrl --token $token --channel $Channel
if ($LASTEXITCODE -ne 0) {
    Write-Host 'No existing release found - this build will be a full package.' -ForegroundColor Yellow
}

Step "Publishing the app"
dotnet publish (Join-Path $root 'src\Launcher.slnx') -c Release --no-self-contained -r win-x64 --property:PublishDir=$publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Step "Packing $Version"
vpk pack `
    --packTitle 'Free Realms Legacy' `
    --packAuthors 'Raising Kaines' `
    -u FreeRealmsLegacyLauncher `
    -e Launcher.exe `
    -o $releasesDir `
    -p $publishDir `
    -i (Join-Path $publishDir 'App.ico') `
    -f net10-x64-desktop `
    --channel $Channel `
    -v $Version
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

Step "Uploading to $RepoUrl"
$uploadArgs = @(
    'upload', 'github',
    '-o', $releasesDir,
    '--repoUrl', $RepoUrl,
    '--token', $token,
    '--channel', $Channel,
    '--tag', "v$Version",
    '--releaseName', "Free Realms Legacy $Version",
    '--merge', 'false'
)

if (-not $Draft) { $uploadArgs += @('--publish', 'true') }

vpk @uploadArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk upload failed.' }

Write-Host "`nDone. " -ForegroundColor Green -NoNewline

if ($Draft) {
    Write-Host "v$Version is uploaded as a draft - publish it on GitHub when you are ready."
} else {
    Write-Host "v$Version is live. Installed launchers will update on their next launch."
}
