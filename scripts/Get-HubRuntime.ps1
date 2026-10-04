param([Parameter(Mandatory=$true)][string]$Destination,[string]$ExistingRuntime)
$ErrorActionPreference='Stop'
# Pinned upstream binary, verified against the official release manifest.
$version='24.21.0';$expected='ba4e6d110e8c1592a1ecd390f6b05f3da124b13871a5be62b341a07a853c6c32'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$binary=Join-Path $Destination 'node.exe'
if($ExistingRuntime -and (Test-Path -LiteralPath $ExistingRuntime) -and (Get-FileHash -LiteralPath $ExistingRuntime -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected){Copy-Item -LiteralPath $ExistingRuntime -Destination $binary}
if(!(Test-Path -LiteralPath $binary) -or (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){
 $manifest=(Invoke-WebRequest "https://nodejs.org/dist/v$version/SHASUMS256.txt" -UseBasicParsing).Content
 if($manifest -notmatch ($expected+'\s+win-x64/node.exe')){throw 'Upstream Node checksum mismatch.'}
 Invoke-WebRequest "https://nodejs.org/dist/v$version/win-x64/node.exe" -OutFile $binary -UseBasicParsing
}
if((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'Node binary integrity check failed.'}
$license=Join-Path $Destination 'LICENSE'
if(!(Test-Path -LiteralPath $license)){Invoke-WebRequest "https://raw.githubusercontent.com/nodejs/node/v$version/LICENSE" -OutFile $license -UseBasicParsing}
if((Get-Item -LiteralPath $license).Length -lt 10000){throw 'Node license missing.'}
