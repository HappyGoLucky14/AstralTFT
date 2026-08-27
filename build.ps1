param(
    [switch]$Release,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$configuration = if ($Release) { 'Release' } else { 'Debug' }

Write-Host "AstralTFT build ($configuration)"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK not found. Install .NET 10 SDK first.'
}

dotnet --info
dotnet restore "$PSScriptRoot\AstralTFT.slnx"
dotnet build "$PSScriptRoot\AstralTFT.slnx" -c $configuration --no-restore

if ($SelfTest) {
    dotnet run --project "$PSScriptRoot\tests\AstralTFT.State.Tests\AstralTFT.State.Tests.csproj" -c $configuration --no-build
    dotnet run --project "$PSScriptRoot\tests\AstralTFT.Foundation.Tests\AstralTFT.Foundation.Tests.csproj" -c $configuration --no-build
}
