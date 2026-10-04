param([switch]$FreeConfirmed)
$ErrorActionPreference='Stop'
$plain=[Console]::In.ReadToEnd().Trim()
$lock=$null;$policyChanged=$false;$keyChanged=$false;$policyBackup=$null;$keyBackup=$null
$policyPath=Join-Path $PSScriptRoot 'policy.json';$keyPath=Join-Path $PSScriptRoot 'data\gemini-key.dpapi'
$pendingSuffix='.pending-'+[guid]::NewGuid().ToString('N')
try{
 if(!$FreeConfirmed -or $plain.Length -gt 160 -or $plain -notmatch '^(?:AIza[A-Za-z0-9_-]{30,}|AQ\.[A-Za-z0-9_-]{30,})$'){exit 2}
 & (Join-Path $PSScriptRoot 'Initialize-GeminiHub.ps1') | Out-Null
 $lock=[IO.File]::Open((Join-Path $PSScriptRoot 'data\key-write.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 Add-Type -AssemblyName System.Security
 $bytes=[Text.Encoding]::UTF8.GetBytes($plain);$digest=[Security.Cryptography.SHA256]::Create()
 try{
  $hash=([BitConverter]::ToString($digest.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()
  $encrypted=[Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 }finally{[Array]::Clear($bytes,0,$bytes.Length);$digest.Dispose()}
 $inputJson=@{keyHash=$hash;keySuffix=$plain.Substring($plain.Length-4);freeConfirmed=$true}|ConvertTo-Json -Compress
 $policyJson=$inputJson | & (Join-Path $PSScriptRoot 'runtime\node.exe') (Join-Path $PSScriptRoot 'core\bootstrap.js') confirm $PSScriptRoot
 if($LASTEXITCODE -ne 0){throw 'Policy confirmation failed'}
 $backups=Join-Path $PSScriptRoot '_backups';New-Item -ItemType Directory -Path $backups -Force | Out-Null
 $policyBackup=Join-Path $backups ('policy-'+[guid]::NewGuid().ToString('N')+'.json')
 $keyBackup=Join-Path $backups ('key-'+[guid]::NewGuid().ToString('N')+'.dpapi')
 [IO.File]::WriteAllText($policyPath+$pendingSuffix,($policyJson -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllBytes($keyPath+$pendingSuffix,$encrypted)
 if(Test-Path -LiteralPath $keyPath){[IO.File]::Replace($keyPath+$pendingSuffix,$keyPath,$keyBackup,$false)}else{[IO.File]::Move($keyPath+$pendingSuffix,$keyPath)}
 $keyChanged=$true
 [IO.File]::Replace($policyPath+$pendingSuffix,$policyPath,$policyBackup,$false);$policyChanged=$true
 Write-Output '{"ok":true}'
}catch{
 # A crash between the two writes fails closed: key hash mismatch rejects requests.
 # An ordinary I/O failure rolls back the paired files before returning an error.
 if($policyChanged -and (Test-Path -LiteralPath $policyBackup)){[IO.File]::Copy($policyBackup,$policyPath,$true)}
 if($keyChanged){if(Test-Path -LiteralPath $keyBackup){[IO.File]::Copy($keyBackup,$keyPath,$true)}elseif(Test-Path -LiteralPath $keyPath){[IO.File]::Delete($keyPath)}}
 Write-Output '{"ok":false,"error":"local_configuration_failed"}';exit 3
}finally{
 if($lock){$lock.Dispose()}
 foreach($file in @($policyPath+$pendingSuffix,$keyPath+$pendingSuffix)){if(Test-Path -LiteralPath $file){[IO.File]::Delete($file)}}
 $plain=$null;$encrypted=$null
}
