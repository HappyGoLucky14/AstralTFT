param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$BuildFirst
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot

if (-not $IsWindows) { throw 'This benchmark runs on Windows only.' }
if ($BuildFirst) { & "$root\verify-windows.ps1" -Release:($Configuration -eq 'Release') }

$project = "$root\src\AstralTFT.App\AstralTFT.App.csproj"
Write-Host 'Start a TFT match first. AstralTFT will attach automatically, pause if TFT is minimized, and close after the match window closes.' -ForegroundColor Cyan
Write-Host 'A JSON capture-overhead report is written automatically under LocalAppData\AstralTFT\Diagnostics.' -ForegroundColor Cyan

dotnet run --project $project -c $Configuration --no-build
