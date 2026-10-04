param([string]$Dotnet='dotnet',[ValidateSet('win-x64')][string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$version='0.22.0'
$destination=Join-Path $root "artifacts\Endfield-Dynamic-Island-v$version-$Runtime"
if(Test-Path -LiteralPath $destination){throw 'Output already exists. Choose a clean checkout or remove that specific previous build first.'}
& $Dotnet publish (Join-Path $root 'src\EndfieldIsland\EndfieldChargePlus.csproj') -c Release -r $Runtime --self-contained true -o $destination
if($LASTEXITCODE -ne 0){throw 'Island publish failed.'}
& $Dotnet publish (Join-Path $root 'src\MusicPlayerHost\MusicPlayerHost.csproj') -c Release -r $Runtime --self-contained true -o (Join-Path $destination 'MusicPlayerHost')
if($LASTEXITCODE -ne 0){throw 'Music host publish failed.'}
foreach($file in @('LICENSE','NOTICE.md','README.md','README.en.md')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $destination -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start.ps1') -Destination $destination
foreach($relative in @('EndfieldChargePlus.runtimeconfig.json','MusicPlayerHost\MusicPlayerHost.runtimeconfig.json')){
 $config=Get-Content -LiteralPath (Join-Path $destination $relative) -Raw | ConvertFrom-Json
 if(!$config.runtimeOptions.includedFrameworks){throw 'Self-contained runtime missing.'}
}
if(!(Test-Path -LiteralPath (Join-Path $destination 'MusicPlayerHost\nonstop\LICENSE'))){throw 'NonStop license missing.'}
Write-Output "Published: $destination"
