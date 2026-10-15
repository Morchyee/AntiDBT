param([string]$Output='publish/antiDBT-phase4-win-x64',[switch]$Offline)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.dotnet-home'
Push-Location $taskRoot
try {
    $destination=[IO.Path]::GetFullPath((Join-Path $taskRoot $Output))
    $publishRoot=[IO.Path]::GetFullPath((Join-Path $taskRoot 'publish'))+[IO.Path]::DirectorySeparatorChar
    if(-not $destination.StartsWith($publishRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be within prototype/publish.'}
    $requestedDestination=$destination
    $revision=2
    while(Test-Path -LiteralPath $destination){
        $destination="$requestedDestination-r$revision"
        $revision++
    }
    New-Item -ItemType Directory -Path artifacts/phase4 -Force | Out-Null
    $restoreArgs=@('restore','src/AntiDbt.Desktop/AntiDbt.Desktop.csproj','-r','win-x64','-m:1')
    if($Offline){$restoreArgs+='--ignore-failed-sources'}
    & dotnet @restoreArgs *> artifacts/phase4/publish-restore.log
    if($LASTEXITCODE -ne 0){throw 'Restore failed: artifacts/phase4/publish-restore.log'}
    & dotnet publish src/AntiDbt.Desktop/AntiDbt.Desktop.csproj --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=None -p:UseSharedCompilation=false -m:1 -o $destination *> artifacts/phase4/publish.log
    if($LASTEXITCODE -ne 0){throw 'Publish failed: artifacts/phase4/publish.log; partial output preserved for diagnostics.'}
    Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md -Destination $destination
    Copy-Item -LiteralPath licenses -Destination $destination -Recurse
    New-Item -ItemType Directory -Path (Join-Path $destination 'docs') -Force | Out-Null
    foreach($document in @('API_PROVIDERS.md','ARCHITECTURE.md','COMPLETION-MATRIX.md','VALIDATION.md','CONTINUATION.md','PROVIDER-SPECS.md')){
        Copy-Item -LiteralPath (Join-Path docs $document) -Destination (Join-Path $destination 'docs')
    }
    Copy-Item -LiteralPath docs/phase4 -Destination (Join-Path $destination 'docs') -Recurse
    # Include resolved dependency metadata and the legal files supplied by each NuGet package.
    $assets=Get-Content src/AntiDbt.Desktop/obj/project.assets.json -Raw | ConvertFrom-Json -AsHashtable
    $dependencies=@()
    foreach($entry in $assets.libraries.GetEnumerator() | Sort-Object Key){
        if($entry.Value.type -ne 'package'){continue}
        $packageDir=Join-Path $taskRoot ('.packages/'+$entry.Value.path)
        $legalDir=Join-Path $destination ('licenses/packages/'+$entry.Value.path)
        New-Item -ItemType Directory -Path $legalDir -Force | Out-Null
        $files=@(Get-ChildItem -LiteralPath $packageDir -File | Where-Object {$_.Name -match '^(LICENSE|NOTICE|THIRD.?PARTY)' -or $_.Extension -eq '.nuspec'})
        foreach($file in $files){Copy-Item -LiteralPath $file.FullName -Destination $legalDir}
        $dependencies+=@{package=$entry.Key;legalFiles=@($files.Name)}
    }
    $dependencies | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'licenses/DEPENDENCIES.json') -Encoding utf8
    $native=Join-Path $destination 'Microsoft.CognitiveServices.Speech.core.dll'
    if(-not(Test-Path -LiteralPath $native)){throw 'Required Azure Speech native runtime missing from portable output.'}
    if(Test-Path -LiteralPath (Join-Path $destination 'data')){throw 'Fresh portable must not contain user configuration data.'}
    $zipPath="$destination.zip"
    if(Test-Path -LiteralPath $zipPath){Remove-Item -LiteralPath $zipPath -Force}
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $zipPath -Force
    $exeHash=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination 'antiDBT.exe')).Hash
    $zipHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
    [PSCustomObject]@{
        Hash=$exeHash
        Path=(Join-Path $destination 'antiDBT.exe')
        ZipHash=$zipHash
        ZipPath=$zipPath
    } | ConvertTo-Json -Depth 4 | Set-Content artifacts/phase4/release-sha256.json -Encoding utf8
    @{requested=$requestedDestination;destination=$destination;zipPath=$zipPath;existingOutputsPreserved=$true;sha256=$exeHash;zipSha256=$zipHash} | ConvertTo-Json | Set-Content artifacts/phase4/last-publish.json -Encoding utf8
    Write-Output "Published new release: $destination"
    Write-Output "Created release archive: $zipPath"
} finally {Pop-Location}
