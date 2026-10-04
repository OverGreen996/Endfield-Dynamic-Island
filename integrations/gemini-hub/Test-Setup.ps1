param([string]$NodeExe='node')
$ErrorActionPreference='Stop'
$tempParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
$root=Join-Path $tempParent ('gemini-setup-qa-'+[guid]::NewGuid().ToString('N'))
$checks=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAIL: $Name"};$script:checks++;Write-Output "PASS: $Name"}
$fakeKey='AIza'+('x'*32)+'mock';$replacement='AIza'+('y'*32)+'test'
try{
 New-Item -ItemType Directory -Path (Join-Path $root 'runtime') -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'core') -Destination $root -Recurse
 foreach($file in @('Initialize-GeminiHub.ps1','Configure-GeminiHubFromStdin.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $root}
 Copy-Item -LiteralPath (Get-Command $NodeExe -ErrorAction Stop).Source -Destination (Join-Path $root 'runtime\node.exe')
 $configure=Join-Path $root 'Configure-GeminiHubFromStdin.ps1'
 $fakeKey | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $configure | Out-Null
 Check ($LASTEXITCODE -eq 2 -and !(Test-Path -LiteralPath (Join-Path $root 'policy.json'))) 'confirmation required before any configuration is created'
 $fakeKey | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $configure -FreeConfirmed | Out-Null
 Check ($LASTEXITCODE -eq 0) 'fresh key configured locally without provider calls'
 $policy=Join-Path $root 'policy.json';$token=Join-Path $root 'data\hub.token';$ledger=Join-Path $root 'data\usage.sqlite';$key=Join-Path $root 'data\gemini-key.dpapi'
 $p=Get-Content -LiteralPath $policy -Raw | ConvertFrom-Json
 Check ($p.enabled -and $p.dailyRequests -eq 20 -and !$p.providerLimitsVerified -and $null -eq $p.models.'gemini-3.5-flash-lite'.official) 'fresh install uses conservative local caps, not guessed cloud quota'
 $policyHash=(Get-FileHash -LiteralPath $policy).Hash;$tokenHash=(Get-FileHash -LiteralPath $token).Hash;$keyHash=(Get-FileHash -LiteralPath $key).Hash;$ledgerHash=(Get-FileHash -LiteralPath $ledger).Hash
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Initialize-GeminiHub.ps1') | Out-Null
 Check ($LASTEXITCODE -eq 0 -and $policyHash -eq (Get-FileHash -LiteralPath $policy).Hash -and $tokenHash -eq (Get-FileHash -LiteralPath $token).Hash -and $keyHash -eq (Get-FileHash -LiteralPath $key).Hash -and $ledgerHash -eq (Get-FileHash -LiteralPath $ledger).Hash) 'reinitialization preserves policy, auth, encrypted key and usage ledger'
 Check (![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($key)).Contains($fakeKey)) 'key is stored encrypted'
 $keyLock=[IO.File]::Open($policy,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{$replacement | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $configure -FreeConfirmed | Out-Null;Check ($LASTEXITCODE -eq 3) 'blocked policy replacement reports failure'}finally{$keyLock.Dispose()}
 Check ($keyHash -eq (Get-FileHash -LiteralPath $key).Hash -and $policyHash -eq (Get-FileHash -LiteralPath $policy).Hash) 'failed paired write rolls back the encrypted key and policy'
 $replacement | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $configure -FreeConfirmed | Out-Null
 Check ($LASTEXITCODE -eq 0 -and $tokenHash -eq (Get-FileHash -LiteralPath $token).Hash -and $ledgerHash -eq (Get-FileHash -LiteralPath $ledger).Hash) 'confirmed replacement keeps shared auth and usage history'
 Write-Output "$checks/$checks PASS; synthetic keys only, zero provider requests."
}finally{
 if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($root)) -ne $tempParent -or ![IO.Path]::GetFileName($root).StartsWith('gemini-setup-qa-')){throw 'Cleanup path mismatch'}
 if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
 $fakeKey=$null;$replacement=$null
}
