import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync,readFileSync,writeFileSync,rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Worker } from 'node:worker_threads';
import { request } from 'node:http';
import { UsageGuard,pacificDay,nextReset,validatePolicy } from '../guard.js';
import { GeminiChat,textRequest } from '../gemini.js';
import { createHub } from '../server.js';
import { loadPolicy } from '../config.js';
const lite='gemini-3.5-flash-lite',flash='gemini-3.8-flash';
const base=Date.parse('2026-10-03T09:00:00Z');
const policy=()=>{const p=loadPolicy();p.verification={tier:'free',keySuffix:'mock',observedAt:'2026-10-03T08:50:00Z',validUntil:'2026-10-04T08:50:00Z'};return p;};
const fixture=(changes={})=>new UsageGuard(':memory:',{...policy(),...changes},{now:()=>base});
const usage=(prompt=10,candidate=20,thought=5)=>({promptTokenCount:prompt,candidatesTokenCount:candidate,thoughtsTokenCount:thought,totalTokenCount:prompt+candidate+thought});
const message={messages:[{role:'user',text:'請說明為什麼天空是藍色。'}]};
const mockKey='AIza'+'x'.repeat(32)+'mock';
test('standard and authorization keys match the verified project without accepting malformed credentials',()=>{
 const g=fixture();
 for(const key of [mockKey,'AQ.'+'x'.repeat(46)+'mock'])assert.equal(new GeminiChat(g,{key}).keyValid(),true);
 for(const key of ['AQ.shortmock','AQ.'+'x'.repeat(46)+'other','AQ.'+'x'.repeat(46)+'mock\n','Bearer '+mockKey])assert.equal(new GeminiChat(g,{key}).keyValid(),false);
 g.close();
});
const throws=(f,code)=>assert.throws(f,e=>e.code===code);

test('verified project limits and default lite are preserved',()=>{
 const p=validatePolicy(policy());assert.equal(p.defaultModel,lite);assert.deepEqual(p.models[lite].official,{rpm:15,tpm:250000,rpd:500});assert.deepEqual(Object.keys(p.models),[lite]);assert.equal(p.dailyRequests,450);
});
test('paid, tools, unknown models and excessive limits are rejected',()=>{
 for(const k of ['paidAllowed','toolsAllowed'])throws(()=>fixture({[k]:true}),'invalid_policy');
 const p=policy();p.models[lite].local.rpd=501;throws(()=>validatePolicy(p),'invalid_model_policy');
 const q=policy();q.models['gemini-pro-paid']=q.models[lite];throws(()=>validatePolicy(q),'invalid_model_policy');
});
test('expired, future and disabled verification fail closed',()=>{
 const g=fixture({verification:{...policy().verification,validUntil:'2026-10-03T08:55:00Z'}});throws(()=>g.reserve(lite,5),'free_tier_verification_expired');g.close();
 const d=fixture({enabled:false});throws(()=>d.reserve(lite,5),'disabled');d.close();
});
test('daily quota is reserved before a call, finished calls still count',()=>{
 const p=policy();p.models[lite].local.rpd=2;const g=fixture(p);
 g.finish(g.reserve(lite,10),usage());g.finish(g.reserve(lite,10),usage());
 throws(()=>g.reserve(lite,10),'daily_request_limit');assert.equal(g.status().models[lite].used_requests_today,2);g.close();
});
test('rolling minute quota and minute input TPM reject before sending',()=>{
 const p=policy();p.models[lite].local.rpm=1;const g=fixture(p);g.reserve(lite,10);throws(()=>g.reserve(lite,10),'minute_request_limit');g.close();
 const q=policy();q.models[lite].local.tpm=500;const h=fixture(q);h.reserve(lite,10);throws(()=>h.reserve(lite,10),'minute_input_token_limit');h.close();
});
test('long inputs and non-integer token counts cannot bypass limits',()=>{
 const g=fixture();throws(()=>g.reserve(lite,g.p.maxInputTokens),'input_token_limit');for(const n of [0,-1,NaN,Infinity,1.5,'12'])throws(()=>g.reserve(lite,n),'invalid_token_count');assert.equal(g.status().models[lite].used_requests_today,0);g.close();
});
test('project daily budget applies in addition to model limits',()=>{
 const g=fixture({dailyTotalTokens:66000});g.reserve(lite,10);throws(()=>g.reserve(lite,10),'daily_token_limit');g.close();
 const h=fixture({dailyRequests:1});h.reserve(lite,10);throws(()=>h.reserve(lite,10),'daily_request_limit');h.close();
});
test('3.8 is rejected in configuration and chat before any provider request',async()=>{
 const p=policy();p.models[flash]=structuredClone(p.models[lite]);
 throws(()=>validatePolicy(p),'invalid_model_policy');
 const g=fixture();let calls=0;
 const c=new GeminiChat(g,{key:mockKey,fetcher:async()=>{calls++;}});
 await assert.rejects(c.chat({...message,model:flash}),e=>e.code==='model_not_allowed');
 assert.equal(calls,0);assert.equal(g.status().models[lite].used_requests_today,0);g.close();
});
test('thinking tokens are counted, pending reservation releases only on valid usage',()=>{
 const g=fixture();const id=g.reserve(lite,10);assert.equal(g.status().accounted_tokens_today,65802);
 g.finish(id,usage());const s=g.status();assert.equal(s.accounted_tokens_today,291);assert.equal(s.unknown_or_inflight_requests,0);g.close();
});
test('missing or inconsistent usage locks and retains conservative reservation',()=>{
 for(const u of [null,{},usage(-1),{...usage(),totalTokenCount:1},{...usage(),toolUsePromptTokenCount:1}]){
  const g=fixture();assert.equal(g.finish(g.reserve(lite,10),u),false);
  throws(()=>g.reserve(lite,10),'unknown_usage_lock');assert.equal(g.status().accounted_tokens_today,65802);g.close();
 }
});
test('actual usage exceeding reservation triggers persistent hard lock',()=>{
 const g=fixture();g.finish(g.reserve(lite,10),usage(9000));throws(()=>g.reserve(lite,10),'reservation_exceeded_lock');g.close();
});
test('429 and transport failures retain request/token quota without retry',()=>{
 for(const status of [429,400,0]){
  const g=fixture();g.fail(g.reserve(lite,10),status);assert.equal(g.status().models[lite].used_requests_today,1);
  throws(()=>g.reserve(lite,10),status===429?'provider_429_lock':'unknown_usage_lock');g.close();
 }
});
test('received HTTP 500/503 cool down without retry or releasing conservative quota',async()=>{
 for(const status of [500,503]){
  let now=base,calls=0;
  const g=new UsageGuard(':memory:',policy(),{now:()=>now});
  const c=new GeminiChat(g,{key:mockKey,fetcher:async(url)=>{
   calls++;return url.endsWith('countTokens')?{ok:true,json:async()=>({totalTokens:10})}:{ok:false,status};
  }});
  await assert.rejects(c.chat(message),e=>e.code==='provider_http_'+status);
  assert.equal(calls,2);assert.equal(g.status().accounted_tokens_today,65802);
  assert.equal(g.status().models[lite].used_requests_today,1);
  assert.equal(g.status().unknown_or_inflight_requests,0);
  await assert.rejects(c.chat(message),e=>e.code==='provider_temporary_unavailable');assert.equal(calls,2);
  now+=60000;assert.equal(g.status(true).models[lite].callable,true);
  const success=new GeminiChat(g,{key:mockKey,fetcher:async(url)=>url.endsWith('countTokens')?
   {ok:true,json:async()=>({totalTokens:10})}:{ok:true,json:async()=>({usageMetadata:usage(),candidates:[{content:{parts:[{text:'restored'}]}}]})}});
  assert.equal((await success.chat(message)).text,'restored');assert.equal(g.status().models[lite].used_requests_today,2);
  assert.equal(g.status().accounted_tokens_today,65802+291);g.close();
 }
});
test('old recorded 503 lock is repaired on restart without resetting quota',()=>{
 const dir=mkdtempSync(join(tmpdir(),'gemini-503-')),path=join(dir,'use.sqlite');let now=base;
 let g=new UsageGuard(path,policy(),{initialize:true,now:()=>now});const id=g.reserve(lite,10);
 g.db.prepare("UPDATE events SET status='uncertain',reason='http_503' WHERE id=?").run(id);
 g.lock(lite,'unknown_usage_lock',Date.parse(nextReset(now)));g.close();
 now+=60001;g=new UsageGuard(path,policy(),{now:()=>now});
 assert.equal(g.status(true).models[lite].callable,true);assert.equal(g.status().accounted_tokens_today,65802);
 assert.equal(g.status().models[lite].used_requests_today,1);assert.equal(g.status().unknown_or_inflight_requests,0);
 g.close();rmSync(dir,{recursive:true,force:true});
});
test('503 repair preserves independent unknown transport, in-flight and hard policy locks',()=>{
 for(const other of ['transport','inflight','overrun','429']){
  const g=fixture(),id=g.reserve(lite,10),second=g.reserve(lite,10);
  if(other==='transport')g.fail(second,0);
  if(other==='429')g.fail(second,429);
  if(other==='overrun')g.finish(second,usage(9000));
  g.db.prepare("UPDATE events SET status='uncertain',reason='http_503' WHERE id=?").run(id);
  if(other==='inflight')g.lock(lite,'unknown_usage_lock',Date.parse(nextReset(base)));
  assert.equal(g.reconcileProviderFailures(),1);
  throws(()=>g.reserve(lite,10),other==='429'?'provider_429_lock':other==='overrun'?'reservation_exceeded_lock':'unknown_usage_lock');g.close();
 }
 const g=fixture(),a=g.reserve(lite,10),b=g.reserve(lite,10);g.fail(a,0);g.fail(b,503);
 throws(()=>g.reserve(lite,10),'unknown_usage_lock');g.close();
});
test('same reservation cannot be reconciled twice',()=>{
 const g=fixture();const id=g.reserve(lite,10);g.finish(id,usage());throws(()=>g.finish(id,usage()),'reservation_state');g.close();
});
test('Google Pacific day and DST reset use timezone rather than fixed Taiwan hour',()=>{
 assert.equal(pacificDay(Date.parse('2026-10-03T06:59:59Z')),'2026-10-02');
 assert.equal(pacificDay(Date.parse('2026-10-03T07:00:00Z')),'2026-10-03');
 assert.equal(nextReset(Date.parse('2026-10-03T08:00:00Z')),'2026-10-04T07:00:00.000Z');
 assert.equal(nextReset(Date.parse('2026-01-03T09:00:00Z')),'2026-01-04T08:00:00.000Z');
 assert.equal(nextReset(Date.parse('2026-11-01T08:00:00Z')),'2026-11-02T08:00:00.000Z');
});
test('rolling window releases after 60 seconds, day resets at Pacific midnight',()=>{
 let now=Date.parse('2026-10-03T06:59:30Z');const p=policy();p.verification.observedAt='2026-10-02T23:00:00Z';p.verification.validUntil='2026-10-03T23:00:00Z';p.models[lite].local.rpd=1;
 const g=new UsageGuard(':memory:',p,{now:()=>now});g.reserve(lite,10);throws(()=>g.reserve(lite,10),'daily_request_limit');now+=60000;assert.ok(g.reserve(lite,10));g.close();
});
test('persistent ledger survives restart, crash reservation, and clock rollback',()=>{
 const dir=mkdtempSync(join(tmpdir(),'gemini-guard-')),path=join(dir,'use.sqlite');let now=base;
 let g=new UsageGuard(path,policy(),{initialize:true,now:()=>now});g.reserve(lite,10);g.close();
 g=new UsageGuard(path,policy(),{now:()=>now});assert.equal(g.status().models[lite].used_requests_today,1);now-=1;throws(()=>g.reserve(lite,10),'clock_rollback');g.close();rmSync(dir,{recursive:true,force:true});
});
test('deleted/corrupt ledger fails closed rather than resetting quota',()=>{
 const dir=mkdtempSync(join(tmpdir(),'gemini-corrupt-')),path=join(dir,'use.sqlite');
 throws(()=>new UsageGuard(path,policy()),'usage_database_missing');
 writeFileSync(path+'.initialized','1');throws(()=>new UsageGuard(path,policy(),{initialize:true}),'usage_database_missing');
 writeFileSync(path,'broken');assert.throws(()=>new UsageGuard(path,policy()));rmSync(dir,{recursive:true,force:true});
});
test('multiple independent workers cannot race past shared request limit',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'gemini-race-')),path=join(dir,'use.sqlite');const p=policy();p.models[lite].local.rpd=2;
 const g=new UsageGuard(path,p,{initialize:true,now:()=>base});
 const source=`const {parentPort,workerData}=require('node:worker_threads');(async()=>{const {UsageGuard}=await import(workerData.module);const g=new UsageGuard(workerData.path,workerData.policy,{now:()=>workerData.now});try{g.reserve(workerData.model,10);parentPort.postMessage(true);}catch(e){parentPort.postMessage(e.code);}finally{g.close();}})();`;
 const results=await Promise.all(Array.from({length:8},()=>new Promise((resolve,reject)=>{
  const w=new Worker(source,{eval:true,workerData:{module:new URL('../guard.js',import.meta.url).href,path,policy:p,now:base,model:lite}});
  w.once('message',resolve);w.once('error',reject);
 })));
 assert.equal(results.filter(v=>v===true).length,2);assert.equal(g.status().models[lite].used_requests_today,2);g.close();rmSync(dir,{recursive:true,force:true});
});
test('local usage status does not invoke the provider or pretend to know global remaining',()=>{
 const g=fixture();let calls=0;new GeminiChat(g,{fetcher:()=>{calls++;throw Error();}});for(let i=0;i<10;i++)g.status();
 assert.equal(calls,0);assert.equal(g.status().google_live_remaining,null);assert.equal(g.status().models[lite].used_requests_today,0);g.close();
});
test('tools, files, caller token counts, arbitrary URLs and generation settings are refused',()=>{
 const p=policy();
 for(const extra of [{tools:[]},{input_tokens:1},{endpoint:'https://evil.example'},{generationConfig:{}},{search:true},{systemInstruction:'x'},{stream:true}])
  throws(()=>textRequest({...message,...extra},p),'text_chat_only');
 throws(()=>textRequest({messages:[{role:'user',text:'x',file:'a.png'}]},p),'invalid_text_message');
 throws(()=>textRequest({...message,max_output_tokens:2049},p),'output_token_limit');
});
test('no credential, mismatched key, or unverified free tier sends any Google request',async()=>{
 for(const key of ['',mockKey.slice(0,-4)+'wrong']){
  const g=fixture();let calls=0;const c=new GeminiChat(g,{key,fetcher:()=>{calls++;}});
  await assert.rejects(c.chat(message),e=>e.code==='api_key_not_configured_or_mismatch');assert.equal(calls,0);g.close();
 }
});
test('text chat preflights input and reserves before generating; no tools are sent',async()=>{
 const g=fixture();const seen=[];const c=new GeminiChat(g,{key:mockKey,fetcher:async(url,opts)=>{
  seen.push(url);const body=JSON.parse(opts.body);assert.equal(body.tools,undefined);assert.equal(opts.redirect,'error');assert.ok(!url.includes(mockKey));
  if(url.endsWith(':countTokens'))return {ok:true,json:async()=>({totalTokens:10})};
  assert.equal(g.status().models[lite].used_requests_today,1);assert.equal(body.generationConfig.candidateCount,1);assert.equal(body.generationConfig.maxOutputTokens,2048);
  return {ok:true,json:async()=>({usageMetadata:usage(),candidates:[{content:{parts:[{text:'測試回答'}]},finishReason:'STOP'}]})};
 }});
 const r=await c.chat(message);assert.equal(r.text,'測試回答');assert.equal(r.search_used,false);assert.equal(seen.length,2);g.close();
});
test('failed token counting or oversized input never generates',async()=>{
 for(const response of [{ok:false,status:500},{ok:true,json:async()=>({totalTokens:policy().maxInputTokens})},{ok:true,json:async()=>({totalTokens:'10'})}]){
  const g=fixture();let calls=0;const c=new GeminiChat(g,{key:mockKey,fetcher:async()=>{calls++;return response;}});
  await assert.rejects(c.chat(message));assert.equal(calls,1);assert.equal(g.status().models[lite].used_requests_today,0);g.close();
 }
});
test('429 counting stops future preflight calls and never auto changes model',async()=>{
 const g=fixture();let calls=0;const c=new GeminiChat(g,{key:mockKey,fetcher:async()=>{calls++;return {ok:false,status:429};}});
 await assert.rejects(c.chat(message));await assert.rejects(c.chat(message),e=>e.code==='provider_429_lock');assert.equal(calls,1);g.close();
});
test('provider error is sanitized and failed generation is counted without retry',async()=>{
 const g=fixture();let calls=0;const c=new GeminiChat(g,{key:mockKey,fetcher:async(url)=>{calls++;if(url.endsWith('countTokens'))return {ok:true,json:async()=>({totalTokens:10})};throw Error('SECRET_KEY_OR_PROMPT');}});
 await assert.rejects(c.chat(message),e=>e.code==='provider_transport_error'&&!e.message.includes('SECRET'));assert.equal(calls,2);assert.equal(g.status().models[lite].used_requests_today,1);g.close();
});
test('preflight calls have their own bounded rate even when generation is rejected',()=>{
 const g=fixture({preflightPerMinute:2});g.preflight(lite);g.preflight(lite);throws(()=>g.preflight(lite),'preflight_rate_limit');g.close();
});
test('local API requires authentication and refuses browser-origin/DNS-rebinding requests',async()=>{
 const g=fixture();let calls=0;const chat=new GeminiChat(g,{fetcher:async()=>{calls++;}});const token='a'.repeat(64);
 const server=createHub(g,chat,token);await new Promise(r=>server.listen(0,'127.0.0.1',r));const url='http://127.0.0.1:'+server.address().port;
 try{
  let r=await fetch(url+'/usage');assert.equal(r.status,403);
  const headers={authorization:'Bearer '+token};
  r=await fetch(url+'/usage',{headers});assert.equal(r.status,200);assert.equal((await r.json()).scope,'local_hub_only');
  r=await fetch(url+'/usage',{headers:{...headers,origin:'https://evil.example'}});assert.equal(r.status,403);
  const badHost=await new Promise((resolve,reject)=>{const req=request(url+'/usage',{headers:{...headers,host:'evil.example:8890'}},r=>{r.resume();resolve(r.statusCode);});req.on('error',reject);req.end();});assert.equal(badHost,403);
  r=await fetch(url+'/chat',{method:'POST',headers:{...headers,'content-type':'application/json'},body:JSON.stringify(message)});assert.equal(r.status,503);assert.equal(calls,0);
 }finally{await new Promise(r=>server.close(r));g.close();}
});
