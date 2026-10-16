param(
    [switch]$SkipRestore,
    [switch]$Soak,
    [switch]$HardwareTone,
    [string]$ArtifactsDir = "artifacts/phase5.3"
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $taskRoot
try {
    $fullArtifactsDir = Join-Path $taskRoot $ArtifactsDir
    New-Item -ItemType Directory -Path $fullArtifactsDir -Force | Out-Null
    
    $timestamp = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    $commit = git rev-parse HEAD
    $branch = git branch --show-current

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " antiDBT Phase 5.3 Unified Verification Suite" -ForegroundColor Cyan
    Write-Host " Commit: $commit ($branch)" -ForegroundColor Gray
    Write-Host " Output: $fullArtifactsDir" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Cyan

    # 1. Build Solution
    Write-Host "`n[1/8] Compiling AntiDbt.sln..." -ForegroundColor Yellow
    $buildArgs = @('build', 'AntiDbt.sln', '-m:1', '-p:UseSharedCompilation=false')
    if ($SkipRestore) { $buildArgs += '--no-restore' }
    & dotnet @buildArgs *> (Join-Path $fullArtifactsDir "build.log")
    if ($LASTEXITCODE -ne 0) { throw "Build failed; inspect $fullArtifactsDir/build.log" }
    Write-Host "  -> Build succeeded." -ForegroundColor Green

    $suiteResults = [System.Collections.Generic.List[PSCustomObject]]::new()

    function Run-Suite {
        param(
            [string]$Name,
            [string]$Category,
            [string]$ExePath,
            [string[]]$Arguments,
            [string]$LogName,
            [int]$TimeoutSeconds = 60
        )
        Write-Host "Running $Name [$Category]..." -ForegroundColor Yellow
        $logFile = Join-Path $fullArtifactsDir $LogName
        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = "dotnet"
        $allArgs = @($ExePath) + $Arguments
        $psi.Arguments = [string]::Join(' ', $allArgs)
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $proc = [System.Diagnostics.Process]::Start($psi)
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()

        $completed = $proc.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            Write-Host "  -> $Name TIMEOUT after ${TimeoutSeconds}s! Terminating process tree..." -ForegroundColor Red
            try { & taskkill.exe /PID $proc.Id /T /F | Out-Null } catch { $proc.Kill($true) }
            $sw.Stop()
            $timeoutMsg = "Suite $Name timed out after $TimeoutSeconds seconds"
            Set-Content -LiteralPath $logFile -Value $timeoutMsg
            $suiteResults.Add([PSCustomObject]@{
                Name = $Name
                Category = $Category
                Passed = $false
                ExitCode = -1
                DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 3)
                Log = $LogName
            })
            throw $timeoutMsg
        }

        $sw.Stop()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        Set-Content -LiteralPath $logFile -Value ($stdout + "`n" + $stderr)
        $exitCode = $proc.ExitCode

        $passed = ($exitCode -eq 0)
        $color = if ($passed) { [ConsoleColor]::Green } else { [ConsoleColor]::Red }
        Write-Host "  -> $($Name): $(if ($passed) {'PASS'} else {'FAIL (code ' + $exitCode + ')'}) ($([Math]::Round($sw.Elapsed.TotalSeconds, 2))s)" -ForegroundColor $color

        $suiteResults.Add([PSCustomObject]@{
            Name = $Name
            Category = $Category
            Passed = $passed
            ExitCode = $exitCode
            DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 3)
            Log = $LogName
        })

        if (-not $passed) {
            throw "Suite $Name failed with exit code $exitCode. Log: $logFile"
        }
    }

    # 2. Core Unit & Contract Tests
    Run-Suite -Name "Core" -Category "OfflineUnit" -ExePath "tests/AntiDbt.Core.Tests/bin/Debug/net9.0/AntiDbt.Core.Tests.dll" -Arguments @() -LogName "core.log"

    # 3. Windows Audio & DPAPI Tests
    Run-Suite -Name "Windows" -Category "PlatformWindows" -ExePath "tests/AntiDbt.Windows.Tests/bin/Debug/net9.0-windows/AntiDbt.Windows.Tests.dll" -Arguments @() -LogName "windows.log"

    # 4. CloudSpeech SDK Contracts & Native Smoke
    Run-Suite -Name "CloudSpeech" -Category "SdkContracts" -ExePath "tests/AntiDbt.CloudSpeech.Tests/bin/Debug/net9.0/AntiDbt.CloudSpeech.Tests.dll" -Arguments @() -LogName "cloudspeech.log"
    Run-Suite -Name "CloudSpeech.NativeSmoke" -Category "SdkNativeLoad" -ExePath "tests/AntiDbt.CloudSpeech.Tests/bin/Debug/net9.0/AntiDbt.CloudSpeech.Tests.dll" -Arguments @('--native-smoke') -LogName "cloudspeech-native-smoke.log"

    # 5. Providers & Localhost Protocol Tests
    Run-Suite -Name "Providers" -Category "ProviderContracts" -ExePath "tests/AntiDbt.Providers.Tests/bin/Debug/net9.0/AntiDbt.Providers.Tests.dll" -Arguments @() -LogName "providers.log"
    Run-Suite -Name "Providers.Multi" -Category "ProviderMockFactory" -ExePath "tests/AntiDbt.Providers.Multi.Tests/bin/Debug/net9.0/AntiDbt.Providers.Multi.Tests.dll" -Arguments @() -LogName "providers-multi.log"
    Run-Suite -Name "Providers.Multi.Localhost" -Category "LocalhostCombinations" -ExePath "tests/AntiDbt.Providers.Multi.Tests/bin/Debug/net9.0/AntiDbt.Providers.Multi.Tests.dll" -Arguments @('--localhost') -LogName "providers-multi-localhost.log"

    # 6. Language & Domestic Integration Tests
    Run-Suite -Name "Language" -Category "LanguageRegistry" -ExePath "tests/AntiDbt.Language.Tests/bin/Debug/net9.0/AntiDbt.Language.Tests.dll" -Arguments @() -LogName "language.log"
    Run-Suite -Name "Domestic" -Category "DomesticLoopback" -ExePath "tests/AntiDbt.Domestic.Tests/bin/Debug/net9.0/AntiDbt.Domestic.Tests.dll" -Arguments @() -LogName "domestic.log"
    Run-Suite -Name "Domestic.Offline" -Category "DomesticOffline" -ExePath "tests/AntiDbt.Domestic.Tests/bin/Debug/net9.0/AntiDbt.Domestic.Tests.dll" -Arguments @('--offline') -LogName "domestic-offline.log"

    # 7. WPF Desktop Smoke Test
    Write-Host "Running Desktop.Smoke [WpfAutomation]..." -ForegroundColor Yellow
    $desktopDir = Join-Path $fullArtifactsDir "desktop-smoke"
    New-Item -ItemType Directory -Path $desktopDir -Force | Out-Null
    $desktopExe = "$taskRoot/src/AntiDbt.Desktop/bin/Debug/net9.0-windows/antiDBT.exe"
    $desktopLog = Join-Path $desktopDir "desktop-console.log"
    $desktopSw = [System.Diagnostics.Stopwatch]::StartNew()

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $desktopExe
    $psi.Arguments = "--smoke-test `"$desktopDir`""
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $desktopProc = [System.Diagnostics.Process]::Start($psi)
    $stdoutTask = $desktopProc.StandardOutput.ReadToEndAsync()
    $stderrTask = $desktopProc.StandardError.ReadToEndAsync()

    # Bounded wait with 60s timeout
    $timeoutMs = 60000
    $completed = $desktopProc.WaitForExit($timeoutMs)
    if (-not $completed) {
        Write-Host "  -> Desktop.Smoke TIMEOUT after $($timeoutMs/1000)s! Terminating process tree..." -ForegroundColor Red
        try { & taskkill.exe /PID $desktopProc.Id /T /F | Out-Null } catch { $desktopProc.Kill($true) }
        $desktopSw.Stop()
        $timeoutMsg = "Desktop Smoke timed out after $($timeoutMs/1000) seconds"
        Set-Content -LiteralPath (Join-Path $desktopDir "desktop-smoke-error.txt") -Value $timeoutMsg
        $suiteResults.Add([PSCustomObject]@{
            Name = "Desktop.Smoke"
            Category = "WpfAutomation"
            Passed = $false
            ExitCode = -1
            DurationSeconds = [Math]::Round($desktopSw.Elapsed.TotalSeconds, 3)
            Log = "desktop-smoke/desktop-smoke.json"
        })
        throw $timeoutMsg
    }

    $desktopSw.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Set-Content -LiteralPath $desktopLog -Value ($stdout + "`n" + $stderr)

    $desktopPassed = ($desktopProc.ExitCode -eq 0)
    $desktopColor = if ($desktopPassed) { [ConsoleColor]::Green } else { [ConsoleColor]::Red }
    Write-Host "  -> Desktop.Smoke: $(if ($desktopPassed) {'PASS'} else {'FAIL (code ' + $desktopProc.ExitCode + ')'}) ($([Math]::Round($desktopSw.Elapsed.TotalSeconds, 2))s)" -ForegroundColor $desktopColor
    $suiteResults.Add([PSCustomObject]@{
        Name = "Desktop.Smoke"
        Category = "WpfAutomation"
        Passed = $desktopPassed
        ExitCode = $desktopProc.ExitCode
        DurationSeconds = [Math]::Round($desktopSw.Elapsed.TotalSeconds, 3)
        Log = "desktop-smoke/desktop-smoke.json"
    })
    if (-not $desktopPassed) {
        $errFile = Join-Path $desktopDir "desktop-smoke-error.txt"
        $errDetails = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { $stderr }
        Write-Host "  Desktop Smoke Error Details:`n$errDetails" -ForegroundColor Red
        throw "Desktop Smoke failed with exit code $($desktopProc.ExitCode). Details: $errDetails"
    }

    # 8. Diagnostics & Optional Soak / Tone
    Run-Suite -Name "Diagnostics.Performance" -Category "SyntheticPerformance" -ExePath "tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll" -Arguments @() -LogName "diagnostics-performance.log"

    if ($Soak) {
        Run-Suite -Name "Diagnostics.Soak" -Category "SyntheticSoak" -ExePath "tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll" -Arguments @('--soak') -LogName "diagnostics-soak.log"
    }

    if ($HardwareTone) {
        Run-Suite -Name "Diagnostics.HardwareTone" -Category "HardwareAudioLoopback" -ExePath "tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll" -Arguments @('--tone-capture') -LogName "diagnostics-hardware-tone.log"
    }

    # Summary Report
    $summary = [PSCustomObject]@{
        Timestamp = $timestamp
        Commit = $commit
        Branch = $branch
        TotalSuites = $suiteResults.Count
        PassedSuites = @($suiteResults | Where-Object { $_.Passed }).Count
        FailedSuites = @($suiteResults | Where-Object { -not $_.Passed }).Count
        TotalDurationSeconds = [Math]::Round(($suiteResults | Measure-Object -Property DurationSeconds -Sum).Sum, 3)
        Suites = $suiteResults
    }

    $summaryJson = $summary | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path $fullArtifactsDir "verification-summary.json") -Value $summaryJson -Encoding utf8

    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host " All $($suiteResults.Count) Verification Suites Passed Successfully!" -ForegroundColor Green
    Write-Host " Summary: $(Join-Path $fullArtifactsDir 'verification-summary.json')" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Cyan

} finally {
    Pop-Location
}
