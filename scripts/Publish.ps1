param([string]$Dotnet='dotnet',[ValidateSet('win-x64')][string]$Runtime='win-x64',[string]$InnoCompiler='ISCC.exe')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src\EndfieldIsland\EndfieldChargePlus.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
if(!(Get-Command $InnoCompiler -ErrorAction SilentlyContinue)){throw 'Inno Setup 7.1 or newer is required. Pass -InnoCompiler with the path to ISCC.exe.'}
$compiler=(Get-Command $InnoCompiler -ErrorAction Stop).Source
# ISCC's file resource reports 0.0.0.0; use the compiler engine's version.
$compilerVersion=(& $compiler '--version' | Out-String).Trim()
if($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '\b(\d+\.\d+\.\d+)\b' -or [version]$matches[1] -lt [version]'7.1.0'){throw 'Inno Setup 7.1 or newer is required for the industrial installer theme.'}
# The runtime folder is internal build staging, never a portable distribution.
$destination=Join-Path $root ('artifacts\staging\v'+$version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
& $Dotnet publish (Join-Path $root 'src\EndfieldIsland\EndfieldChargePlus.csproj') -c Release -r $Runtime --self-contained true -o $destination
if($LASTEXITCODE -ne 0){throw 'Island publish failed.'}
# Both independent processes share the same pinned .NET files on disk.
# Executable/deps/runtimeconfig names stay distinct; private player data stays outside the installation.
& $Dotnet publish (Join-Path $root 'src\MusicPlayerHost\MusicPlayerHost.csproj') -c Release -r $Runtime --self-contained true -o $destination
if($LASTEXITCODE -ne 0){throw 'Music host publish failed.'}
foreach($file in @('LICENSE','NOTICE.md','README.md','README.en.md','CHANGELOG.md')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $destination -Recurse
foreach($relative in @('EndfieldChargePlus.runtimeconfig.json','MusicPlayerHost.runtimeconfig.json')){
 $config=Get-Content -LiteralPath (Join-Path $destination $relative) -Raw | ConvertFrom-Json
 if(!$config.runtimeOptions.includedFrameworks){throw 'Self-contained runtime missing.'}
}
if(!(Test-Path -LiteralPath (Join-Path $destination 'nonstop\LICENSE'))){throw 'NonStop license missing.'}
$installerOutput=Join-Path $root 'artifacts\installer'
# Only the current native runtime and current documentation ship.
$shippingCode=Get-ChildItem -LiteralPath (Join-Path $root 'src\EndfieldIsland') -Recurse -File -Filter '*.cs' | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}
foreach($file in $shippingCode){
 if((Get-Content -LiteralPath $file.FullName -Raw) -match 'XngConnection|Start-XNG|core[\\/]xng\.js|127\.0\.0\.1:8889|http://127\.0\.0\.1:8890'){throw 'Retired runtime dependency found. Packaging was stopped.'}
}
if(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object {$_.Name -in 'node.exe','policy.json','gemini-key.dpapi','assistant-backup-keys.dpapi','assistant-backup-state.json','assistant-personas.dpapi','gemini-pool-keys.dpapi','search-secrets.dpapi','usage.sqlite','search-state.sqlite','gemini-pool-state.sqlite'}){throw 'Unexpected runtime or private data in payload.'}
& (Join-Path $PSScriptRoot 'Test-Distribution.ps1') -PayloadDir $destination
& $compiler '/Qp' "/DPayloadDir=$destination" "/DAppVersion=$version" "/DOutputDir=$installerOutput" (Join-Path $root 'installer\EndfieldIsland.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$setup=Join-Path $installerOutput "Endfield-Dynamic-Island-Setup-v$version-$Runtime.exe"
$hash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($setup+'.sha256',"$hash  $([IO.Path]::GetFileName($setup))`n",[Text.Encoding]::ASCII)
Write-Output "Windows installer: $setup"
Write-Output "SHA256: $hash"
