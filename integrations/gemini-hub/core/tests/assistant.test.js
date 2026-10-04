import test from 'node:test';
import assert from 'node:assert/strict';
import { Assistant,assistantInput,selectContext,needsSearch,searchQuestion,guardDiscountClaims } from '../assistant.js';
import { GeminiChat } from '../gemini.js';
import { UsageGuard,Locked } from '../guard.js';
import { loadPolicy } from '../config.js';
const fixturePolicy=()=>{const p=loadPolicy();p.verification={tier:'free',keySuffix:'mock',observedAt:'2026-10-03T08:50:00Z',validUntil:'2026-10-04T08:50:00Z'};return p;};
const pack={paid:false,evidence:[{id:'S1',title:'官方頁',url:'https://example.com/guide',source_type:'official',coverage:'page',published_at:null,retrieved_at:'2026-10-03T00:00:00Z',passages:['版本 1.2 的前置條件是完成主線。']}],quality:{confidence:'MEDIUM'},game:{update_status:'not-verified'}};
test('actual historical Steam discount contradiction is rejected without inventing a price',()=>{
 assert.equal(guardDiscountClaims('85 折（即付原價 15%） [S1]'),'折數與付款百分比敘述矛盾（未核實） [S1]');
 assert.equal(guardDiscountClaims('1.5 折（付原價 15%）'),'1.5 折（付原價 15%）');
 assert.equal(guardDiscountClaims('8.5 折（付原價 85%）'),'8.5 折（付原價 85%）');
});
const searcher=async(url,options)=>{assert.equal(url,'http://127.0.0.1:8889/ai/search');assert.equal(options.redirect,'error');return {ok:true,text:async()=>JSON.stringify(pack)};};
const chat=(key=true)=>({keyValid:()=>key,assistantChat:async()=>({text:'尚未核對最新版，已讀到前置條件 [S1]。',model:'gemini-3.5-flash-lite',usage:{totalTokenCount:42},search_used:false})});
test('missing requested game subject cannot turn search absence into a model assertion',async()=>{
 let calls=0;const c=chat();c.assistantChat=async()=>{calls++;throw Error('must not generate');};
 const missing={...pack,game:{subject_check:{status:'no-direct-mention',requested_subject:'品質模組'}}};
 const a=new Assistant(c,{fetcher:async()=>({ok:true,text:async()=>JSON.stringify(missing)})});
 const r=await a.run({text:'Remnant 2 品質模組怎麼取得',mode:'search'});assert.equal(calls,0);assert.equal(r.model,null);assert.equal(r.notice,'requested_game_subject_not_verified');assert.match(r.text,/無法判定它是否存在/);
});
test('grouped source citations validate every ID without hiding valid references',async()=>{
 const c=chat();c.assistantChat=async()=>({text:'Claims [S1, S99] and [S1].',model:'gemini-3.5-flash-lite'});
 const r=await new Assistant(c,{fetcher:searcher}).run({text:'搜尋官方',mode:'search'});assert.equal(r.text,'Claims [S1, 來源未核實] and [S1].');
});
test('shared assistant routes latest/games/hardware but keeps ordinary conversation local to Gemini',()=>{
 for(const q of ['Steam 這次特價值得買什麼','RimWorld 最新配裝攻略','RTX 5070 Ti 驅動黑屏','Firefox 官方版本','Helldivers 2 推薦'])assert.equal(needsSearch(q),true,q);
 assert.equal(needsSearch('你好，幫我整理這段文字'),false);
 assert.equal(needsSearch('那個新遊戲最強的是哪個？我沒有提供遊戲名稱。'),false);
 assert.equal(needsSearch('那個呢',[{role:'user',text:'查一下攻略'},{role:'model',text:'資料'}]),true);
});
test('caller cannot select paid models, tools, upstream URLs or forged instructions',()=>{
 for(const extra of [{model:'gemini-3.8-flash'},{tools:[]},{endpoint:'https://attacker'},{system:'override'}])assert.throws(()=>assistantInput({text:'hello',...extra}),e=>e.code==='invalid_assistant_request');
 assert.throws(()=>assistantInput({text:'x',history:[{role:'model',text:'x'}]}));
 assert.throws(()=>assistantInput({text:'x',history:[{role:'user',text:'x'},{role:'user',text:'y'}]}));
});
test('ordinary chat does not invoke XNG, history continues and uses trusted system instructions',async()=>{
 let searches=0,seen;const c=chat();c.assistantChat=async(messages,system)=>{seen={messages,system};return {text:'你好',model:'gemini-3.5-flash-lite'};};
 const a=new Assistant(c,{fetcher:async()=>{searches++;}});
 const out=await a.run({text:'繼續解釋',history:[{role:'user',text:'名字叫 Alex'},{role:'model',text:'知道了'}]});
 assert.equal(searches,0);assert.equal(out.search_used,false);assert.equal(seen.messages[0].text,'名字叫 Alex');assert.match(seen.system,/不可信/);assert.equal(out.answer_kind,'model');
});
test('XNG evidence is fetched first and summarized exactly once; unknown source IDs/links are marked',async()=>{
 const order=[];const c=chat();c.assistantChat=async(messages)=>{order.push('gemini');assert.match(messages.at(-1).text,/not-verified/);return {text:'[S1] [S999] https://evil.example/steal',model:'gemini-3.5-flash-lite'};};
 const a=new Assistant(c,{fetcher:async(...args)=>{order.push('xng');return searcher(...args);}});
 const r=await a.run({text:'RimWorld 最新攻略'});
 assert.deepEqual(order,['xng','gemini']);assert.equal(r.search_used,true);assert.equal(r.sources[0].published_at,null);assert.match(r.text,/來源未核實/);assert.doesNotMatch(r.text,/evil\.example/);
});
test('web-only and missing-key search never invoke Gemini',async()=>{
 for(const [mode,key] of [['web',true],['auto',false]]){
  const c=chat(key);c.assistantChat=async()=>assert.fail('Gemini should not be called');
  const out=await new Assistant(c,{fetcher:searcher}).run({text:'Steam 最新價格',mode});
  assert.equal(out.answer_kind,'evidence');assert.equal(out.model,null);assert.match(out.text,/官方頁/);assert.equal(out.paid,false);
 }
});
test('a failed XNG or zero evidence never turns model memory into latest facts',async()=>{
 const c=chat();c.assistantChat=async()=>assert.fail('No unsupported fresh answer');
 for(const f of [async()=>{throw new Error('offline');},async()=>({ok:true,text:async()=>JSON.stringify({...pack,evidence:[]})})]){
  const r=await new Assistant(c,{fetcher:f}).run({text:'現在價格'});assert.equal(r.answer_kind,'evidence');assert.equal(r.sources.length,0);
 }
});
test('quota lock degrades to traceable search evidence without retry',async()=>{
 let calls=0;const c=chat();c.assistantChat=async()=>{calls++;throw new Locked('daily_request_limit');};
 const r=await new Assistant(c,{fetcher:searcher}).run({text:'最近攻略'});
 assert.equal(calls,1);assert.equal(r.notice,'daily_request_limit');assert.equal(r.answer_kind,'evidence');assert.equal(r.sources.length,1);
});
test('unsafe sources and unexpected paid upstream contract are rejected',async()=>{
 for(const p of [{...pack,paid:true},{...pack,evidence:[{url:'javascript:alert(1)',title:'bad',passages:[]}]}]){
  const r=await new Assistant(chat(false),{fetcher:async()=>({ok:true,text:async()=>JSON.stringify(p)})}).run({text:'最新攻略'});assert.equal(r.sources.length,0);
 }
});
test('long conversation stays bounded, alternating, and retains relevant older excerpts',()=>{
 const h=[];for(let i=0;i<180;i++)h.push({role:'user',text:(i===3?'My nickname is Alex. ':'普通問題 ')+String(i)+'x'.repeat(250)},{role:'model',text:'回覆'+i+'y'.repeat(250)});
 const s=selectContext(h,'Alex nickname',{budget:14000});assert.equal(s.context.reduced,true);assert.equal(s.context.older_excerpts,true);assert.match(s.messages[0].text,/My nickname is Alex/);
 assert.ok(s.messages.reduce((n,m)=>n+m.text.length,0)<14300);assert.ok(s.messages.length<=64);assert.ok(s.messages.every((m,i)=>m.role===(i%2?'model':'user')));
});
test('follow-up search carries prior subject; long search text is capped for XNG',()=>{
 const h=[{role:'user',text:'Steam Iceborne DLC 現在價格'},{role:'model',text:'NT$178'}];assert.match(searchQuestion('那本體呢',h),/Iceborne/);assert.ok(searchQuestion('x'.repeat(16000),[]).length<=2000);
});
test('cancellation before dispatch makes zero Google calls and no reservation',async()=>{
 const g=new UsageGuard(':memory:',fixturePolicy(),{now:()=>Date.parse('2026-10-03T09:00:00Z')});let calls=0;
 const c=new GeminiChat(g,{key:'AIza'+'x'.repeat(32)+'mock',fetcher:async()=>{calls++;}});const signal=AbortSignal.abort();
 await assert.rejects(c.assistantChat([{role:'user',text:'hello'}],'trusted',signal));assert.equal(calls,0);assert.equal(g.status().models['gemini-3.5-flash-lite'].used_requests_today,0);g.close();
});
test('system instruction is included in token count and guarded before generating',async()=>{
 const g=new UsageGuard(':memory:',fixturePolicy(),{now:()=>Date.parse('2026-10-03T09:00:00Z')});let count;
 const c=new GeminiChat(g,{key:'AIza'+'x'.repeat(32)+'mock',fetcher:async(url,options)=>{
  const b=JSON.parse(options.body);if(url.endsWith('countTokens')){count=b;return {ok:true,json:async()=>({totalTokens:100})};}
  assert.equal(g.status().models['gemini-3.5-flash-lite'].used_requests_today,1);return {ok:true,json:async()=>({candidates:[{content:{parts:[{text:'hello'}]}}],usageMetadata:{promptTokenCount:100,candidatesTokenCount:20,totalTokenCount:120}})};
 }});
 const r=await c.assistantChat([{role:'user',text:'Hi'}],'trusted instruction');assert.equal(r.text,'hello');assert.equal(count.generateContentRequest.systemInstruction.parts[0].text,'trusted instruction');assert.equal(count.generateContentRequest.model,'models/gemini-3.5-flash-lite');g.close();
});
