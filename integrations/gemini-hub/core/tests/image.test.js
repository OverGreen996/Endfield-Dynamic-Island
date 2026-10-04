import test from 'node:test';
import assert from 'node:assert/strict';
import {imageInput,visionQuery,IMAGE_MAX_BYTES} from '../image.js';
import {Assistant,assistantInput} from '../assistant.js';
import {GeminiChat} from '../gemini.js';
import {UsageGuard} from '../guard.js';
import {loadPolicy} from '../config.js';
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=','base64');
const image={mime_type:'image/png',data:png.toString('base64')};
function policy(){const p=loadPolicy();p.verification={tier:'free',keySuffix:'test',observedAt:'2026-10-04T09:00:00Z',validUntil:'2026-10-05T09:00:00Z'};return p;}
const q='圖片上的物品最新攻略';
test('image entry accepts bounded inline PNG only; never URLs, files, excessive dimensions or caller tools',()=>{
 assert.deepEqual(imageInput(image),image);
 const huge=Buffer.from(png);huge.writeUInt32BE(10000,16);
 for(const v of [{url:'https://evil.example/image'}, {...image,mime_type:'image/svg+xml'}, {...image,data:'AAAA'}, {...image,data:'a'.repeat(Math.ceil(IMAGE_MAX_BYTES/3)*4+4)}, {...image,data:huge.toString('base64')}, {...image,tools:[]}])assert.throws(()=>imageInput(v));
 assert.throws(()=>assistantInput({text:q,image,mode:'web'}),e=>e.code==='image_requires_gemini');
 assert.match(assistantInput({text:'',image,mode:'chat'}).text,/描述/);
});
test('image-only chat counts the complete image and system instruction, then generates once',async()=>{
 const g=new UsageGuard(':memory:',policy(),{now:()=>Date.parse('2026-10-04T10:00:00Z')});let calls=0;
 const c=new GeminiChat(g,{key:'AIza'+'x'.repeat(32)+'test',fetcher:async(url,opts)=>{
  calls++;const body=JSON.parse(opts.body);
  const req=url.endsWith('countTokens')?body.generateContentRequest:body;
  assert.equal(req.contents.at(-1).parts.at(-1).inlineData.data,image.data);
  assert.equal(req.tools,undefined);assert.ok(req.systemInstruction);
  return {ok:true,json:async()=>url.endsWith('countTokens')?{totalTokens:350}:{usageMetadata:{promptTokenCount:350,candidatesTokenCount:10,totalTokenCount:360},candidates:[{content:{parts:[{text:'圖片為示範。'}]},finishReason:'STOP'}]}};
 }});
 const r=await c.visionChat([{role:'user',text:'描述圖片'}],'僅判讀',image);assert.equal(r.text,'圖片為示範。');assert.equal(calls,2);assert.equal(g.status().models['gemini-3.5-flash-lite'].used_requests_today,1);g.close();
});
test('image analysis routes through XNG before one final answer, with no image or key sent to XNG',async()=>{
 const order=[],c={keyValid:()=>true,visionChat:async()=>{order.push('vision');return {text:JSON.stringify({description:'GPU fault screenshot',search_query:'RTX 5070 Ti driver black screen',confident:true}),model:'gemini-3.5-flash-lite'};},
  assistantChat:async(messages)=>{order.push('answer');assert.match(messages.at(-1).text,/GPU fault/);return {text:'官方文件 [S1]',model:'gemini-3.5-flash-lite'};}};
 const fetcher=async(url,opts)=>{order.push('xng');const request=JSON.parse(opts.body);assert.equal(request.q,'RTX 5070 Ti driver black screen');assert.equal(request.image,undefined);assert.ok(!opts.body.includes(image.data));return {ok:true,text:async()=>JSON.stringify({paid:false,evidence:[{id:'S1',title:'官方',url:'https://example.com',source_type:'official',coverage:'page',passages:['官方證據']} ]})};};
 const r=await new Assistant(c,{fetcher}).run({text:q,image,mode:'auto'});assert.deepEqual(order,['vision','xng','answer']);assert.equal(r.gemini_completed_calls,2);assert.deepEqual(r.memory_suggestions,[]);
});
test('chat mode needs one vision call; ambiguity avoids searching and another Gemini call',async()=>{
 for(const mode of ['chat','auto']){
  let calls=0;const c={keyValid:()=>true,visionChat:async()=>{calls++;return {text:mode==='chat'?'描述':JSON.stringify({description:'看不清楚',search_query:'猜的型號',confident:false}),model:'gemini-3.5-flash-lite'};},assistantChat:()=>assert.fail('no extra Gemini')};
  const r=await new Assistant(c,{fetcher:()=>assert.fail('no guessed search')}).run({text:q,image,mode});assert.equal(calls,1);assert.equal(r.search_used,false);assert.equal(r.gemini_completed_calls,1);
 }
 assert.equal(visionQuery('not JSON').query,null);
});
test('quota lock or zero evidence during image search falls back without retry and never saves image memories',async()=>{
 for(const empty of [false,true]){
  let answerCalls=0;const c={keyValid:()=>true,visionChat:async()=>({text:JSON.stringify({description:'錯誤畫面',search_query:'app error',confident:true}),model:'gemini-3.5-flash-lite'}),assistantChat:async()=>{answerCalls++;throw new Error('locked');}};
  const a=new Assistant(c,{fetcher:async()=>({ok:true,text:async()=>JSON.stringify({paid:false,evidence:empty?[]:[{id:'S1',title:'file',url:'https://example.com',passages:['內容'],coverage:'page'}]})})});
  const r=await a.run({text:q,image});assert.equal(r.answer_kind,'evidence');assert.equal(answerCalls,empty?0:1);assert.equal(r.memory_suggestions,undefined);
 }
});
