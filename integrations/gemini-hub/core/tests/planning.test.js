import test from 'node:test';
import assert from 'node:assert/strict';
import {Assistant} from '../assistant.js';
import {localClock,resolveSearchDate,parsePlan,requiresEvidence} from '../planning.js';
import {GeminiChat} from '../gemini.js';
import {UsageGuard,Locked} from '../guard.js';
import {loadPolicy} from '../config.js';

const now=()=>Date.parse('2026-10-04T15:59:59Z');
const pack={paid:false,evidence:[{title:'官方公告',url:'https://example.com/news',source_type:'official',coverage:'page',passages:['經核實的官方內容。']} ]};
const json=(action,answer='',search_query='',memory=null)=>JSON.stringify({action,answer,search_query,memory});
const c=(raw)=>({keyValid:()=>true,planChat:async()=>({text:raw,model:'gemini-3.5-flash-lite',usage:{totalTokenCount:50}}),assistantChat:async()=>({text:JSON.stringify({answer:'官方資料 [S1]',memory:null}),memory_response_required:true,model:'gemini-3.5-flash-lite',usage:{totalTokenCount:60}})});

test('Taipei trusted clock resolves midnight, year rollover, leap day and weekday-independent dates',()=>{
 for(const [instant,date,tomorrow] of [['2026-10-04T15:59:59Z','2026-10-04','2026-10-05'],['2026-10-04T16:00:00Z','2026-10-05','2026-10-06'],['2026-12-31T15:00:00Z','2026-12-31','2027-01-01'],['2028-02-28T15:00:00Z','2028-02-28','2028-02-29']]) {
  const clock=localClock(Date.parse(instant));assert.equal(clock.date,date);assert.equal(clock.tomorrow,tomorrow);assert.equal(clock.timezone,'Asia/Taipei');
 }
});
test('tomorrow holiday query is given an absolute date without duplicate date or automatic region override',()=>{
 const clock=localClock(now());
 assert.equal(resolveSearchDate('台灣 明天 節日','明天是什麼節日',clock),'台灣 2026-10-05 節日');
 assert.equal(resolveSearchDate('日本 2026-10-05 祝日','日本明天放假嗎',clock),'日本 2026-10-05 祝日');
 assert.equal(resolveSearchDate('美國 holiday','美國明天什麼節日',clock),'美國 holiday 2026-10-05');
});
test('freshness questions require evidence while personal statements remain one-call chat',()=>{
 for(const q of ['明天是什麼節日','近期活動','Firefox 最新版本','Steam 這次特價','latest Firefox release','tomorrow holiday'])assert.equal(requiresEvidence(q),true,q);
 for(const q of ['你好','我有養一隻兔子','我偏好簡短回答','請用繁體中文回答'])assert.equal(requiresEvidence(q),false,q);
});
test('all auto text gets Gemini understanding first, including formerly missed activity query',async()=>{
 const order=[],chat=c(json('search','','《明日方舟：終末地》 近期活動 官方公告'));
 chat.planChat=async(_m,instructions)=>{order.push('plan');assert.match(instructions,/2026-10-04/);return {text:json('search','','《明日方舟：終末地》 近期活動 官方公告'),model:'gemini-3.5-flash-lite'};};
 const answer=chat.assistantChat;chat.assistantChat=async(...args)=>{order.push('answer');return answer(...args);};
 const r=await new Assistant(chat,{now,fetcher:async(_u,o)=>{order.push('xng');assert.equal(JSON.parse(o.body).q,'《明日方舟：終末地》 近期活動 官方公告');return {ok:true,text:async()=>JSON.stringify(pack)};}}).run({text:'明日方舟 終末地 近期活動'});
 assert.deepEqual(order,['plan','xng','answer']);assert.equal(r.planning.used,true);assert.equal(r.gemini_completed_calls,2);assert.equal(r.search_used,true);
});
test('ordinary conversation and memory classification complete in the first call without search or another generation',async()=>{
 const q='我有養一隻兔子';let plans=0;
 const chat=c(json('answer','照顧牠順利嗎？','',{category:'個人事項',quote:q}));chat.planChat=async()=>{plans++;return {text:json('answer','照顧牠順利嗎？','',{category:'個人事項',quote:q})};};chat.assistantChat=()=>assert.fail('no second call');
 const r=await new Assistant(chat,{now,fetcher:()=>assert.fail('no search')}).run({text:q});
 assert.equal(plans,1);assert.equal(r.text,'照顧牠順利嗎？');assert.deepEqual(r.memory_suggestions,[{category:'個人事項',quote:q}]);assert.equal(r.gemini_completed_calls,1);
});
test('ambiguity asks once instead of inventing an unnamed game or issuing a query',async()=>{
 const r=await new Assistant(c(json('clarify','請提供遊戲名稱。')),{now,fetcher:()=>assert.fail('no guessed search')}).run({text:'那個遊戲最新活動呢'});
 assert.equal(r.planning.action,'clarify');assert.equal(r.search_used,false);assert.deepEqual(r.memory_suggestions,[]);
});
test('follow-up interpretation preserves previous subject but only sends planned query to XNG',async()=>{
 const chat=c(json('search','','Steam Monster Hunter World 本體 台灣 TWD 價格'));
 chat.planChat=async(messages)=>{assert.match(messages[0].text,/Iceborne/);return {text:json('search','','Steam Monster Hunter World 本體 台灣 TWD 價格')};};
 const r=await new Assistant(chat,{now,fetcher:async(_u,o)=>{const body=JSON.parse(o.body);assert.equal(body.q,'Steam Monster Hunter World 本體 台灣 TWD 價格');assert.equal(body.history,undefined);return {ok:true,text:async()=>JSON.stringify(pack)};}}).run({text:'那本體呢',history:[{role:'user',text:'Iceborne DLC 台灣價格'},{role:'model',text:'資料'}]});assert.equal(r.gemini_completed_calls,2);
});
test('zero evidence spends only planning call and cannot become an unsupported latest answer',async()=>{
 const chat=c(json('search','','官方 最新活動'));chat.assistantChat=()=>assert.fail('no unsupported answer');
 const r=await new Assistant(chat,{now,fetcher:async()=>({ok:true,text:async()=>JSON.stringify({...pack,evidence:[]})})}).run({text:'近期活動'});
 assert.equal(r.answer_kind,'evidence');assert.equal(r.gemini_completed_calls,1);assert.equal(r.notice,'no_usable_evidence');
});
test('planner lock degrades once to free search with explicit error, no retry or answer call',async()=>{
 let plans=0,searches=0;const chat=c('');chat.planChat=async()=>{plans++;throw new Locked('daily_request_limit',429);};chat.assistantChat=()=>assert.fail('must not retry');
 const r=await new Assistant(chat,{now,fetcher:async()=>{searches++;return {ok:true,text:async()=>JSON.stringify(pack)};}}).run({text:'明天是什麼節日'});
 assert.equal(plans,1);assert.equal(searches,1);assert.equal(r.planning.error,'daily_request_limit');assert.equal(r.gemini_completed_calls,0);assert.match(r.search_query,/2026-10-05/);
});
test('a locked planner never forwards private casual conversation merely because it says today or game',async()=>{
 const chat=c('');chat.planChat=async()=>{throw new Locked('daily_request_limit',429);};
 for(const q of ['我今天心情很差','我在遊戲公司做設計'])await assert.rejects(new Assistant(chat,{now,fetcher:()=>assert.fail('no private fallback query')}).run({text:q}),e=>e.code==='daily_request_limit');
});
test('an unconfigured planner never forwards private casual conversation to XNG',async()=>{
 const chat=c('');chat.keyValid=()=>false;chat.assistantChat=async()=>{throw new Locked('api_key_not_configured_or_mismatch',503);};
 await assert.rejects(new Assistant(chat,{now,fetcher:()=>assert.fail('no private fallback query')}).run({text:'我在遊戲公司做設計'}),e=>e.code==='api_key_not_configured_or_mismatch');
});
test('game activities cannot imply verified live participation without an event-window check',async()=>{
 const chat=c(json('search','','《遊戲》 活動 官方公告'));
 const r=await new Assistant(chat,{now,fetcher:async()=>({ok:true,text:async()=>JSON.stringify({...pack,game:{version_check:{latest_status:'unverified'}}})})}).run({text:'遊戲近期活動'});
 assert.match(r.text,/起訖日期與目前可參加狀態尚未核實/);
});
test('web-only keeps zero Gemini cost and pure chat keeps one answer call with a trusted clock',async()=>{
 for(const mode of ['web','chat']) {
  const chat=c('');chat.planChat=()=>assert.fail('explicit modes do not use planner');
  chat.assistantChat=async(_m,instructions)=>{assert.match(instructions,/2026-10-04/);return {text:JSON.stringify({answer:'你好',memory:null}),memory_response_required:true};};
  const r=await new Assistant(chat,{now,fetcher:async()=>({ok:true,text:async()=>JSON.stringify(pack)})}).run({text:'你好',mode});assert.equal(r.gemini_completed_calls??0,mode==='web'?0:1);
 }
});
test('malformed plans, hidden fields, credential queries and search-time memories fail locally',()=>{
 const clock=localClock(now());
 for(const raw of ['plain answer',json('search','already known','query'),json('search','',''),json('answer','ok','query'),JSON.stringify({action:'answer',answer:'ok',search_query:'',memory:null,tools:[]}),json('search','','Bearer secret-token'),json('search','','query',{category:'個人事項',quote:'我養兔子'})])assert.throws(()=>parsePlan(raw,'question',clock),e=>e instanceof Locked);
});
test('cancellation after understanding never starts XNG or another Gemini generation',async()=>{
 const abort=new AbortController(),chat=c('');chat.planChat=async()=>{abort.abort();return {text:json('search','','官方 更新')};};
 const r=new Assistant(chat,{now,fetcher:async(_u,o)=>{o.signal.throwIfAborted();assert.fail('must not dispatch');}}).run({text:'最新更新'},abort.signal);
 await assert.rejects(r);
});
test('real Gemini planning and answering both count schema/system and reserve distinct generations',async()=>{
 const p=loadPolicy();p.verification={tier:'free',keySuffix:'test',observedAt:'2026-10-04T09:00:00Z',validUntil:'2026-10-05T09:00:00Z'};
 const guard=new UsageGuard(':memory:',p,{now:()=>Date.parse('2026-10-04T10:00:00Z')});let counts=0,generations=0;
 const chat=new GeminiChat(guard,{key:'AIza'+'x'.repeat(32)+'test',fetcher:async(url,o)=>{
  const body=JSON.parse(o.body),request=url.endsWith('countTokens')?body.generateContentRequest:body;assert.equal(request.tools,undefined);assert.equal(request.generationConfig.responseFormat.text.mimeType,'APPLICATION_JSON');assert.match(request.systemInstruction.parts[0].text,/Asia\/Taipei/);
  if(request.generationConfig.responseFormat.text.schema.properties.action)assert.deepEqual(request.generationConfig.responseFormat.text.schema.properties.action.enum,['search','clarify']);
  if(url.endsWith('countTokens')){counts++;return {ok:true,json:async()=>({totalTokens:100})};}
  generations++;const text=generations===1?json('search','','台灣 2026-10-05 國定假日 行政院人事行政總處'):JSON.stringify({answer:'官方資料 [S1]',memory:null});
  return {ok:true,json:async()=>({usageMetadata:{promptTokenCount:100,candidatesTokenCount:50,totalTokenCount:150},candidates:[{content:{parts:[{text}]},finishReason:'STOP'}]})};
 }});
 try{const r=await new Assistant(chat,{now,fetcher:async()=>({ok:true,text:async()=>JSON.stringify(pack)})}).run({text:'明天是什麼節日'});assert.equal(counts,2);assert.equal(generations,2);assert.equal(guard.status().models['gemini-3.5-flash-lite'].used_requests_today,2);assert.equal(r.gemini_completed_calls,2);assert.equal(r.planning_usage.totalTokenCount,150);}
 finally{guard.close();}
});
