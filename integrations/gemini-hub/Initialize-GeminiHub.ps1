$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'runtime\node.exe') (Join-Path $PSScriptRoot 'core\bootstrap.js') init $PSScriptRoot
if($LASTEXITCODE -ne 0){throw 'Local initialization failed. Existing data was preserved.'}
