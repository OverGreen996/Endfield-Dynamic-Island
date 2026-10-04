import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,readFileSync,writeFileSync,rmSync,unlinkSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {initialize,initialPolicy,confirmedPolicy} from '../bootstrap.js';
import {UsageGuard,validatePolicy,freePolicyFingerprint} from '../guard.js';
const binding={keyHash:'a'.repeat(64),keySuffix:'mock',freeConfirmed:true};
test('confirmation accepts BOM-prefixed Windows PowerShell JSON without changing key binding',()=>{
 const root=mkdtempSync(join(tmpdir(),'gemini-bootstrap-'));
 try{
  initialize(root);
  const result=spawnSync(process.execPath,[fileURLToPath(new URL('../bootstrap.js',import.meta.url)),'confirm',root],{input:'\ufeff'+JSON.stringify(binding)+'\r\n',encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);
  assert.equal(JSON.parse(result.stdout).verification.binding.keySha256,binding.keyHash);
 }finally{rmSync(root,{recursive:true,force:true});}
});
test('fresh install is disabled, authenticated, quota guarded and makes no provider call',()=>{
 const root=mkdtempSync(join(tmpdir(),'gemini-bootstrap-'));
 try{
  assert.equal(initialize(root).enabled,false);
  const p=JSON.parse(readFileSync(join(root,'policy.json'),'utf8'));
  assert.equal(p.providerLimitsVerified,false);assert.equal(p.models[p.defaultModel].official,null);
  assert.equal(p.dailyRequests,20);assert.equal(readFileSync(join(root,'data/hub.token'),'utf8').length,64);
  const g=new UsageGuard(join(root,'data/usage.sqlite'),p);
  try{assert.throws(()=>g.preflight(p.defaultModel),e=>e.code==='disabled');}finally{g.close();}
  const token=readFileSync(join(root,'data/hub.token'),'utf8'),before=readFileSync(join(root,'policy.json'),'utf8');
  initialize(root);assert.equal(readFileSync(join(root,'policy.json'),'utf8'),before);assert.equal(readFileSync(join(root,'data/hub.token'),'utf8'),token);
 }finally{rmSync(root,{recursive:true,force:true});}
});
test('free confirmation is explicit and changed keys retain project usage while lowering budgets',()=>{
 for(const confirmation of [undefined,false,'true'])assert.throws(()=>confirmedPolicy(initialPolicy(),{...binding,freeConfirmed:confirmation}));
 const p=confirmedPolicy(initialPolicy(),binding);
 assert.equal(p.enabled,true);assert.equal(p.verification.mode,'project-pinned');validatePolicy(p);
 assert.deepEqual(confirmedPolicy(p,binding).models,p.models);
 const high=structuredClone(p);high.providerLimitsVerified=true;high.dailyRequests=450;
 high.models[high.defaultModel]={official:{rpm:15,tpm:250000,rpd:500},local:{rpm:12,tpm:200000,rpd:450},outputReservation:65536};
 high.verification.binding.limitsSha256=freePolicyFingerprint(high);
 assert.equal(confirmedPolicy(high,binding).dailyRequests,450);
 // Rebind the synthetic known-key policy to its actual budgets before exercising it.
 const provisional={...high,verification:initialPolicy().verification};
 const known=confirmedPolicy(provisional,binding);
 // First confirmation uses conservative limits because provider limits are unknown.
 assert.equal(known.dailyRequests,20);
 const replacement=confirmedPolicy(p,{...binding,keyHash:'b'.repeat(64)});
 assert.equal(replacement.project,p.project);assert.equal(replacement.dailyRequests,20);
 const g=new UsageGuard(':memory:',p);
 try{const r=g.reserve(p.defaultModel,100);g.finish(r,{promptTokenCount:100,candidatesTokenCount:10,totalTokenCount:110});g.p=replacement;
  assert.equal(g.status().models[p.defaultModel].used_requests_today,1);
 }finally{g.close();}
});
test('conservative local limits cannot be inflated or confused with verified cloud limits',()=>{
 for(const mutation of [p=>p.dailyRequests=21,p=>p.models[p.defaultModel].local.rpm=4,p=>p.models[p.defaultModel].local.tpm=15001,p=>p.models[p.defaultModel].local.rpd=21,p=>p.models[p.defaultModel].official={rpm:15,tpm:250000,rpd:500}]){
  const p=initialPolicy();mutation(p);assert.throws(()=>validatePolicy(p));
 }
});
test('missing usage or partial existing data is preserved and never silently reset',()=>{
 const root=mkdtempSync(join(tmpdir(),'gemini-bootstrap-'));
 try{
  initialize(root);unlinkSync(join(root,'data/usage.sqlite'));
  assert.throws(()=>initialize(root));
  unlinkSync(join(root,'data/usage.sqlite.initialized'));assert.throws(()=>initialize(root));
  unlinkSync(join(root,'policy.json'));assert.throws(()=>initialize(root));
  assert.equal(readFileSync(join(root,'data/hub.token'),'utf8').length,64);
 }finally{rmSync(root,{recursive:true,force:true});}
});
