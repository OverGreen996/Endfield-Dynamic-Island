param([string]$NodeExe='node')
$ErrorActionPreference='Stop'
$taskTempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskTestRoot=Join-Path $taskTempRoot ('gemini-hub-public-tests-'+[guid]::NewGuid().ToString('N'))
try {
 $null=New-Item -ItemType Directory -Path $taskTestRoot
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'core') -Destination $taskTestRoot -Recurse
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'policy.example.json') -Destination (Join-Path $taskTestRoot 'policy.json')
 & $NodeExe --test (Join-Path $taskTestRoot 'core/tests/*.test.js')
 if($LASTEXITCODE -ne 0){throw 'Gemini Hub regression failed'}
} finally {
 $taskResolved=[IO.Path]::GetFullPath($taskTestRoot)
 if(![string]::Equals([IO.Path]::GetDirectoryName($taskResolved),$taskTempRoot.TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($taskResolved).StartsWith('gemini-hub-public-tests-')){throw 'Test cleanup path mismatch'}
 if(Test-Path -LiteralPath $taskResolved){Remove-Item -LiteralPath $taskResolved -Recurse -Force}
}
