$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'RadioCompanion\RadioCompanion.csproj'
$out = Join-Path $PSScriptRoot 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'A .NET SDK capable of targeting .NET 8 is required.' -ForegroundColor Yellow
    Write-Host 'Install a compatible Windows x64 SDK, then run this script again.'
    exit 1
}

Push-Location $PSScriptRoot
try {
    if (Test-Path -LiteralPath $out) {
        Remove-Item -LiteralPath $out -Recurse -Force
    }

    dotnet publish $project -c Release -o $out
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

Write-Host "`nBuilt successfully:" -ForegroundColor Green
Write-Host (Join-Path $out 'RadioCompanion.exe')
