$exePath = "D:\Desktop\AntiDBT-Gemini\antiDBT\prototype\artifacts\phase5.3\dist\antiDBT-v0.5.3-beta-win-x64\antiDBT.exe"
$outDir = "D:\Desktop\AntiDBT-Gemini\antiDBT\prototype\artifacts\phase5.3-verification-live"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exePath
$psi.Arguments = "--smoke-test `"$outDir`""
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

Write-Host "Launching released binary: $exePath"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = [System.Diagnostics.Process]::Start($psi)

$samples = [System.Collections.Generic.List[PSCustomObject]]::new()
$prevCpuTime = [TimeSpan]::Zero
$prevSampleTime = [DateTime]::UtcNow

while (-not $proc.HasExited) {
    try {
        $proc.Refresh()
        $now = [DateTime]::UtcNow
        $cpuTime = $proc.TotalProcessorTime
        $timeDelta = ($now - $prevSampleTime).TotalMilliseconds
        $cpuPercent = 0.0
        if ($timeDelta -gt 0 -and $prevCpuTime -ne [TimeSpan]::Zero) {
            $cpuPercent = [Math]::Round((($cpuTime - $prevCpuTime).TotalMilliseconds / ($timeDelta * [Environment]::ProcessorCount)) * 100, 2)
        }
        $prevCpuTime = $cpuTime
        $prevSampleTime = $now

        $samples.Add([PSCustomObject]@{
            ElapsedMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 1)
            WorkingSetMB = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
            PrivateBytesMB = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
            PagedMemoryMB = [Math]::Round($proc.PagedMemorySize64 / 1MB, 2)
            ThreadCount = $proc.Threads.Count
            HandleCount = $proc.HandleCount
            CpuPercent = $cpuPercent
        })
    } catch {
        # Process might exit during sample
    }
    Start-Sleep -Milliseconds 400
}

$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$exitCode = $proc.ExitCode
$sw.Stop()

Write-Host "Process exited with code $exitCode after $([Math]::Round($sw.Elapsed.TotalSeconds, 2))s"

$metrics = [PSCustomObject]@{
    ExitCode = $exitCode
    TotalDurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 3)
    SampleCount = $samples.Count
    PeakWorkingSetMB = ($samples | Measure-Object -Property WorkingSetMB -Maximum).Maximum
    AvgWorkingSetMB = [Math]::Round(($samples | Measure-Object -Property WorkingSetMB -Average).Average, 2)
    FinalWorkingSetMB = if ($samples.Count -gt 0) { $samples[-1].WorkingSetMB } else { 0 }
    PeakPrivateBytesMB = ($samples | Measure-Object -Property PrivateBytesMB -Maximum).Maximum
    AvgPrivateBytesMB = [Math]::Round(($samples | Measure-Object -Property PrivateBytesMB -Average).Average, 2)
    PeakCpuPercent = ($samples | Measure-Object -Property CpuPercent -Maximum).Maximum
    AvgCpuPercent = [Math]::Round(($samples | Measure-Object -Property CpuPercent -Average).Average, 2)
    PeakThreadCount = ($samples | Measure-Object -Property ThreadCount -Maximum).Maximum
    PeakHandleCount = ($samples | Measure-Object -Property HandleCount -Maximum).Maximum
    Samples = $samples
}

$metricsJson = $metrics | ConvertTo-Json -Depth 4
Set-Content -LiteralPath "$outDir\process-telemetry.json" -Value $metricsJson -Encoding utf8
Write-Host "Telemetry recorded to $outDir\process-telemetry.json"
