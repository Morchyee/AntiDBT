param([switch]$SkipRestore,[switch]$Soak,[switch]$HardwareTone)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.dotnet-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
Push-Location $taskRoot
try {
    New-Item -ItemType Directory -Path artifacts/phase4 -Force | Out-Null
    $buildArgs=@('build','AntiDbt.sln','-m:1','-p:UseSharedCompilation=false')
    if($SkipRestore){$buildArgs+='--no-restore'}
    & dotnet @buildArgs *> artifacts/phase4/full-build.log
    if($LASTEXITCODE -ne 0){throw 'Build failed: artifacts/phase4/full-build.log'}
    foreach($suite in @('Core','Windows','CloudSpeech','Providers','Providers.Multi','Language','Domestic')){
        $framework=if($suite -eq 'Windows'){'net9.0-windows'}else{'net9.0'}
        & dotnet "tests/AntiDbt.$suite.Tests/bin/Debug/$framework/AntiDbt.$suite.Tests.dll" *> "artifacts/phase4/$($suite.ToLower())-run.log"
        if($LASTEXITCODE -ne 0){throw "$suite tests failed; see artifacts/phase4"}
    }
    & dotnet tests/AntiDbt.Providers.Multi.Tests/bin/Debug/net9.0/AntiDbt.Providers.Multi.Tests.dll --localhost *> artifacts/phase4/combinations-run.log
    if($LASTEXITCODE -ne 0){throw 'Localhost combinations failed.'}
    & dotnet tests/AntiDbt.CloudSpeech.Tests/bin/Debug/net9.0/AntiDbt.CloudSpeech.Tests.dll --native-smoke *> artifacts/phase4/native-smoke.log
    if($LASTEXITCODE -ne 0){throw 'Azure native resource smoke failed.'}
    $desktopTest=Start-Process -FilePath "$taskRoot/src/AntiDbt.Desktop/bin/Debug/net9.0-windows/antiDBT.exe" -ArgumentList '--smoke-test',"$taskRoot/artifacts/phase4/desktop-smoke" -WindowStyle Hidden -Wait -PassThru
    if($desktopTest.ExitCode -ne 0){throw 'WPF desktop smoke failed.'}
    & dotnet tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll *> artifacts/phase4/performance-run.log
    if($LASTEXITCODE -ne 0){throw 'Performance harness failed.'}
    if($Soak){
        & dotnet tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll --soak *> artifacts/phase4/soak-run.log
        if($LASTEXITCODE -ne 0){throw 'Synthetic short soak failed.'}
    }
    if($HardwareTone){
        & dotnet tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll --tone-capture *> artifacts/phase4/hardware-tone-run.log
        if($LASTEXITCODE -ne 0){throw 'Hardware audio tone smoke failed.'}
    }
    Write-Output 'Phase4 automated checks passed; no real cloud or physical touchpad validation implied.'
} finally {Pop-Location}
