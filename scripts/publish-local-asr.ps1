param(
    [string]$Output = 'artifacts/release-0.5.5.4-beta/dist',
    [string]$VerificationDirectory = 'artifacts/release-0.5.5.4-beta/verification'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    $dist = [IO.Path]::GetFullPath((Join-Path $taskRoot $Output))
    $prefix = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $dist.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be under artifacts/' }
    if (Test-Path -LiteralPath $dist) { throw 'Use a fresh output directory; existing artifacts are preserved' }
    $version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
    $commit = (git rev-parse HEAD).Trim()
    git diff --quiet HEAD -- src tests Directory.Build.props scripts docs README.md THIRD-PARTY-NOTICES.md licenses
    if ($LASTEXITCODE -ne 0) { throw 'Commit release source before packaging' }
    $verification = Get-Content (Join-Path $VerificationDirectory 'verification-summary.json') -Raw | ConvertFrom-Json
    if ($verification.Commit -ne $commit -or $verification.FailedSuites -ne 0 -or $verification.TotalSuites -lt 12) { throw 'Fresh complete verification of this commit is required' }
    $dotnet = if (Test-Path .dotnet/dotnet.exe) { Join-Path $taskRoot '.dotnet/dotnet.exe' } else { 'dotnet' }
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $package = Join-Path $dist 'antiDBT'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    & $dotnet publish src/AntiDbt.Desktop/AntiDbt.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=None -p:UseSharedCompilation=false -m:1 -o $package *> (Join-Path $dist 'publish.log')
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; see publish.log' }
    foreach ($file in @('README.md','README.en.md','README.zh-TW.md','README.ja.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath $file -Destination $package }
    New-Item -ItemType Directory -Path (Join-Path $package 'docs') -Force | Out-Null
    Copy-Item docs/LOCAL_ASR_SETUP.zh-CN.md (Join-Path $package 'docs')
    Copy-Item docs/LOCAL_ASR_SETUP.zh-CN.md (Join-Path $package '本地ASR部署指南.md')
    Copy-Item -LiteralPath licenses -Destination $package -Recurse
    $model = 'sherpa-onnx-streaming-zipformer-en-2023-06-21'
    $modelTarget = Join-Path $package "models/$model"
    New-Item -ItemType Directory -Path $modelTarget -Force | Out-Null
    $sources = Get-Content licenses/upstream/ZIPFORMER-2023-06-21-SOURCES.json -Raw | ConvertFrom-Json
    foreach ($file in $sources.files) {
        $source = Join-Path "models/$model" $file.file
        if ((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "Model hash mismatch: $source" }
        Copy-Item -LiteralPath $source -Destination $modelTarget
    }
    # Preserve exact resolved NuGet metadata and any licenses/notices shipped by packages.
    $assets = Get-Content src/AntiDbt.Desktop/obj/project.assets.json -Raw | ConvertFrom-Json
    $dependencies = foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $source = Join-Path '.packages' $library.Value.path
        $target = Join-Path $package ('licenses/packages/' + $library.Value.path)
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $legal = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -match 'LICENSE|NOTICE|\.nuspec$' })
        foreach ($file in $legal) { Copy-Item -LiteralPath $file.FullName -Destination $target }
        @{package=$library.Name; legalFiles=@($legal.Name)}
    }
    $dependencies | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $package 'licenses/DEPENDENCIES.json') -Encoding utf8
    foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $runtimeRoot = Get-ChildItem ".packages/$runtime" -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if (-not $runtimeRoot) { throw "Runtime legal files unavailable: $runtime" }
        $target = Join-Path $package "licenses/$runtime"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Get-ChildItem $runtimeRoot.FullName -File | Where-Object Name -Match 'LICENSE|NOTICE' | Copy-Item -Destination $target
    }
    foreach ($file in @('antiDBT.exe','sherpa-onnx-c-api.dll','onnxruntime.dll','Microsoft.CognitiveServices.Speech.core.dll','docs/LOCAL_ASR_SETUP.zh-CN.md','licenses/upstream/SHERPA-ONNX-APACHE-2.0.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $package $file))) { throw "Missing required file: $file" }
    }
    $forbidden = @(Get-ChildItem $package -Recurse -File | Where-Object { $_.Extension -in @('.wav','.log','.pdb') -or $_.Name -eq 'settings.json' -or $_.FullName -match '[\\/]data[\\/]' })
    if ($forbidden.Count) { throw 'Private/development data detected in bundle' }
    @{ version=$version; commit=$commit; dotnetSdk=(& $dotnet --version).Trim(); runtime='win-x64 self-contained'; framework='net10.0-windows'; bundledModel=$model; verificationSuites=$verification.TotalSuites; verificationPassed=$verification.PassedSuites } | ConvertTo-Json | Set-Content (Join-Path $package 'release-manifest.json') -Encoding utf8
    $exe = Join-Path $package 'antiDBT.exe'
    $smoke = Join-Path $dist 'package smoke with spaces'
    $proc = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', "`"$smoke`"") -WorkingDirectory $dist -WindowStyle Hidden -PassThru
    if (-not $proc.WaitForExit(90000)) { $proc.Kill($true); throw 'Package smoke timed out' }
    if ($proc.ExitCode -ne 0) { throw 'Package smoke failed' }
    # Smoke uses testMode and must not create persistent data.
    if (Test-Path (Join-Path $package 'data')) { throw 'Smoke wrote user data into bundle' }
    $zip = Join-Path $dist "antiDBT-v$version-win-x64-full.zip"
    Compress-Archive -LiteralPath $package -DestinationPath $zip
    $exeHash = (Get-FileHash $exe).Hash
    $zipHash = (Get-FileHash $zip).Hash
    @("$zipHash *$([IO.Path]::GetFileName($zip))", "$exeHash *antiDBT/antiDBT.exe") | Set-Content (Join-Path $dist "antiDBT-v$version-win-x64-full.sha256") -Encoding utf8
    # Verify every ZIP file byte-for-byte against the staging tree.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $count = 0
        foreach ($entry in $archive.Entries) {
            if (-not $entry.FullName.StartsWith('antiDBT/')) { throw 'ZIP must have one antiDBT/ root' }
            if (-not $entry.Name) { continue }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($hash -ne (Get-FileHash (Join-Path $dist $entry.FullName)).Hash) { throw "ZIP mismatch: $($entry.FullName)" }
            $count++
        }
        @{passed=$true; fileCount=$count; zipSha256=$zipHash; exeSha256=$exeHash; zipBytes=(Get-Item $zip).Length; commit=$commit; modelFiles=$sources.files; smokeExitCode=$proc.ExitCode} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $dist 'package-integrity.json') -Encoding utf8
    } finally { $archive.Dispose() }
    Write-Output "Built $zip SHA256 $zipHash"
} finally { Pop-Location }
