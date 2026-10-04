$ErrorActionPreference='Stop'
$pidPath=Join-Path $PSScriptRoot '.runtime\hub.pid'
$expectedExe=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'runtime\node.exe'))
$expectedScript=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'core\server.js'))
if(Test-Path -LiteralPath $pidPath){
 $hubPid=[int][IO.File]::ReadAllText($pidPath)
 $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+$hubPid)
 if($process){
  if(![string]::Equals($process.ExecutablePath,$expectedExe,[StringComparison]::OrdinalIgnoreCase) -or !$process.CommandLine.Contains($expectedScript)){
   throw 'PID ownership mismatch. Existing process was left running.'
  }
  Stop-Process -Id $hubPid -ErrorAction Stop
 }
}
& (Join-Path $PSScriptRoot 'Start-GeminiHub.ps1')
