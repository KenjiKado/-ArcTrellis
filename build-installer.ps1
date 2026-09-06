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

    $setup = Get-ChildItem $installerOutput -Filter "ArcTrellis-Setup-1.1.20-win-x64.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setup) { throw "Installer output was not found." }
    $hash = Get-FileHash $setup.FullName -Algorithm SHA256
    Set-Content -Path ($setup.FullName + ".sha256") -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $setup.Name)

    # Exercise the actual installer and installed executable on the Windows runner.
    $installTest = Join-Path $env:RUNNER_TEMP "ArcTrellis-Install-Test"
    if (Test-Path $installTest) { Remove-Item -Recurse -Force $installTest }
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "ArcTrellis.lnk"
    $desktopShortcutExistedBefore = Test-Path $desktopShortcut
    $installResult = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LANG=russian", "/DIR=$installTest", "/TASKS=desktopicon" -Wait -PassThru
    if ($installResult.ExitCode -ne 0) { throw "Silent installer test failed with exit code $($installResult.ExitCode)." }
    $installedExe = Join-Path $installTest "ArcTrellis.exe"
    if (-not (Test-Path $installedExe)) { throw "The installer completed but ArcTrellis.exe was not installed." }
    if (-not (Test-Path $desktopShortcut)) { throw "The desktop shortcut task was selected but ArcTrellis.lnk was not created." }
    $shortcutTarget = (New-Object -ComObject WScript.Shell).CreateShortcut($desktopShortcut).TargetPath
    if (-not (Test-Path $shortcutTarget) -or ([IO.Path]::GetFullPath($shortcutTarget) -ne [IO.Path]::GetFullPath($installedExe))) {
        throw "The desktop shortcut does not target the installed ArcTrellis executable."
    }
    $uiSmokeReport = Join-Path $installerOutput "ArcTrellis-UI-Smoke.txt"
    if (Test-Path $uiSmokeReport) { Remove-Item -Force $uiSmokeReport }
    $appProcess = Start-Process $installedExe -ArgumentList "--language=ru-RU", "--ui-smoke=$uiSmokeReport" -PassThru
    for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path $uiSmokeReport); $attempt++) { Start-Sleep -Seconds 1 }
    if ($appProcess.HasExited) { throw "The installed application exited during its launch smoke test (code $($appProcess.ExitCode))." }
    if (-not (Test-Path $uiSmokeReport)) { throw "The installed application did not complete its UI smoke test." }
    $uiSmokeResult = Get-Content $uiSmokeReport -Raw
    if (-not $uiSmokeResult.StartsWith("PASS")) { throw "Installed UI smoke test failed: $uiSmokeResult" }
    Stop-Process -Id $appProcess.Id -Force
    $reopenSmokeReport = Join-Path $installerOutput "ArcTrellis-Reopen-UI-Smoke.txt"
    if (Test-Path $reopenSmokeReport) { Remove-Item -Force $reopenSmokeReport }
    $reopenProcess = Start-Process $installedExe -ArgumentList "--ui-smoke=$reopenSmokeReport" -PassThru
    for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path $reopenSmokeReport); $attempt++) { Start-Sleep -Seconds 1 }
    if ($reopenProcess.HasExited) { throw "The installed application exited during its persisted-language reopen test (code $($reopenProcess.ExitCode))." }
    if (-not (Test-Path $reopenSmokeReport)) { throw "The installed application did not complete its persisted-language reopen test." }
    $reopenSmokeResult = Get-Content $reopenSmokeReport -Raw
    if (-not $reopenSmokeResult.StartsWith("PASS")) { throw "Installed reopen UI smoke test failed: $reopenSmokeResult" }
    Stop-Process -Id $reopenProcess.Id -Force
    $uninstaller = Join-Path $installTest "unins000.exe"
    if (Test-Path $uninstaller) {
        $uninstallResult = Start-Process $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
        if ($uninstallResult.ExitCode -ne 0) { throw "Silent uninstall test failed with exit code $($uninstallResult.ExitCode)." }
    }
    if (-not $desktopShortcutExistedBefore -and (Test-Path $desktopShortcut)) { throw "Uninstall did not remove the ArcTrellis desktop shortcut." }
    Write-Host "Install, desktop shortcut, launch, persisted-language reopen, and uninstall smoke test passed."
    Write-Host "Installer ready: $($setup.FullName)"
    Write-Host "SHA-256: $($hash.Hash)"
}
finally { Pop-Location }
