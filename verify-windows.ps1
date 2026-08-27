param(
    [switch]$Release,
    [switch]$SkipPreflight
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$configuration = if ($Release) { 'Release' } else { 'Debug' }

function Step([string]$Text) {
    Write-Host "`n==> $Text" -ForegroundColor Magenta
}

if (-not $IsWindows) {
    throw 'AstralTFT Windows capture verification must run on Windows.'
}

Step '.NET SDK check'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw '.NET 10 SDK not found. Install the current .NET 10 SDK, then run this script again.'
}

$sdkLines = @(dotnet --list-sdks)
$sdk10 = @($sdkLines | Where-Object { $_ -match '^10\.' })
if ($sdk10.Count -eq 0) {
    throw ".NET 10 SDK not found. Installed SDKs:`n$($sdkLines -join "`n")"
}
Write-Host "Using .NET 10 SDK candidate: $($sdk10[-1])"
dotnet --info

if (-not $SkipPreflight) {
    Step 'Windows/TFT preflight metadata'
    & "$root\scripts-doctor.ps1" -OutputPath "$root\astraltft-preflight.json"
}

Step 'Restore'
dotnet restore "$root\AstralTFT.slnx"
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Step "Build ($configuration)"
dotnet build "$root\AstralTFT.slnx" -c $configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

Step 'State self-tests'
dotnet run --project "$root\tests\AstralTFT.State.Tests\AstralTFT.State.Tests.csproj" -c $configuration --no-build
if ($LASTEXITCODE -ne 0) { throw 'State self-tests failed.' }

Step 'Foundation/capture self-tests'
dotnet run --project "$root\tests\AstralTFT.Foundation.Tests\AstralTFT.Foundation.Tests.csproj" -c $configuration --no-build
if ($LASTEXITCODE -ne 0) { throw 'Foundation self-tests failed.' }

Step 'Verification complete'
Write-Host 'The project compiled and package/API signatures resolved on Windows.' -ForegroundColor Green
Write-Host 'Next runtime gate: launch TFT, then run the AstralTFT diagnostic app.'
