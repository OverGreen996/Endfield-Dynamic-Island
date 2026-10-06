param([string]$PayloadDir)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$retired='SharedHubPayload|Get-HubRuntime|DHubPayloadDir|DHubDataDir|integrations[/\\]gemini-hub|https?://(?:127\.0\.0\.1|localhost):(?:8888|8889|8890)|Start-XNG|XngConnection|Shared AI service|shared Gemini Hub|重新啟動靈動島 AI 服務|Applying restarts the Island AI service'
$files=@(Get-Item (Join-Path $root 'README.md'),(Join-Path $root 'README.en.md'),(Join-Path $root 'NOTICE.md'),(Join-Path $root 'CHANGELOG.md'))
$files+=Get-ChildItem (Join-Path $root 'docs') -Recurse -File -Filter '*.md'
$files+=Get-ChildItem (Join-Path $root '.github') -Recurse -File
$files+=Get-ChildItem (Join-Path $root 'installer') -File -Filter '*.iss'
$artFiles=@(Get-Item (Join-Path $root 'installer/render-art.cjs'))+@(Get-ChildItem (Join-Path $root 'installer/assets') -File -Filter '*.svg')
foreach($f in $artFiles){if([IO.File]::ReadAllText($f.FullName) -match '(?:WINDOWS|ISLAND)[^<\r\n]*\b(?:0\.\d+\.\d+|0\d{2})\b'){throw "Hard-coded installer artwork version: $($f.FullName)"}}
$files+=Get-Item (Join-Path $root 'scripts/Test-InstallerIsolation.ps1')
$files+=Get-ChildItem (Join-Path $root 'src/EndfieldIsland') -Recurse -File | Where-Object {$_.Extension -in '.cs','.axaml' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'}
foreach($f in $files){if([IO.File]::ReadAllText($f.FullName) -match $retired){throw "Retired instruction or dependency: $($f.FullName)"}}
foreach($p in @('integrations/gemini-hub','scripts/Get-HubRuntime.ps1')){if(Test-Path -LiteralPath (Join-Path $root $p)){throw "Retired tool still exists: $p"}}
if($PayloadDir){
 $base=(Resolve-Path -LiteralPath $PayloadDir).Path
 $payload=Get-ChildItem -LiteralPath $base -Recurse -File
 $bad=$payload | Where-Object {$_.Name -in @('node.exe','server.js','policy.json','gemini-key.dpapi','assistant-backup-keys.dpapi','assistant-backup-state.json','assistant-personas.dpapi','gemini-pool-keys.dpapi','search-secrets.dpapi','usage.sqlite','search-state.sqlite','gemini-pool-state.sqlite','hub.token') -or $_.Extension -in '.ps1','.cmd','.bat','.dpapi','.sqlite' -or $_.FullName -match '[\\/](SharedHubPayload|archive|GeminiHub)[\\/]'}
 if($bad){throw "Forbidden payload file: $($bad[0].FullName)"}
 foreach($f in $payload | Where-Object {$_.Extension -eq '.md'}){if([IO.File]::ReadAllText($f.FullName) -match $retired){throw "Retired payload instruction: $($f.FullName)"}}
 foreach($p in @('EndfieldChargePlus.exe','MusicPlayerHost.exe','coreclr.dll','README.md','README.en.md','CHANGELOG.md','NOTICE.md','nonstop/LICENSE','docs/INSTALL.zh-TW.md','docs/INSTALL.en.md','docs/USAGE.zh-TW.md','docs/USAGE.en.md','docs/VALIDATION.md')){if(!(Test-Path -LiteralPath (Join-Path $base $p))){throw "Missing payload file: $p"}}
 foreach($f in $payload | Where-Object {$_.Extension -eq '.md'}){
  foreach($m in [regex]::Matches([IO.File]::ReadAllText($f.FullName),'\]\(([^\s)]+)\)')){
   $target=$m.Groups[1].Value.Split('#')[0]
   if(!$target -or $target -match '^[a-z]+:'){continue}
   if(!(Test-Path -LiteralPath (Join-Path $f.DirectoryName $target))){throw "Broken packaged link in $($f.Name): $target"}
  }
 }
}
Write-Output 'PASS: current instructions and distribution contain no retired AI service, download helpers or private payload.'
