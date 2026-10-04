import { UsageGuard } from './guard.js';
import { loadPolicy,dbPath } from './config.js';
const action=process.argv[2]??'usage';
try{
 if(!['init','usage','usage-json'].includes(action))throw new Error('unknown_command');
 const g=new UsageGuard(dbPath,loadPolicy(),{initialize:action==='init'});
 if(action==='init')console.log('SQLite usage ledger initialized. Existing ledger is preserved.');
 else{
  const s=g.status(false);
  if(action==='usage-json')console.log(JSON.stringify(s,null,2));
  else{
   console.log('Gemini 本機共用用量（不呼叫 Google，不消耗 Gemini 對話次數）');
   console.log('官方全專案剩餘額度：請查 AI Studio；此處只追蹤經過 Hub 的呼叫。');
   for(const [model,v]of Object.entries(s.models))
    console.log(model+': 今日已用 '+v.used_requests_today+' / '+v.local_limits.rpd+
    '，本機剩餘 '+v.remaining_requests_today+'；每分鐘 '+v.used_requests_last_minute+' / '+v.local_limits.rpm+
    (v.locked_reason?'；停止原因 '+v.locked_reason:''));
   console.log('今日已計入／預留 Token：'+s.accounted_tokens_today+' / '+s.daily_local_token_limit);
   console.log('每日重置（台灣）：'+new Date(s.next_daily_reset).toLocaleString('zh-TW',{timeZone:'Asia/Taipei'}));
   console.log(s.free_tier_verification_mode==='project-pinned'?
     '免費方案：專案／金鑰／模型與額度已固定，不會每日到期；變更時需重新核對。':
     '免費方案驗證有效至：'+new Date(s.free_tier_verified_until).toLocaleString('zh-TW',{timeZone:'Asia/Taipei'}));
   console.log('Google 計費方案：本機確認紀錄；不是即時雲端查詢。請勿替此專案啟用付費。');
   console.log('API 金鑰狀態請用執行中的 Hub /usage 查詢；離線查詢不載入金鑰。');
  }
 }
 g.close();
}catch(e){console.error(e.code??'usage_query_failed');process.exitCode=1;}
