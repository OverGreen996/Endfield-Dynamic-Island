$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Initialize-GeminiHub.ps1')
if(Get-NetTCPConnection -State Listen -LocalPort 8890 -ErrorAction SilentlyContinue){
 $headers=@{Authorization='Bearer '+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'data\hub.token')).Trim()}
 $health=Invoke-RestMethod 'http://127.0.0.1:8890/health' -Headers $headers -TimeoutSec 3
 if($health.service -ne 'Gemini Usage Hub'){throw 'Port 8890 belongs to another service; left untouched.'}
 Write-Output 'Existing shared Hub retained.'
}else{& (Join-Path $PSScriptRoot 'Start-GeminiHub.ps1')}
