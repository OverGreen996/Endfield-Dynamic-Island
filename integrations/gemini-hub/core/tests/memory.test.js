import test from 'node:test';
import assert from 'node:assert/strict';
import {memoryResponse} from '../memory.js';
import {Assistant} from '../assistant.js';
import {GeminiChat} from '../gemini.js';
import {UsageGuard} from '../guard.js';
import {loadPolicy} from '../config.js';
const reply=(category,quote)=>'了解，我會依情境回覆。\n<user_memory>'+JSON.stringify({memory:{category,quote}})+'</user_memory>';
test('direct name-address preferences are admitted but jokes, quotes and temporary requests are not',()=>{
 for(const q of ['不需要每次回答都叫我名字','不用每次都叫我的名字','不要一直叫我的全名','請不要叫我姓名'])
  assert.deepEqual(memoryResponse(reply('回答方式',q),q).memory_suggestions,[{category:'回答方式',quote:q}],q);
 for(const q of ['記住','好的','今天不要叫我名字','不要叫我名字，開玩笑的','不要叫我名字嗎？','他說不要叫我名字','不要記住我的名字'])
  assert.deepEqual(memoryResponse(reply('回答方式',q),q).memory_suggestions,[],q);
});
test('Gemini can classify first-person pets, work, preferences and boundaries using verbatim evidence',()=>{
 for(const [category,quote] of [['個人事項','我有養一條 黑王蛇'],['身分資料','我在遊戲公司做設計'],['喜好偏好','我偏好簡短的回答'],['互動禁忌','我不希望你替我做決定'],['生活習慣','我通常晚上十點休息'],['目標計畫','我的計畫是學日文']]){
  const parsed=memoryResponse(reply(category,quote),quote);assert.deepEqual(parsed.memory_suggestions,[{category,quote}]);assert.ok(!parsed.text.includes('user_memory'));
 }
});
test('required structured response cannot silently omit the memory decision',()=>{
 const q='我有養一條 黑王蛇';
 for(const raw of ['黑王蛇是很酷的寵物！',reply('個人事項',q),JSON.stringify({answer:'了解。'}),'{"answer":"了解。","memory":',JSON.stringify({answer:'',memory:null})])
  assert.throws(()=>memoryResponse(raw,q,{required:true}),e=>e.code==='invalid_assistant_response');
 const good=memoryResponse(JSON.stringify({answer:'照顧牠還順利嗎？',memory:{category:'個人事項',quote:q}}),q,{required:true});
 assert.equal(good.text,'照顧牠還順利嗎？');assert.deepEqual(good.memory_suggestions,[{category:'個人事項',quote:q}]);
 assert.deepEqual(memoryResponse(JSON.stringify({answer:'你好。',memory:null}),'你好',{required:true}).memory_suggestions,[]);
});
test('structured format does not bypass original-quote, category or joke checks',()=>{
 for(const [q,category,quote] of [['我養一隻兔子，開玩笑的','個人事項','我養一隻兔子，開玩笑的'],['假設我養兔子','個人事項','假設我養兔子'],['我有養兔子','個人事項','我喜歡小動物'],['我有養兔子','未知分類','我有養兔子'],['我可能養兔子','個人事項','我可能養兔子'],['我朋友養兔子','個人事項','我朋友養兔子']])
  assert.deepEqual(memoryResponse(JSON.stringify({answer:'了解。',memory:{category,quote}}),q,{required:true}).memory_suggestions,[],q);
});
test('real orchestration requests required schema in token preflight and generates once',async()=>{
 const p=loadPolicy();p.verification={tier:'free',keySuffix:'test',observedAt:'2026-10-04T09:00:00Z',validUntil:'2026-10-05T09:00:00Z'};
 const g=new UsageGuard(':memory:',p,{now:()=>Date.parse('2026-10-04T10:00:00Z')});const q='我有養一條 黑王蛇';let counts=0,generations=0;
 const c=new GeminiChat(g,{key:'AIza'+'x'.repeat(32)+'test',fetcher:async(url,options)=>{
  const b=JSON.parse(options.body),request=url.endsWith('countTokens')?b.generateContentRequest:b;
  assert.equal(request.generationConfig.responseFormat.text.mimeType,'APPLICATION_JSON');
  assert.deepEqual(request.generationConfig.responseFormat.text.schema.required,['answer','memory']);
  assert.equal(request.tools,undefined);
  if(url.endsWith('countTokens')){counts++;return {ok:true,json:async()=>({totalTokens:100})};}
  generations++;return {ok:true,json:async()=>({candidates:[{content:{parts:[{text:JSON.stringify({answer:'照顧牠還順利嗎？',memory:{category:'個人事項',quote:q}})}]},finishReason:'STOP'}],usageMetadata:{promptTokenCount:100,candidatesTokenCount:50,totalTokenCount:150}})};
 }});
 try{const result=await new Assistant(c).run({text:q,mode:'chat'});assert.equal(counts,1);assert.equal(generations,1);assert.equal(g.status().models['gemini-3.5-flash-lite'].used_requests_today,1);assert.deepEqual(result.memory_suggestions,[{category:'個人事項',quote:q}]);assert.equal(result.text,'照顧牠還順利嗎？');}
 finally{g.close();}
});
test('missing decision after generation is explicit failure without retry or false memory acknowledgment',async()=>{
 let calls=0;const c={keyValid:()=>true,assistantChat:async(_messages,_system,_signal,options)=>{calls++;assert.equal(options.classifyMemory,true);return {text:'照顧牠還順利嗎？',memory_response_required:true};}};
 await assert.rejects(new Assistant(c).run({text:'我有養一隻兔子',mode:'chat'}),e=>e.code==='invalid_assistant_response');assert.equal(calls,1);
});
test('jokes, examples, speculation, quotes, other people, questions and secrets cannot become memories',()=>{
 for(const q of ['假設我養一條黑王蛇','我有養一條黑王蛇，開玩笑的','他說我養了一條蛇','我可能養一條蛇','我養蛇嗎？','我今天養了一條蛇','我現在很生氣','我有養蛇。她養貓','我有養蛇，但是不要記','我叫神仙','我知道我的密碼是abc','我的 API Key 是 test-only','我的地址是秘密','我朋友養黑王蛇','「我有養一條蛇」'])
  assert.deepEqual(memoryResponse(reply('個人事項',q),q).memory_suggestions,[],q);
});
test('model inferences, unknown categories, malformed/truncated blocks and duplicate metadata are rejected',()=>{
 const q='我有養一條黑王蛇';
 for(const raw of [reply('個人事項','我喜歡爬蟲類'),reply('未知',q),'<user_memory>{broken}</user_memory>',reply('個人事項',q)+reply('個人事項',q),reply('個人事項',q)+'後續文字','<user_memory>{"memory":',reply('個人事項',q).replace('"quote"','"inferred_fact"')]){
  const p=memoryResponse(raw,q);assert.deepEqual(p.memory_suggestions,[]);assert.ok(!p.text.includes('user_memory'));
 }
});
test('classification shares exactly one answer call and never claims a database write',async()=>{
 let calls=0;const q='我有養一條黑王蛇';
 const c={keyValid:()=>true,assistantChat:async(messages,system)=>{calls++;assert.equal(messages.at(-1).text,q);assert.match(system,/沒有本機儲存回報前/);return {text:reply('個人事項',q),model:'gemini-3.5-flash-lite'};}};
 const a=await new Assistant(c).run({text:q,mode:'chat'});assert.equal(calls,1);assert.equal(a.memory_suggestions.length,1);assert.equal(a.search_used,false);
});
