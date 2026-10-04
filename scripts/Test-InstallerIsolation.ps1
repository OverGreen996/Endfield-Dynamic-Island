param([Parameter(Mandatory=$true)][string]$PayloadDir,[Parameter(Mandatory=$true)][string]$InnoCompiler)
$ErrorActionPreference='Stop'
$sourceRoot=Split-Path $PSScriptRoot
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('IslandInstallerVerification-'+[guid]::NewGuid().ToString('N'))
$hub=Join-Path $testRoot 'SharedHub';$app=Join-Path $testRoot 'App';$output=Join-Path $testRoot 'Setup'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$processBefore=(Get-NetTCPConnection -State Listen -LocalPort 8890 -ErrorAction SilentlyContinue).OwningProcess
& $InnoCompiler '/Qp' "/DPayloadDir=$PayloadDir" "/DHubPayloadDir=$(Join-Path $PayloadDir 'SharedHubPayload')" '/DAppVersion=0.25.0' "/DOutputDir=$output" "/DHubDataDir=$hub" '/DSetupAppId=4A40BCD0-0126-4668-A981-18A378BD1F3D' '/DAppMutexName=Local\IslandInstallerVerification' '/DVerificationBuild=1' (Join-Path $sourceRoot 'installer\EndfieldIsland.iss')
if($LASTEXITCODE -ne 0){throw 'Isolated installer compilation failed.'}
$setup=Join-Path $output 'Endfield-Dynamic-Island-Setup-v0.25.0-win-x64.exe'
for($i=0;$i -lt 2;$i++){
 $install=Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=',('/DIR="'+$app+'"'),('/LOG="'+(Join-Path $testRoot ('install-'+$i+'.log'))+'"')) -WindowStyle Hidden -PassThru -Wait
 if($install.ExitCode -ne 0){throw 'Isolated install/upgrade failed.'}
 if(!(Test-Path -LiteralPath (Join-Path $app 'EndfieldChargePlus.exe')) -or !(Test-Path -LiteralPath (Join-Path $hub 'runtime\node.exe'))){throw 'App or shared Node payload missing.'}
 if(!(Test-Path -LiteralPath (Join-Path $app 'MusicPlayerHost.exe')) -or (Test-Path -LiteralPath (Join-Path $app 'MusicPlayerHost\coreclr.dll'))){throw 'Shared music runtime layout is missing or duplicated.'}
 if($i -eq 0){
  $paths=@('policy.json','data\hub.token','data\usage.sqlite','data\usage.sqlite.initialized')
  $hashes=@{};foreach($path in $paths){$hashes[$path]=(Get-FileHash -LiteralPath (Join-Path $hub $path)).Hash}
  # An unrelated consumer's private file must survive both upgrade and uninstall.
  [IO.File]::WriteAllText((Join-Path $hub 'data\consumer-private.txt'),'synthetic retained data')
 }else{foreach($path in $paths){if($hashes[$path] -ne (Get-FileHash -LiteralPath (Join-Path $hub $path)).Hash){throw 'Upgrade changed shared private state.'}}}
}
Write-Output 'PASS: fresh install and upgrade preserve shared policy/auth/ledger.'
$uninstall=Start-Process -FilePath (Join-Path $app 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $testRoot 'uninstall.log')+'"')) -WindowStyle Hidden -PassThru -Wait
if($uninstall.ExitCode -ne 0){throw 'Isolated uninstall failed.'}
for($i=0;$i -lt 30 -and (Test-Path -LiteralPath (Join-Path $app 'EndfieldChargePlus.exe'));$i++){Start-Sleep -Milliseconds 200}
if(Test-Path -LiteralPath (Join-Path $app 'EndfieldChargePlus.exe')){throw 'Uninstall left the app executable.'}
foreach($path in $paths){if($hashes[$path] -ne (Get-FileHash -LiteralPath (Join-Path $hub $path)).Hash){throw 'Uninstall changed shared private state.'}}
foreach($path in @('core\server.js','runtime\node.exe','data\consumer-private.txt','Ensure-GeminiHub.ps1')){if(!(Test-Path -LiteralPath (Join-Path $hub $path))){throw 'Uninstall removed shared service files.'}}
$processAfter=(Get-NetTCPConnection -State Listen -LocalPort 8890 -ErrorAction SilentlyContinue).OwningProcess
if($processBefore -and $processAfter -ne $processBefore){throw 'Isolated setup touched an existing service.'}
Write-Output 'PASS: uninstall removes the isolated app while retaining shared core, runtime, policy and data; existing 8890 process untouched.'
Write-Output ('Installer verification artifacts: '+$testRoot)
