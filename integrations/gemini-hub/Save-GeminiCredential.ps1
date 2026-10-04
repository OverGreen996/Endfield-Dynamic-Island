# Shared local credential persistence. No provider calls or quota changes.
function Save-GeminiCredential([string]$Root,[string]$Key,[bool]$ReplaceExisting=$false) {
 $ErrorActionPreference='Stop'
 $policy=Get-Content -LiteralPath (Join-Path $Root 'policy.json') -Raw | ConvertFrom-Json
 $suffix=[string]$policy.verification.keySuffix
 if($Key.Length -gt 160 -or $Key -notmatch '^(?:AIza[A-Za-z0-9_-]{30,}|AQ\.[A-Za-z0-9_-]{30,})$' -or $suffix.Length -lt 4 -or !$Key.EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)){throw 'key_format_or_project_mismatch'}
 if($policy.verification.mode -eq 'project-pinned'){
  $digest=[Security.Cryptography.SHA256]::Create()
  $keyBytes=[Text.Encoding]::UTF8.GetBytes($Key)
  try{$actual=([BitConverter]::ToString($digest.ComputeHash($keyBytes))).Replace('-','').ToLowerInvariant()}
  finally{[Array]::Clear($keyBytes,0,$keyBytes.Length);$digest.Dispose()}
  if($actual -ne [string]$policy.verification.binding.keySha256){throw 'key_requires_free_tier_reverification'}
 }
 Add-Type -AssemblyName System.Security
 $data=Join-Path $Root 'data'
 $secretPath=Join-Path $data 'gemini-key.dpapi'
 $lock=$null;$pending=Join-Path $data ('gemini-key.pending-'+[guid]::NewGuid().ToString('N'))
 try {
  $lock=[IO.File]::Open((Join-Path $data 'key-write.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  if((Test-Path -LiteralPath $secretPath) -and !$ReplaceExisting){throw 'existing_credential_left_unchanged'}
  $bytes=[Text.Encoding]::UTF8.GetBytes($Key)
  try{$encrypted=[Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)}finally{[Array]::Clear($bytes,0,$bytes.Length)}
  [IO.File]::WriteAllBytes($pending,$encrypted)
  if(Test-Path -LiteralPath $secretPath){
   $backups=Join-Path $Root '_backups';$null=New-Item -ItemType Directory -Path $backups -Force
   $backup=Join-Path $backups ('key-'+[guid]::NewGuid().ToString('N')+'.dpapi')
   [IO.File]::Replace($pending,$secretPath,$backup,$false)
  }else{[IO.File]::Move($pending,$secretPath)}
 }finally{
  if($lock){$lock.Dispose()}
  if(Test-Path -LiteralPath $pending){Remove-Item -LiteralPath $pending -Force}
  $Key=$null
 }
}
