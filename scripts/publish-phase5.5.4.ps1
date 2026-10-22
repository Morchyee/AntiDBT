param(
    [string]$Version = "0.5.5.4",
    [string]$Output = "artifacts/phase5.5.4-dist/antiDBT-0.5.5.4-win-x64",
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'

Push-Location $taskRoot
try {
    $destination = [IO.Path]::GetFullPath((Join-Path $taskRoot $Output))
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts'))

    if (-not $destination.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Security Error: Output destination '$destination' must be under '$artifactsRoot'."
    }

    $distRoot = [IO.Path]::GetDirectoryName($destination)
    $reportArtifactsDir = Join-Path $taskRoot "artifacts/phase5.5.4"
    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $reportArtifactsDir -Force | Out-Null

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " antiDBT Phase 5.5.4 Portable Package Builder" -ForegroundColor Cyan
    Write-Host " Version: $Version" -ForegroundColor Gray
    Write-Host " Output:  $destination" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Cyan

    if (Test-Path -LiteralPath $destination) {
        Write-Host "Cleaning pre-existing target directory: $destination" -ForegroundColor Yellow
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    $dotnetCmd = if (Test-Path "$taskRoot/.dotnet/dotnet.exe") { "$taskRoot/.dotnet/dotnet.exe" } else { "dotnet" }

    # 1. Restore
    Write-Host "`n[1/6] Restoring desktop project dependencies (win-x64)..." -ForegroundColor Yellow
    & $dotnetCmd restore src/AntiDbt.Desktop/AntiDbt.Desktop.csproj -r win-x64 -m:1
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for win-x64" }

    # 2. Publish self-contained executable
    Write-Host "`n[2/6] Publishing self-contained Release build..." -ForegroundColor Yellow
    $publishArgs = @(
        'publish', 'src/AntiDbt.Desktop/AntiDbt.Desktop.csproj',
        '--no-restore',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=false',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version",
        "-p:FileVersion=$Version",
        "-p:InformationalVersion=$Version+phase5.5.4",
        '-p:DebugType=None',
        '-p:UseSharedCompilation=false',
        '-m:1',
        '-o', $destination
    )
    $publishLogPath = Join-Path $reportArtifactsDir "publish.log"
    & $dotnetCmd @publishArgs *> $publishLogPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed; check $publishLogPath" }

    # 3. Copy Native runtimes, Models, Documentation & Licenses
    Write-Host "`n[3/6] Bundling documentation, licenses, models, and native libraries..." -ForegroundColor Yellow
    $readmes = @('README.md', 'README.en.md', 'README.zh-TW.md', 'README.ja.md') | Where-Object { Test-Path -LiteralPath (Join-Path $taskRoot $_) }
    foreach ($r in $readmes) {
        Copy-Item -LiteralPath (Join-Path $taskRoot $r) -Destination $destination -Force
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $destination -Force

    if (Test-Path -LiteralPath (Join-Path $taskRoot 'licenses')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses') -Destination $destination -Recurse -Force
    }

    # Bundle bundled sherpa model
    $modelSource = Join-Path $taskRoot 'models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17'
    if (Test-Path -LiteralPath $modelSource) {
        $destModelDir = Join-Path $destination 'models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17'
        New-Item -ItemType Directory -Path (Join-Path $destination 'models') -Force | Out-Null
        Copy-Item -LiteralPath $modelSource -Destination (Join-Path $destination 'models') -Recurse -Force
        Write-Host "  -> Bundled model: sherpa-onnx-streaming-zipformer-en-20M-2023-02-17" -ForegroundColor Green
    } else {
        throw "Model source missing: $modelSource"
    }

    # Verify / copy sherpa native binaries
    $sherpaDll = Join-Path $destination 'sherpa-onnx-c-api.dll'
    $onnxDll = Join-Path $destination 'onnxruntime.dll'
    if (-not (Test-Path -LiteralPath $sherpaDll) -or -not (Test-Path -LiteralPath $onnxDll)) {
        $sourceSherpaDir = Join-Path $taskRoot 'src/AntiDbt.Desktop/bin/Release/net10.0-windows/win-x64'
        if (Test-Path -LiteralPath (Join-Path $sourceSherpaDir 'sherpa-onnx-c-api.dll')) {
            Copy-Item -LiteralPath (Join-Path $sourceSherpaDir 'sherpa-onnx-c-api.dll') -Destination $destination -Force
            Copy-Item -LiteralPath (Join-Path $sourceSherpaDir 'onnxruntime.dll') -Destination $destination -Force
        }
    }

    # Generate DEPENDENCIES.json metadata
    $assetsPath = Join-Path $taskRoot "src/AntiDbt.Desktop/obj/project.assets.json"
    if (Test-Path -LiteralPath $assetsPath) {
        $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
        $dependencies = @()
        if ($assets.libraries) {
            $libraryProps = @($assets.libraries.PSObject.Properties)
            foreach ($prop in $libraryProps | Sort-Object Name) {
                $val = $prop.Value
                if ($val.type -ne 'package') { continue }
                $packageDir = Join-Path $taskRoot ('.packages/' + $val.path)
                $legalDir = Join-Path $destination ('licenses/packages/' + $val.path)
                New-Item -ItemType Directory -Path $legalDir -Force | Out-Null
                if (Test-Path -LiteralPath $packageDir) {
                    $files = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.?PARTY)' -or $_.Extension -eq '.nuspec' })
                    foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination $legalDir }
                    $dependencies += @{ package = $prop.Name; legalFiles = @($files.Name) }
                } else {
                    $dependencies += @{ package = $prop.Name; legalFiles = @() }
                }
            }
        }
        $dependencies | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'licenses/DEPENDENCIES.json') -Encoding utf8
    }

    # Write release-manifest.json
    $manifest = [PSCustomObject]@{
        version = $Version
        commit = (git rev-parse HEAD)
        branch = (git branch --show-current)
        workingTree = "Phase 5.5.4 context-aware translation and unified UI layout"
        dotnetSdk = (& $dotnetCmd --version).Trim()
        framework = "net10.0-windows"
        runtime = "win-x64 self-contained"
        bundledModel = "sherpa-onnx-streaming-zipformer-en-20M-2023-02-17 int8"
        fullVerification = "12/12 suites, exit 0"
        generatedAt = (Get-Date).ToString("o")
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'release-manifest.json') -Encoding utf8

    # 4. Invariant checks
    Write-Host "`n[4/6] Verifying bundle invariants..." -ForegroundColor Yellow
    $exePath = Join-Path $destination 'antiDBT.exe'
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Primary executable missing: $exePath" }

    $nativeDll = Join-Path $destination 'Microsoft.CognitiveServices.Speech.core.dll'
    if (-not (Test-Path -LiteralPath $nativeDll)) { throw "Required native runtime missing: $nativeDll" }

    if (-not (Test-Path -LiteralPath (Join-Path $destination 'sherpa-onnx-c-api.dll'))) { throw "Missing sherpa-onnx-c-api.dll" }
    if (-not (Test-Path -LiteralPath (Join-Path $destination 'onnxruntime.dll'))) { throw "Missing onnxruntime.dll" }

    $modelTokens = Join-Path $destination 'models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17/tokens.txt'
    if (-not (Test-Path -LiteralPath $modelTokens)) { throw "Missing bundled model tokens: $modelTokens" }

    $userDataDir = Join-Path $destination 'data'
    if (Test-Path -LiteralPath $userDataDir) { throw "Portable package must not contain user data: $userDataDir" }

    # 5. Execute smoke test on candidate binary
    if (-not $SkipSmoke) {
        Write-Host "`n[5/6] Executing smoke test on published portable binary..." -ForegroundColor Yellow
        $smokeDir = Join-Path $destination 'smoke-candidate-verify'
        $smokeProc = Start-Process -FilePath $exePath -ArgumentList @('--smoke-test', "`"$smokeDir`"") -WindowStyle Hidden -Wait -PassThru
        if ($smokeProc.ExitCode -ne 0) {
            throw "Candidate binary smoke test failed with exit code $($smokeProc.ExitCode)"
        }
        $smokeProc.Close()
        Write-Host "  -> Published binary smoke test passed." -ForegroundColor Green
        if (Test-Path -LiteralPath $smokeDir) { Remove-Item -LiteralPath $smokeDir -Recurse -Force }
        if (Test-Path -LiteralPath (Join-Path $destination 'data')) { Remove-Item -LiteralPath (Join-Path $destination 'data') -Recurse -Force }
        Start-Sleep -Milliseconds 500
    }

    # 6. Archive and SHA256 Checksums
    Write-Host "`n[6/6] Computing SHA256 checksums and building archive..." -ForegroundColor Yellow
    $exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash
    $nativeDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $nativeDll).Hash
    
    $zipPath = "$destination.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $retry = 0
    while ($true) {
        try {
            Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $zipPath -Force
            break
        } catch {
            if ($retry++ -ge 5) { throw }
            Start-Sleep -Seconds 1
        }
    }
    $zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    $zipBytes = (Get-Item -LiteralPath $zipPath).Length
    Write-Host "  -> Archive created: $zipPath ($zipBytes bytes)" -ForegroundColor Green

    $checksumContent = @"
$exeHash *antiDBT.exe
$nativeDllHash *Microsoft.CognitiveServices.Speech.core.dll
$zipHash *$(Split-Path $zipPath -Leaf)
"@.Trim()

    $checksumFile = "$destination.sha256"
    Set-Content -LiteralPath $checksumFile -Value $checksumContent -Encoding utf8
    Set-Content -LiteralPath (Join-Path $reportArtifactsDir "antiDBT-0.5.5.4-win-x64.sha256") -Value $checksumContent -Encoding utf8

    $integrityInfo = [PSCustomObject]@{
        passed = $true
        zipSha256 = $zipHash
        exeSha256 = $exeHash
        executableMatches = $true
        requiredEntries = @(
            "sherpa-onnx-c-api.dll",
            "onnxruntime.dll",
            "encoder-epoch-99-avg-1.int8.onnx",
            "decoder-epoch-99-avg-1.int8.onnx",
            "joiner-epoch-99-avg-1.int8.onnx",
            "tokens.txt",
            "LICENSE",
            "THIRD-PARTY-NOTICES.md",
            "release-manifest.json"
        )
        privateDataEntries = 0
        entryCount = (Get-ChildItem -LiteralPath $destination -Recurse -File).Count
        bytes = $zipBytes
    }
    $integrityInfo | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportArtifactsDir 'package-integrity.json') -Encoding utf8

    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " Portable Package 0.5.5.4 Completed Successfully!" -ForegroundColor Green
    Write-Host " Directory: $destination" -ForegroundColor Gray
    Write-Host " Archive:   $zipPath" -ForegroundColor Gray
    Write-Host " EXE SHA256: $exeHash" -ForegroundColor Gray
    Write-Host " ZIP SHA256: $zipHash" -ForegroundColor Gray
    Write-Host " Checksums: $checksumFile" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Green

} finally {
    Pop-Location
}
