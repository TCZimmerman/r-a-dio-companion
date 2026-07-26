$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'RadioCompanion\RadioCompanion.csproj'
$out = Join-Path $PSScriptRoot 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'The .NET 8 SDK is required to build this project.' -ForegroundColor Yellow
    Write-Host 'Install the Windows x64 .NET 8 SDK, then run this script again.'
    exit 1
}

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

Write-Host "`nBuilt successfully:" -ForegroundColor Green
Write-Host (Join-Path $out 'RadioCompanion.exe')
