$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'EndfieldChargePlus.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Run this script from an extracted portable release.'}
Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
