$ErrorActionPreference='Stop'
$hubRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$runtime=Join-Path $hubRoot 'runtime\node.exe'
$dataPath=Join-Path $hubRoot 'data'
$logPath=Join-Path $hubRoot '.runtime'
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
if(Get-NetTCPConnection -State Listen -LocalPort 8890 -ErrorAction SilentlyContinue){throw 'Port 8890 is already in use. Existing process was left running.'}
$secretPath=Join-Path $dataPath 'gemini-key.dpapi'
$oldKey=[Environment]::GetEnvironmentVariable('GEMINI_API_KEY','Process')
try{
 $env:GEMINI_API_KEY=''
 if(Test-Path -LiteralPath $secretPath){
  Add-Type -AssemblyName System.Security
  $encrypted=[IO.File]::ReadAllBytes($secretPath)
  $bytes=[Security.Cryptography.ProtectedData]::Unprotect($encrypted,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  $env:GEMINI_API_KEY=[Text.Encoding]::UTF8.GetString($bytes)
  [Array]::Clear($bytes,0,$bytes.Length)
 }
 $process=Start-Process -FilePath $runtime -ArgumentList ('"'+(Join-Path $hubRoot 'core\server.js')+'"') -WorkingDirectory $hubRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logPath 'hub.out.log') -RedirectStandardError (Join-Path $logPath 'hub.err.log')
 [IO.File]::WriteAllText((Join-Path $logPath 'hub.pid'),[string]$process.Id)
}finally{[Environment]::SetEnvironmentVariable('GEMINI_API_KEY',$oldKey,'Process');$oldKey=$null}
$headers=@{Authorization='Bearer '+[IO.File]::ReadAllText((Join-Path $dataPath 'hub.token')).Trim()}
$ready=$false
for($i=0;$i -lt 20;$i++){
 try{$health=Invoke-RestMethod 'http://127.0.0.1:8890/health' -Headers $headers -TimeoutSec 2;if($health.service -eq 'Gemini Usage Hub'){$ready=$true;break}}catch{}
 Start-Sleep -Milliseconds 200
}
if(!$ready){throw 'Hub startup failed. Inspect .runtime logs.'}
Write-Output ('Gemini Hub ready on loopback 8890. Key configured: '+$health.key_present+'. No automatic Google API requests.')
