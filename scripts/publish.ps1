param([string]$Output='publish/antiDBT-portable-win-x64',[switch]$Offline)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.dotnet-home'
Push-Location $taskRoot
try {
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $publishArgs=@('publish','src/AntiDbt.Desktop/AntiDbt.Desktop.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=false','-p:DebugType=None','-p:UseSharedCompilation=false','-m:1','-o',$Output)
    if($Offline){$publishArgs+='--no-restore'}
    & dotnet @publishArgs *> artifacts/publish-final.log
    if($LASTEXITCODE -ne 0){throw 'Publish failed: artifacts/publish-final.log'}
    Copy-Item -LiteralPath README.md -Destination (Join-Path $Output 'README.md')
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination (Join-Path $Output 'THIRD-PARTY-NOTICES.md')
    Copy-Item -LiteralPath licenses -Destination $Output -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $Output 'docs') -Force | Out-Null
    foreach($document in @('ARCHITECTURE.md','COMPLETION-MATRIX.md','VALIDATION.md','CONTINUATION.md','PROVIDER-SPECS.md')){
        Copy-Item -LiteralPath (Join-Path docs $document) -Destination (Join-Path $Output 'docs')
    }
    Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Output 'antiDBT.exe') | Select-Object Hash,Path | ConvertTo-Json | Set-Content artifacts/release-sha256.json -Encoding utf8
    Write-Output "Published: $Output/antiDBT.exe"
} finally {Pop-Location}
