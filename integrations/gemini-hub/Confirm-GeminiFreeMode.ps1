param([Parameter(Mandatory=$true)][ValidateSet('Free')][string]$Confirmation)
# Run only after the user confirms the current key's project is Free tier.
# Reads the existing encrypted key locally, never outputs it or sends it to a provider.
$ErrorActionPreference='Stop'
$taskPolicyPath=Join-Path $PSScriptRoot 'policy.json'
$taskPolicy=Get-Content -LiteralPath $taskPolicyPath -Raw | ConvertFrom-Json
if($taskPolicy.paidAllowed -ne $false -or $taskPolicy.toolsAllowed -ne $false){throw 'Unexpected paid/tool policy'}
Add-Type -AssemblyName System.Security
$taskBytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'data\gemini-key.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
$taskDigest=[Security.Cryptography.SHA256]::Create()
try{$taskKeyHash=([BitConverter]::ToString($taskDigest.ComputeHash($taskBytes))).Replace('-','').ToLowerInvariant()}
finally{[Array]::Clear($taskBytes,0,$taskBytes.Length);$taskDigest.Dispose()}
$taskPolicy.verification | Add-Member -NotePropertyName mode -NotePropertyValue 'project-pinned' -Force
$taskPolicy.verification | Add-Member -NotePropertyName binding -NotePropertyValue ([pscustomobject]@{project=$taskPolicy.project;model=$taskPolicy.defaultModel;keySha256=$taskKeyHash;limitsSha256=''}) -Force
$taskPolicy.verification.observedAt=[DateTimeOffset]::UtcNow.ToString('o')
$taskPolicy.verification.validUntil=$null
$taskPolicy.verification.source='User explicit Free tier confirmation'
$taskPolicy.verification.note='Pinned local confirmation, not a live cloud billing guarantee. New credentials, project, model or limits require renewed confirmation. No daily expiry; existing usage ledger retained.'
$taskCode=@'
const {pathToFileURL}=require('node:url');const path=require('node:path');
let input='';process.stdin.setEncoding('utf8');process.stdin.on('data',s=>input+=s);
process.stdin.on('end',async()=>{try{
const {validatePolicy,freePolicyFingerprint}=await import(pathToFileURL(path.join(process.argv[1],'core','guard.js')));
const p=JSON.parse(input);p.verification.binding.limitsSha256=freePolicyFingerprint(p);validatePolicy(p);
console.log(JSON.stringify(p,null,2));
}catch{console.error('Verification configuration invalid');process.exitCode=1;}});
'@
$taskJson=($taskPolicy | ConvertTo-Json -Depth 12) | & (Join-Path $PSScriptRoot 'runtime\node.exe') -e $taskCode $PSScriptRoot
if($LASTEXITCODE -ne 0){throw 'Policy validation failed; no changes applied'}
$taskBackups=Join-Path $PSScriptRoot '_backups';New-Item -ItemType Directory -Path $taskBackups -Force | Out-Null
$taskBackup=Join-Path $taskBackups ('policy-'+[guid]::NewGuid().ToString('N')+'.json')
$taskPending=$taskPolicyPath+'.pending-'+[guid]::NewGuid().ToString('N')
[IO.File]::WriteAllText($taskPending,($taskJson -join "`n")+"`n",(New-Object Text.UTF8Encoding($false)))
[IO.File]::Replace($taskPending,$taskPolicyPath,$taskBackup,$false)
Write-Output 'Free mode confirmed and bound to the existing project/key/model/budgets. No daily expiry. Restart Gemini Hub to apply. Usage was not reset.'
