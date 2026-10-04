import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {XngConnection} from '../xng.js';
import {createHub} from '../server.js';
import {Assistant} from '../assistant.js';
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'island-xng-'));
test.after(()=>{assert.equal(path.dirname(path.resolve(temp)),path.resolve(os.tmpdir()));fs.rmSync(temp,{recursive:true,force:true});});
function fixture(name){const dir=path.join(temp,name);fs.mkdirSync(path.join(dir,'.runtime'),{recursive:true});for(const file of ['Start-XNG.ps1','Manage-XNGPlugin.ps1'])fs.writeFileSync(path.join(dir,file),'fixture');return dir;}
function setup(dir,extra={}){fs.writeFileSync(path.join(dir,'xng-setup.json'),JSON.stringify({schema:1,id:'xng-one-click'}));fs.writeFileSync(path.join(dir,'.runtime/connection.json'),JSON.stringify({port:19889,searxng_url:'http://127.0.0.1:19888',...extra}));}
const health=(dir,extra={})=>({service:'XNG AI Search Hub',schema_version:1,paid:false,state_directory:path.join(dir,'.runtime'),endpoint:'http://127.0.0.1:19888',plugin:{version:'2026.10.03-2058'},rules:{version:'2026.10.03-1'},...extra});
const response=value=>({ok:true,text:async()=>JSON.stringify(value)});
test('shared discovery preserves existing canonical root, schema and versions',async()=>{
 const dir=fixture('canonical');let calls=0;
 const xng=new XngConnection({standaloneRoot:dir,setupRoot:path.join(temp,'none'),fetcher:async(url,options)=>{calls++;assert.equal(url,'http://127.0.0.1:8889/health');assert.equal(options.redirect,'error');return response(health(dir));}});
 const state=await xng.status();assert.equal(state.connected,true);assert.equal(state.installation,'standalone');assert.equal(state.core_version,'2026.10.03-2058');assert.equal(await xng.endpoint(),'http://127.0.0.1:8889/ai/search');assert.equal(calls,1);
});
test('one-click custom port and backend are reused; manual status bypasses discovery cache',async()=>{
 const dir=fixture('setup');setup(dir);let calls=0;
 const xng=new XngConnection({standaloneRoot:path.join(temp,'none'),setupRoot:dir,fetcher:async(url)=>{calls++;assert.equal(url,'http://127.0.0.1:19889/health');return response(health(dir));}});
 assert.equal(await xng.endpoint(),'http://127.0.0.1:19889/ai/search');assert.equal((await xng.status()).installation,'setup');assert.equal(calls,1);await xng.status({refresh:true});assert.equal(calls,2);
});
test('wrong installation ownership, paid service, schema or backend cannot be selected',async()=>{
 const old=fixture('old'),dir=fixture('fallback');setup(dir);
 for(const extra of [{paid:true},{schema_version:2},{service:'other'},{state_directory:path.join(temp,'somebody-else')},{endpoint:'http://127.0.0.1:9999'}]){
  const xng=new XngConnection({standaloneRoot:path.join(temp,'none'),setupRoot:dir,fetcher:async()=>response(health(dir,extra))});assert.equal((await xng.status()).connected,false);
 }
 const xng=new XngConnection({standaloneRoot:old,setupRoot:dir,fetcher:async url=>response(health(url.includes(':8889/')?dir:dir))});
 assert.equal((await xng.status()).installation,'setup');
});
test('invalid setup metadata never creates a remote request or contacts Gemini port',async()=>{
 const dir=fixture('invalid');let calls=0;
 for(const extra of [{port:8890},{port:80},{port:70000},{port:'19889'},{searxng_url:'https://evil.example'},{searxng_url:'http://127.0.0.1.evil.example'},{searxng_url:'http://name:secret@127.0.0.1:19888'},{searxng_url:'http://127.0.0.1:19888/search'}]){
  setup(dir,extra);const xng=new XngConnection({standaloneRoot:path.join(temp,'none'),setupRoot:dir,fetcher:async()=>{calls++;throw Error('must not fetch');}});assert.equal((await xng.status()).installation,null);
 }assert.equal(calls,0);
 setup(dir);fs.writeFileSync(path.join(dir,'xng-setup.json'),JSON.stringify({schema:1,id:'other'}));assert.equal(new XngConnection({standaloneRoot:path.join(temp,'none'),setupRoot:dir}).candidates().length,0);
});
test('authenticated status and web evidence share custom-port discovery with zero model calls',async()=>{
 const dir=fixture('transport');setup(dir);const visited=[];
 const fetcher=async url=>{visited.push(url);return response(url.endsWith('/health')?health(dir):{paid:false,schema_version:1,evidence:[{title:'官方來源',url:'https://example.com',source_type:'official',passages:['核對後原文']} ]});};
 const xng=new XngConnection({standaloneRoot:path.join(temp,'none'),setupRoot:dir,fetcher});let google=0;
 const chat={keyValid:()=>true,assistantChat:async()=>{google++;throw Error('must not call Gemini');}};
 const assistant=new Assistant(chat,{fetcher,xng});const token='z'.repeat(64),server=createHub({p:{maxBodyBytes:128000}},chat,token,{xng,assistant});await new Promise(r=>server.listen(0,'127.0.0.1',r));const url='http://127.0.0.1:'+server.address().port;
 try{
  assert.equal((await fetch(url+'/xng/status')).status,403);assert.equal(visited.length,0);
  const headers={authorization:'Bearer '+token};const state=await (await fetch(url+'/xng/status',{headers})).json();assert.equal(state.endpoint,'http://127.0.0.1:19889');
  const result=await(await fetch(url+'/assistant',{method:'POST',headers:{...headers,'content-type':'application/json'},body:JSON.stringify({text:'搜尋官方資料',mode:'web'})})).json();
  assert.equal(result.answer_kind,'evidence');assert.equal(result.model,null);assert.equal(result.sources.length,1);assert.equal(google,0);assert.ok(visited.includes('http://127.0.0.1:19889/ai/search'));
 }finally{server.closeIdleConnections();await new Promise(r=>server.close(r));}
});
test('disconnected search degrades to evidence notice without spending a model request',async()=>{
 let google=0;const chat={keyValid:()=>true,assistantChat:async()=>{google++;}};
 const assistant=new Assistant(chat,{xng:{cached:{},endpoint:async()=>{throw Error('offline');}}});const result=await assistant.run({text:'現在版本',mode:'search'});assert.equal(result.notice,'xng_unavailable');assert.equal(result.model,null);assert.equal(google,0);
});
