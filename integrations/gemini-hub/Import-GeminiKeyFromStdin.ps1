param([switch]$ReplaceExisting)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Save-GeminiCredential.ps1')
$plain=[Console]::In.ReadToEnd().Trim()
try {
 Save-GeminiCredential $PSScriptRoot $plain $ReplaceExisting.IsPresent
 Write-Output '{"ok":true}'
} catch {
 if($_.Exception.Message -eq 'key_format_or_project_mismatch'){Write-Output '{"ok":false,"error":"key_format_or_project_mismatch"}';exit 2}
 Write-Output '{"ok":false,"error":"credential_save_failed"}';exit 3
} finally {$plain=$null}
