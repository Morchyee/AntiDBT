param(
    [string]$Version = "0.5.5",
    [string]$OutputZip = "artifacts/dist/antiDBT-v0.5.5-win-x64-full.zip",
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
    $fullZipPath = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputZip))
    $distDir = [IO.Path]::GetDirectoryName($fullZipPath)
    $stagingRoot = Join-Path $taskRoot "artifacts/staging-v0.5.5"
    $destination = Join-Path $stagingRoot "antiDBT"

    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " antiDBT v$Version Official Stable Package Builder" -ForegroundColor Cyan
    Write-Host " Version:    $Version" -ForegroundColor Gray
    Write-Host " Staging:    $destination" -ForegroundColor Gray
    Write-Host " Output ZIP: $fullZipPath" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Cyan

    $dotnetCmd = if (Test-Path "$taskRoot/.dotnet/dotnet.exe") { "$taskRoot/.dotnet/dotnet.exe" } else { "dotnet" }
    $desktopProjXml = [xml](Get-Content (Join-Path $taskRoot 'src/AntiDbt.Desktop/AntiDbt.Desktop.csproj'))
    $tfmWin = $desktopProjXml.Project.PropertyGroup.TargetFramework
    if ([string]::IsNullOrWhiteSpace($tfmWin)) { $tfmWin = "net10.0-windows" }

    # 1. Restore
    Write-Host "`n[1/6] Restoring desktop project dependencies (win-x64)..." -ForegroundColor Yellow
    & $dotnetCmd restore src/AntiDbt.Desktop/AntiDbt.Desktop.csproj -r win-x64 -m:1
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for win-x64" }

    # 2. Publish self-contained executable
    Write-Host "`n[2/6] Publishing self-contained Release build (target: $tfmWin)..." -ForegroundColor Yellow
    $publishArgs = @(
        'publish', 'src/AntiDbt.Desktop/AntiDbt.Desktop.csproj',
        '--no-restore',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=false',
        "-p:Version=$Version",
        "-p:AssemblyVersion=0.5.5.0",
        "-p:FileVersion=0.5.5.0",
        "-p:InformationalVersion=$Version",
        '-p:DebugType=None',
        '-p:UseSharedCompilation=false',
        '-m:1',
        '-o', $destination
    )
    & $dotnetCmd @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    # 3. Copy Notices, Licenses, Documentation & Models
    Write-Host "`n[3/6] Bundling documentation, licenses, and models..." -ForegroundColor Yellow
    $readmes = @('README.md', 'README.en.md', 'README.zh-TW.md', 'README.ja.md') | Where-Object { Test-Path -LiteralPath (Join-Path $taskRoot $_) }
    foreach ($r in $readmes) {
        Copy-Item -LiteralPath (Join-Path $taskRoot $r) -Destination $destination -Force
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $destination -Force

    if (Test-Path -LiteralPath (Join-Path $taskRoot 'licenses')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses') -Destination $destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path (Join-Path $destination 'docs') -Force | Out-Null
    foreach ($doc in @('API_PROVIDERS.md', 'ARCHITECTURE.md', 'PROVIDER-SPECS.md', 'COMPLETION-MATRIX.md', 'VALIDATION.md', 'CODEX_HANDOFF.md', 'LOCAL_ASR_SETUP.zh-CN.md')) {
        $p = Join-Path (Join-Path $taskRoot 'docs') $doc
        if (Test-Path -LiteralPath $p) {
            Copy-Item -LiteralPath $p -Destination (Join-Path $destination 'docs') -Force
        }
    }

    # Bundle the official default local English model: sherpa-onnx-streaming-zipformer-en-2023-06-21
    $modelName = 'sherpa-onnx-streaming-zipformer-en-2023-06-21'
    $modelSource = Join-Path $taskRoot "models/$modelName"
    if (-not (Test-Path -LiteralPath $modelSource)) {
        throw "Model source directory missing: $modelSource"
    }

    $destModelDir = Join-Path $destination "models/$modelName"
    New-Item -ItemType Directory -Path $destModelDir -Force | Out-Null

    $requiredModelFiles = @(
        'encoder-epoch-99-avg-1.int8.onnx',
        'decoder-epoch-99-avg-1.int8.onnx',
        'joiner-epoch-99-avg-1.int8.onnx',
        'tokens.txt'
    )
    foreach ($f in $requiredModelFiles) {
        $srcFile = Join-Path $modelSource $f
        if (-not (Test-Path -LiteralPath $srcFile)) { throw "Required model file missing: $srcFile" }
        Copy-Item -LiteralPath $srcFile -Destination $destModelDir -Force
    }
    if (Test-Path -LiteralPath (Join-Path $modelSource 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $modelSource 'README.md') -Destination $destModelDir -Force
    }
    Write-Host "  -> Bundled default local ASR model: $modelName" -ForegroundColor Green

    # Generate DEPENDENCIES.json metadata
    $assetsPath = "src/AntiDbt.Desktop/obj/project.assets.json"
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

    # 4. Invariant checks
    Write-Host "`n[4/6] Verifying bundle invariants..." -ForegroundColor Yellow
    $exePath = Join-Path $destination 'antiDBT.exe'
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Primary executable missing: $exePath" }

    $nativeDll = Join-Path $destination 'Microsoft.CognitiveServices.Speech.core.dll'
    if (-not (Test-Path -LiteralPath $nativeDll)) { throw "Required native runtime missing: $nativeDll" }

    $sherpaDll = Join-Path $destination 'sherpa-onnx-c-api.dll'
    if (-not (Test-Path -LiteralPath $sherpaDll)) { throw "Missing sherpa-onnx-c-api.dll: $sherpaDll" }

    $onnxDll = Join-Path $destination 'onnxruntime.dll'
    if (-not (Test-Path -LiteralPath $onnxDll)) { throw "Missing onnxruntime.dll: $onnxDll" }

    foreach ($f in $requiredModelFiles) {
        $p = Join-Path $destModelDir $f
        if (-not (Test-Path -LiteralPath $p)) { throw "Bundled model file missing: $p" }
        if ((Get-Item -LiteralPath $p).Length -eq 0) { throw "Bundled model file is empty: $p" }
    }

    $userDataDir = Join-Path $destination 'data'
    if (Test-Path -LiteralPath $userDataDir) { throw "Portable package must not contain user data: $userDataDir" }

    # 5. Execute smoke test on candidate binary
    if (-not $SkipSmoke) {
        Write-Host "`n[5/6] Executing smoke test on published binary..." -ForegroundColor Yellow
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
    Write-Host "`n[6/6] Computing SHA256 checksums and building archive with top-level antiDBT/ directory..." -ForegroundColor Yellow
    $exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash
    $nativeDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $nativeDll).Hash
    $sherpaDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sherpaDll).Hash
    
    if (Test-Path -LiteralPath $fullZipPath) { Remove-Item -LiteralPath $fullZipPath -Force }
    $retry = 0
    while ($true) {
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $fullZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
            break
        } catch {
            if ($retry++ -ge 5) { throw }
            Start-Sleep -Seconds 1
        }
    }
    $zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullZipPath).Hash
    $zipBytes = (Get-Item -LiteralPath $fullZipPath).Length
    Write-Host "  -> Archive created: $fullZipPath ($zipBytes bytes)" -ForegroundColor Green

    $zipFileName = Split-Path $fullZipPath -Leaf
    $checksumContent = @"
$zipHash *$zipFileName
$exeHash *antiDBT.exe
"@.Trim()

    $checksumFile = [IO.Path]::ChangeExtension($fullZipPath, ".sha256")
    Set-Content -LiteralPath $checksumFile -Value $checksumContent -Encoding utf8

    $sdkVersion = (& $dotnetCmd --version).Trim()
    $gitCommit = (git rev-parse HEAD)
    $gitBranch = (git branch --show-current)
    $manifest = [PSCustomObject]@{
        Version = $Version
        Commit = $gitCommit
        Branch = $gitBranch
        BuildTime = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        TargetFramework = $tfmWin
        DotnetSdkVersion = $sdkVersion
        RuntimeIdentifier = "win-x64"
        ExecutableHash = $exeHash
        ZipHash = $zipHash
        ZipBytes = $zipBytes
        ZipPath = $fullZipPath
        ChecksumFile = $checksumFile
        BundledModel = $modelName
        TopLevelFolder = "antiDBT"
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path $distDir "release-manifest.json") -Value $manifestJson -Encoding utf8
    Set-Content -LiteralPath (Join-Path $destination "release-manifest.json") -Value $manifestJson -Encoding utf8

    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " antiDBT v$Version Package Completed Successfully!" -ForegroundColor Green
    Write-Host " Staging:   $destination" -ForegroundColor Gray
    Write-Host " Archive:   $fullZipPath" -ForegroundColor Gray
    Write-Host " Size:      $zipBytes bytes" -ForegroundColor Gray
    Write-Host " EXE SHA256: $exeHash" -ForegroundColor Gray
    Write-Host " ZIP SHA256: $zipHash" -ForegroundColor Gray
    Write-Host " Checksums:  $checksumFile" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Green

} finally {
    Pop-Location
}
