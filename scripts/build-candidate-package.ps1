param(
    [string]$Version = "0.5.3-beta.1",
    [string]$Output = "artifacts/phase5.3/dist/antiDBT-v0.5.3-beta.1-win-x64",
    [switch]$SkipSmoke,
    [switch]$NoZip
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
    $allowedRoots = @(
        [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')),
        [IO.Path]::GetFullPath((Join-Path $taskRoot 'publish'))
    )

    $isAllowed = $false
    foreach ($root in $allowedRoots) {
        $rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($destination.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -and $destination.Length -gt $rootPrefix.Length) {
            $isAllowed = $true
            break
        }
    }

    if (-not $isAllowed) {
        throw "Security Error: Output destination '$destination' must be a strict subdirectory under '$taskRoot\artifacts' or '$taskRoot\publish'. Refusing to delete or write to an unconfined path."
    }

    $distRoot = [IO.Path]::GetDirectoryName($destination)
    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " antiDBT Phase 5.3 Candidate Package Builder" -ForegroundColor Cyan
    Write-Host " Version: $Version" -ForegroundColor Gray
    Write-Host " Output:  $destination" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Cyan

    if (Test-Path -LiteralPath $destination) {
        Write-Host "Cleaning pre-existing target directory: $destination" -ForegroundColor Yellow
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    # Detect dotnet executable and target framework from project evaluation
    $dotnetCmd = if (Test-Path "$taskRoot/.dotnet/dotnet.exe") { "$taskRoot/.dotnet/dotnet.exe" } else { "dotnet" }
    $desktopProjXml = [xml](Get-Content (Join-Path $taskRoot 'src/AntiDbt.Desktop/AntiDbt.Desktop.csproj'))
    $tfmWin = $desktopProjXml.Project.PropertyGroup.TargetFramework
    if ([string]::IsNullOrWhiteSpace($tfmWin)) { $tfmWin = "net10.0-windows" }

    # 1. Restore
    Write-Host "`n[1/6] Restoring desktop project dependencies (win-x64, using $dotnetCmd)..." -ForegroundColor Yellow
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
        "-p:AssemblyVersion=0.5.3.0",
        "-p:FileVersion=0.5.3.0",
        "-p:InformationalVersion=$Version+phase5.3-candidate",
        '-p:DebugType=None',
        '-p:UseSharedCompilation=false',
        '-m:1',
        '-o', $destination
    )
    & $dotnetCmd @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    # 3. Copy Notices, Licenses & Documentation
    Write-Host "`n[3/6] Bundling documentation, licenses, and metadata..." -ForegroundColor Yellow
    $readmes = @('README.md', 'README.en.md', 'README.zh-TW.md', 'README.ja.md') | Where-Object { Test-Path -LiteralPath $_ }
    Copy-Item -LiteralPath ($readmes + @('THIRD-PARTY-NOTICES.md', 'LICENSE')) -Destination $destination -Force
    if (Test-Path -LiteralPath licenses) {
        Copy-Item -LiteralPath licenses -Destination $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $destination 'docs') -Force | Out-Null
    foreach ($doc in @('API_PROVIDERS.md', 'ARCHITECTURE.md', 'PROVIDER-SPECS.md', 'COMPLETION-MATRIX.md', 'VALIDATION.md', 'CODEX_HANDOFF.md')) {
        $p = Join-Path docs $doc
        if (Test-Path -LiteralPath $p) {
            Copy-Item -LiteralPath $p -Destination (Join-Path $destination 'docs') -Force
        }
    }
    if (Test-Path -LiteralPath docs/phase5) {
        Copy-Item -LiteralPath docs/phase5 -Destination (Join-Path $destination 'docs') -Recurse -Force
    }

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

    $userDataDir = Join-Path $destination 'data'
    if (Test-Path -LiteralPath $userDataDir) { throw "Portable package must not contain user data: $userDataDir" }

    # 5. Execute smoke test on candidate binary
    if (-not $SkipSmoke) {
        Write-Host "`n[5/6] Executing smoke test on published candidate binary..." -ForegroundColor Yellow
        $smokeDir = Join-Path $destination 'smoke-candidate-verify'
        $smokeProc = Start-Process -FilePath $exePath -ArgumentList @('--smoke-test', "`"$smokeDir`"") -WindowStyle Hidden -Wait -PassThru
        if ($smokeProc.ExitCode -ne 0) {
            throw "Candidate binary smoke test failed with exit code $($smokeProc.ExitCode)"
        }
        $smokeProc.Close()
        Write-Host "  -> Published binary smoke test passed." -ForegroundColor Green
        if (Test-Path -LiteralPath $smokeDir) { Remove-Item -LiteralPath $smokeDir -Recurse -Force }
        if (Test-Path -LiteralPath (Join-Path $destination 'data')) { Remove-Item -LiteralPath (Join-Path $destination 'data') -Recurse -Force }
        Start-Sleep -Milliseconds 800
    }

    # 6. Archive and SHA256 Checksums
    Write-Host "`n[6/6] Computing SHA256 checksums and building archive..." -ForegroundColor Yellow
    $exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash
    $nativeDllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $nativeDll).Hash
    
    $zipPath = "$destination.zip"
    $zipHash = $null
    if (-not $NoZip) {
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
        Write-Host "  -> Archive created: $zipPath" -ForegroundColor Green
    }

    $checksumContent = @"
$exeHash *antiDBT.exe
$nativeDllHash *Microsoft.CognitiveServices.Speech.core.dll
$(if ($zipHash) { "$zipHash *$(Split-Path $zipPath -Leaf)" })
"@.Trim()

    $checksumFile = "$destination.sha256"
    Set-Content -LiteralPath $checksumFile -Value $checksumContent -Encoding utf8

    $sdkVersion = (& $dotnetCmd --version).Trim()
    $gitDirty = (git status --porcelain).Length -gt 0
    $manifest = [PSCustomObject]@{
        Version = $Version
        Commit = (git rev-parse HEAD)
        Branch = (git branch --show-current)
        WorkingTreeState = if ($gitDirty) { "Dirty" } else { "Clean" }
        BuildTime = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        TargetFramework = $tfmWin
        DotnetSdkVersion = $sdkVersion
        RuntimeIdentifier = "win-x64"
        ExecutableHash = $exeHash
        NativeSpeechDllHash = $nativeDllHash
        ZipHash = $zipHash
        ExecutablePath = $exePath
        ZipPath = $zipPath
        ChecksumFile = $checksumFile
        Disclaimer = "Phase 5.3 candidate portable package. This build has not been promoted to production GitHub Release."
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path $distRoot "candidate-manifest.json") -Value $manifestJson -Encoding utf8
    Set-Content -LiteralPath (Join-Path $destination "candidate-manifest.json") -Value $manifestJson -Encoding utf8

    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " Candidate Package Completed Successfully!" -ForegroundColor Green
    Write-Host " Directory: $destination" -ForegroundColor Gray
    Write-Host " Archive:   $zipPath" -ForegroundColor Gray
    Write-Host " EXE SHA256: $exeHash" -ForegroundColor Gray
    if ($zipHash) { Write-Host " ZIP SHA256: $zipHash" -ForegroundColor Gray }
    Write-Host " Manifest:  $(Join-Path $distRoot 'candidate-manifest.json')" -ForegroundColor Gray
    Write-Host "==========================================================" -ForegroundColor Green

} finally {
    Pop-Location
}
