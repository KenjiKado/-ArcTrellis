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

    $setup = Get-ChildItem $installerOutput -Filter "ArcTrellis-Setup-1.3.22-win-x64.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setup) { throw "Installer output was not found." }
    $hash = Get-FileHash $setup.FullName -Algorithm SHA256
    Set-Content -Path ($setup.FullName + ".sha256") -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $setup.Name)

    # Exercise the actual installer and installed executable on the Windows runner.
    $installTest = Join-Path $env:RUNNER_TEMP "ArcTrellis-Install-Test"
    if (Test-Path $installTest) { Remove-Item -Recurse -Force $installTest }
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "ArcTrellis.lnk"
    $desktopShortcutExistedBefore = Test-Path $desktopShortcut
    $installResult = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LANG=russian", "/DIR=$installTest" -Wait -PassThru
    if ($installResult.ExitCode -ne 0) { throw "Silent installer test failed with exit code $($installResult.ExitCode)." }
    $installedExe = Join-Path $installTest "ArcTrellis.exe"
    if (-not (Test-Path $installedExe)) { throw "The installer completed but ArcTrellis.exe was not installed." }
    if (-not (Test-Path $desktopShortcut)) { throw "A normal install did not create ArcTrellis.lnk on the desktop." }
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

    # Windows Explorer or another process can keep a shortcut memory mapped.
    # Reinstalling must not overwrite a shortcut that already targets the app.
    $shortcutMapping = [System.IO.MemoryMappedFiles.MemoryMappedFile]::CreateFromFile(
        $desktopShortcut, [System.IO.FileMode]::Open, "ArcTrellis-Shortcut-Upgrade-Smoke", 0, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    try {
        $mappedReinstall = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$installTest" -Wait -PassThru
        if ($mappedReinstall.ExitCode -ne 0) { throw "Reinstalling while the desktop shortcut is memory mapped failed (code $($mappedReinstall.ExitCode))." }
    }
    finally { $shortcutMapping.Dispose() }
    $mappedTarget = (New-Object -ComObject WScript.Shell).CreateShortcut($desktopShortcut).TargetPath
    if ([IO.Path]::GetFullPath($mappedTarget) -ne [IO.Path]::GetFullPath($installedExe)) { throw "The mapped desktop shortcut changed its target during reinstall." }

    # Reinstalling over an existing directory must repair a deleted shortcut.
    Remove-Item $desktopShortcut -Force
    $repairResult = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$installTest" -Wait -PassThru
    if ($repairResult.ExitCode -ne 0 -or -not (Test-Path $desktopShortcut)) { throw "Reinstalling in the same directory did not repair the desktop shortcut." }
    $repairedTarget = (New-Object -ComObject WScript.Shell).CreateShortcut($desktopShortcut).TargetPath
    if ([IO.Path]::GetFullPath($repairedTarget) -ne [IO.Path]::GetFullPath($installedExe)) { throw "The repaired desktop shortcut targets the wrong executable." }
    $uninstaller = Join-Path $installTest "unins000.exe"
    if (Test-Path $uninstaller) {
        $uninstallResult = Start-Process $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
        if ($uninstallResult.ExitCode -ne 0) { throw "Silent uninstall test failed with exit code $($uninstallResult.ExitCode)." }
    }
    if (-not $desktopShortcutExistedBefore -and (Test-Path $desktopShortcut)) { throw "Uninstall did not remove the ArcTrellis desktop shortcut." }

    # The additional-tasks checkbox must also be able to suppress the shortcut.
    $optOutDir = Join-Path $env:RUNNER_TEMP "ArcTrellis-Shortcut-Opt-Out-Test"
    if (Test-Path $optOutDir) { Remove-Item -Recurse -Force $optOutDir }
    $optOutResult = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/TASKS=!desktopicon", "/DIR=$optOutDir" -Wait -PassThru
    if ($optOutResult.ExitCode -ne 0 -or -not (Test-Path (Join-Path $optOutDir "ArcTrellis.exe"))) { throw "Desktop-shortcut opt-out installation failed." }
    if (-not $desktopShortcutExistedBefore -and (Test-Path $desktopShortcut)) { throw "Unchecking the desktop shortcut task still created a shortcut." }

    # An upgrade must be able to create a shortcut even if the previous install skipped it.
    $upgradeResult = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$optOutDir" -Wait -PassThru
    if ($upgradeResult.ExitCode -ne 0 -or -not (Test-Path $desktopShortcut)) { throw "Reinstalling over a shortcut-free installation did not create the selected shortcut." }
    $upgradeTarget = (New-Object -ComObject WScript.Shell).CreateShortcut($desktopShortcut).TargetPath
    if ([IO.Path]::GetFullPath($upgradeTarget) -ne [IO.Path]::GetFullPath((Join-Path $optOutDir "ArcTrellis.exe"))) { throw "The upgrade shortcut targets the wrong executable." }
    $optOutUninstall = Start-Process (Join-Path $optOutDir "unins000.exe") -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
    if ($optOutUninstall.ExitCode -ne 0) { throw "Shortcut opt-out uninstall failed." }
    if (-not $desktopShortcutExistedBefore -and (Test-Path $desktopShortcut)) { throw "Uninstall after upgrading a shortcut-free installation left its shortcut behind." }

    # In administrative mode the shortcut must be on the shared desktop, visible to every user.
    $sharedDesktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) "ArcTrellis.lnk"
    $sharedShortcutExistedBefore = Test-Path $sharedDesktopShortcut
    $allUsersDir = Join-Path $env:RUNNER_TEMP "ArcTrellis-All-Users-Install-Test"
    if (Test-Path $allUsersDir) { Remove-Item -Recurse -Force $allUsersDir }
    $allUsersResult = Start-Process $setup.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/ALLUSERS", "/DIR=$allUsersDir" -Wait -PassThru
    if ($allUsersResult.ExitCode -ne 0) { throw "All-users installation failed with exit code $($allUsersResult.ExitCode)." }
    $allUsersExe = Join-Path $allUsersDir "ArcTrellis.exe"
    if (-not (Test-Path $allUsersExe) -or -not (Test-Path $sharedDesktopShortcut)) { throw "All-users installation did not create the shared desktop shortcut." }
    $sharedShortcutTarget = (New-Object -ComObject WScript.Shell).CreateShortcut($sharedDesktopShortcut).TargetPath
    if ([IO.Path]::GetFullPath($sharedShortcutTarget) -ne [IO.Path]::GetFullPath($allUsersExe)) { throw "The shared desktop shortcut does not target the all-users installation." }
    $allUsersUninstall = Start-Process (Join-Path $allUsersDir "unins000.exe") -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
    if ($allUsersUninstall.ExitCode -ne 0 -or (-not $sharedShortcutExistedBefore -and (Test-Path $sharedDesktopShortcut))) { throw "All-users uninstall did not remove the shared desktop shortcut." }
    Write-Host "Install, optional desktop shortcut in both install modes, launch, persisted-language reopen, and uninstall smoke tests passed."
    Write-Host "Installer ready: $($setup.FullName)"
    Write-Host "SHA-256: $($hash.Hash)"
}
finally { Pop-Location }
