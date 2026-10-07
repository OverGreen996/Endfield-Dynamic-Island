param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
New-Item -ItemType Directory -Path (Join-Path $root 'outputs') -Force | Out-Null
& $Dotnet build (Join-Path $root 'tests\IslandRegression\IslandRegression.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Regression build failed.'}
$dll=Join-Path $root 'tests\IslandRegression\bin\Release\net8.0-windows10.0.19041.0\IslandRegression.dll'
$flags=@('--hud-pin','--original-hud-test','--personas-pool','--personas-layout','--assistant-native','--ai-backups','--cf-fast-reply','--ai-backups-layout','--native-search-layout','--memory-palace-layout','--reply-reveal','--unit','--bubble-layout','--music-unit','--notification-unit','--personal-unit','--image-unit','--local-ocr','--notification-monitor','--music-layout','--all-ui-layout','--all-edge-test','--music-edge-test','--collapse-test','--industrial-unit','--notification-routing','--industrial-test','--performance-unit','--performance-ui','--settings-chrome','--gpu-unit')
$results=Join-Path $root 'TestResults';New-Item -ItemType Directory -Path $results -Force | Out-Null
Push-Location $root
try {
 foreach($flag in $flags){
  $log=Join-Path $results ($flag.TrimStart('-')+'.log')
  & $Dotnet $dll $flag *> $log
  $resultText=[IO.File]::ReadAllText($log)
  if($LASTEXITCODE -ne 0 -or $resultText -match '(?m)FAIL:|Unhandled exception' -or $resultText -notmatch '\d+/\d+ (?:native [^\r\n]+ )?PASS'){Get-Content -LiteralPath $log -Tail 35;throw "Regression failed: $flag"}
  Write-Output "$flag PASS"
 }
}finally{Pop-Location}
Write-Output 'All regression suites passed. Tests use synthetic data and do not call model APIs.'
