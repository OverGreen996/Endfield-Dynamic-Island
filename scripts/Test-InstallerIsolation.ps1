param([Parameter(Mandatory=$true)][string]$PayloadDir,[Parameter(Mandatory=$true)][string]$InnoCompiler)
$ErrorActionPreference='Stop'
$sourceRoot=Split-Path $PSScriptRoot
& (Join-Path $PSScriptRoot 'Test-Distribution.ps1') -PayloadDir $PayloadDir
[xml]$project=Get-Content -LiteralPath (Join-Path $sourceRoot 'src/EndfieldIsland/EndfieldChargePlus.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
$id=[guid]::NewGuid().ToString('N')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('IslandInstallerVerification-'+$id)
$app=Join-Path $testRoot 'App';$output=Join-Path $testRoot 'Setup';$private=Join-Path $testRoot 'RetainedPrivateData'
New-Item -ItemType Directory -Path $output,$private -Force | Out-Null
# Synthetic sentinels only; never launch against the real user's data.
$hashes=@{}
foreach($p in @('policy.json','gemini-key.dpapi','usage.sqlite','search-state.sqlite','personal-settings.dpapi','assistant-personas.dpapi','gemini-pool-keys.dpapi')){
 [IO.File]::WriteAllText((Join-Path $private $p),'synthetic retained sentinel '+$p)
 $hashes[$p]=(Get-FileHash -LiteralPath (Join-Path $private $p)).Hash
}
$runPath='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runBefore=(Get-ItemProperty -LiteralPath $runPath -ErrorAction SilentlyContinue).'Endfield Charge Plus'
$phoneBefore=(Get-Service cloudflared -ErrorAction SilentlyContinue).Status
$appProcessesBefore=@(Get-Process EndfieldChargePlus -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
function Assert-Retained {
 foreach($p in $hashes.Keys){if($hashes[$p] -ne (Get-FileHash -LiteralPath (Join-Path $private $p)).Hash){throw 'Installer altered retained synthetic data.'}}
 if((Get-ItemProperty -LiteralPath $runPath -ErrorAction SilentlyContinue).'Endfield Charge Plus' -ne $runBefore){throw 'Verification altered real startup settings.'}
 if((Get-Service cloudflared -ErrorAction SilentlyContinue).Status -ne $phoneBefore){throw 'Verification affected the phone service.'}
 foreach($pidBefore in $appProcessesBefore){if(!(Get-Process -Id $pidBefore -ErrorAction SilentlyContinue)){throw 'Verification affected the running real app.'}}
}
& $InnoCompiler '/Qp' "/DPayloadDir=$PayloadDir" "/DAppVersion=$version" "/DOutputDir=$output" "/DSetupAppId=$([guid]::NewGuid())" "/DAppMutexName=Local\IslandVerification-$id" "/DInstallerMutex=IslandVerificationSetup-$id" "/DAppGroupName=Island Verification $id" '/DVerificationBuild=1' (Join-Path $sourceRoot 'installer/EndfieldIsland.iss')
if($LASTEXITCODE -ne 0){throw 'Isolated installer compilation failed.'}
$setup=Join-Path $output "Endfield-Dynamic-Island-Setup-v$version-win-x64.exe"
for($i=0;$i -lt 2;$i++){
 $install=Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=',('/DIR="'+$app+'"'),('/LOG="'+(Join-Path $testRoot ('install-'+$i+'.log'))+'"')) -WindowStyle Hidden -PassThru -Wait
 if($install.ExitCode -ne 0){throw 'Isolated install/upgrade failed.'}
 & (Join-Path $PSScriptRoot 'Test-Distribution.ps1') -PayloadDir $app
 foreach($p in @('EndfieldChargePlus.dll','MusicPlayerHost.dll')){if((Get-FileHash (Join-Path $app $p)).Hash -ne (Get-FileHash (Join-Path $PayloadDir $p)).Hash){throw 'Installed binary does not match payload.'}}
 if(Test-Path -LiteralPath (Join-Path $app 'MusicPlayerHost/coreclr.dll')){throw 'Music runtime was duplicated.'}
 Assert-Retained
}
Write-Output 'PASS: isolated fresh install and upgrade; native payload and retained sentinels verified.'
$uninstall=Start-Process -FilePath (Join-Path $app 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $testRoot 'uninstall.log')+'"')) -WindowStyle Hidden -PassThru -Wait
if($uninstall.ExitCode -ne 0){throw 'Isolated uninstall failed.'}
for($i=0;$i -lt 30 -and (Test-Path -LiteralPath (Join-Path $app 'EndfieldChargePlus.exe'));$i++){Start-Sleep -Milliseconds 200}
if(Test-Path -LiteralPath (Join-Path $app 'EndfieldChargePlus.exe')){throw 'Uninstall left the app executable.'}
Assert-Retained
Write-Output 'PASS: isolated uninstall preserves sentinels, startup settings and running app/phone service.'
Write-Output ('Installer verification artifacts: '+$testRoot)
