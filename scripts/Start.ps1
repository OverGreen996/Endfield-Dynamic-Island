$ErrorActionPreference='Stop'
$exe=Join-Path $env:LOCALAPPDATA 'Programs\EndfieldDynamicIsland\EndfieldChargePlus.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Install Endfield Dynamic Island with the Windows setup program first.'}
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden
