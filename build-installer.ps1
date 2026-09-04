[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$Runtime = "win-x64"
$artifacts = Join-Path $projectRoot "artifacts"
$publish = Join-Path $artifacts "publish\$Runtime"
$installerOutput = Join-Path $artifacts "installer"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0"
}

$sdkMajor = [int]((dotnet --version).Split('.')[0])
if ($sdkMajor -lt 8) { throw ".NET SDK 8 or newer is required." }

Push-Location $projectRoot
try {
    dotnet restore .\ArcTrellis.sln
    if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

    if (-not $SkipTests) {
        dotnet run --project .\tests\ArcTrellis.SmokeTests\ArcTrellis.SmokeTests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed." }
    }

    dotnet publish .\src\ArcTrellis.App\ArcTrellis.App.csproj -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:PublishReadyToRun=true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }

    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw "Inno Setup 6 is required to produce the installer. Install it with: winget install JRSoftware.InnoSetup"
    }

    New-Item -ItemType Directory -Force -Path $installerOutput | Out-Null
    & $iscc "/DAppPublishDir=$publish" ".\installer\ArcTrellis.iss"
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }

    $setup = Get-ChildItem $installerOutput -Filter "ArcTrellis-Setup-*-win-x64.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setup) { throw "Installer output was not found." }
    $hash = Get-FileHash $setup.FullName -Algorithm SHA256
    Set-Content -Path ($setup.FullName + ".sha256") -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $setup.Name)
    Write-Host "Installer ready: $($setup.FullName)"
    Write-Host "SHA-256: $($hash.Hash)"
}
finally { Pop-Location }
