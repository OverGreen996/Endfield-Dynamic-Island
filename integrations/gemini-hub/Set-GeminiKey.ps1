# Local credential entry only. Never pass the key as a command line argument.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Save-GeminiCredential.ps1')
$secure=Read-Host '在此電腦輸入 Gemini API Key（隱藏輸入；不要貼到聊天）' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try{
 $plain=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
 Save-GeminiCredential $PSScriptRoot $plain $true
 Write-Output 'API Key 已以 Windows DPAPI 加密保存。重新啟動 Gemini Hub 才會載入；未呼叫 Google API。'
}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$plain=$null;$secure.Dispose()}
