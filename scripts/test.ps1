param([switch]$HardwareTone,[switch]$Soak,[switch]$SkipRestore)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.dotnet-home'
Push-Location $taskRoot
try {
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $buildArgs=@('build','AntiDbt.sln','-m:1','-p:UseSharedCompilation=false')
    if($SkipRestore){$buildArgs+='--no-restore'}
    & dotnet @buildArgs *> artifacts/test-build.log
    if($LASTEXITCODE -ne 0){throw 'Build failed: artifacts/test-build.log'}
    foreach($suite in @('Core','Windows','Providers')){
        $framework=if($suite -eq 'Windows'){'net9.0-windows'}else{'net9.0'}
        $dll="tests/AntiDbt.$suite.Tests/bin/Debug/$framework/AntiDbt.$suite.Tests.dll"
        & dotnet $dll *> "artifacts/$($suite.ToLower())-run.log"
        if($LASTEXITCODE -ne 0){throw "$suite tests failed; see artifacts"}
    }
    $desktopTest=Start-Process -FilePath "$taskRoot/src/AntiDbt.Desktop/bin/Debug/net9.0-windows/antiDBT.exe" -ArgumentList '--smoke-test',"$taskRoot/artifacts/desktop-smoke" -WindowStyle Hidden -Wait -PassThru
    if($desktopTest.ExitCode -ne 0){throw 'Desktop smoke failed'}
    & dotnet tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll *> artifacts/performance-run.log
    if($LASTEXITCODE -ne 0){throw 'Performance harness failed'}
    if($HardwareTone){
        & dotnet tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll --tone-capture *> artifacts/hardware-tone-run.log
        if($LASTEXITCODE -ne 0){throw 'Nonzero audio capture failed'}
    }
    if($Soak){
        & dotnet tests/AntiDbt.Diagnostics/bin/Debug/net9.0-windows/AntiDbt.Diagnostics.dll --soak *> artifacts/soak-run.log
        if($LASTEXITCODE -ne 0){throw 'Synthetic soak failed'}
    }
    Write-Output 'All requested checks passed. Evidence: prototype/artifacts.'
} finally {Pop-Location}
