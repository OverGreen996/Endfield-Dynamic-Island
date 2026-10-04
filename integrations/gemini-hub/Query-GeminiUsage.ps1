param([switch]$Json)
$ErrorActionPreference='Stop'
$tokenPath=Join-Path $PSScriptRoot 'data\hub.token'
try{
 $headers=@{Authorization='Bearer '+[IO.File]::ReadAllText($tokenPath).Trim()}
 $usage=Invoke-RestMethod 'http://127.0.0.1:8890/usage' -Headers $headers -TimeoutSec 3
 if($Json){$usage | ConvertTo-Json -Depth 8}
 else{
  Write-Output 'Gemini 共用用量（本機查詢，不呼叫 Google）'
  Write-Output ('API Key 已設定：'+$usage.key_present+'；預設模型：'+$usage.default_model)
  foreach($entry in $usage.models.PSObject.Properties){
   $v=$entry.Value
   Write-Output ($entry.Name+'：今日已用 '+$v.used_requests_today+' / '+$v.local_limits.rpd+'；剩餘 '+$v.remaining_requests_today+'；每分鐘 '+$v.used_requests_last_minute+' / '+$v.local_limits.rpm)
   Write-Output ('  Google 已回報 Token：輸入 '+$v.reported_tokens_today.input+'、輸出 '+$v.reported_tokens_today.output+'、思考 '+$v.reported_tokens_today.thinking)
   if($v.locked_reason){Write-Output ('  已停止：'+$v.locked_reason)}
  }
  Write-Output ('今日計入 Token（含安全預留）：'+$usage.accounted_tokens_today+' / '+$usage.daily_local_token_limit)
  Write-Output ('每日重置：'+([datetimeoffset]$usage.next_daily_reset).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss zzz'))
  if($usage.free_tier_verification_mode -eq 'project-pinned'){
   Write-Output '免費方案：專案／金鑰／模型與額度已固定，不會每日到期；變更時需重新核對。'
  }else{Write-Output ('免費方案驗證有效至：'+([datetimeoffset]$usage.free_tier_verified_until).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss zzz'))}
  Write-Output 'Google 計費方案是本機確認紀錄，不是即時雲端查詢；請勿替此專案啟用付費。'
  Write-Output '此處只統計經過 Hub 的用量；其他程式直接呼叫的用量，請至 AI Studio 查詢。'
 }
}catch{
 & (Join-Path $PSScriptRoot 'runtime\node.exe') (Join-Path $PSScriptRoot 'core\cli.js') usage
}
