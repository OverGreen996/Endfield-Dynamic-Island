import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { UsageGuard,validatePolicy,freePolicyFingerprint } from '../guard.js';
import { GeminiChat } from '../gemini.js';
import { loadPolicy } from '../config.js';
const key='AIza'+'p'.repeat(32)+'test',model='gemini-3.5-flash-lite';
const base=Date.parse('2026-10-04T10:00:00Z');
function policy(){
 const p=loadPolicy();
 p.verification={tier:'free',mode:'project-pinned',keySuffix:'test',observedAt:new Date(base).toISOString(),validUntil:null,
 binding:{project:p.project,model:p.defaultModel,keySha256:createHash('sha256').update(key).digest('hex'),limitsSha256:freePolicyFingerprint(p)}};
 return p;
}
test('confirmed project works after 24 hours and months without changing request budgets',()=>{
 for(const days of [0,1,2,30,365]){
  const g=new UsageGuard(':memory:',policy(),{now:()=>base+days*86400000});
  const c=new GeminiChat(g,{key});assert.equal(c.keyValid(),true);
  const id=g.reserve(model,10);assert.ok(id);assert.equal(g.status(true).models[model].used_requests_today,1);
  assert.equal(g.status().free_tier_verified_until,null);assert.equal(g.status().free_tier_verification_mode,'project-pinned');
  assert.equal(g.status().billing_status_live_verified,false);g.close();
 }
});
test('another key with identical suffix cannot call Google or replace its usage',async()=>{
 const g=new UsageGuard(':memory:',policy(),{now:()=>base});let calls=0;
 const c=new GeminiChat(g,{key:key.replace('p','q'),fetcher:async()=>{calls++;}});
 await assert.rejects(c.chat({messages:[{role:'user',text:'test'}]}),e=>e.code==='api_key_not_configured_or_mismatch');
 assert.equal(calls,0);assert.equal(g.status().models[model].used_requests_today,0);g.close();
});
test('project, model, limits, paid/tool flags and missing credential binding require re-confirmation',()=>{
 for(const mutate of [p=>p.project+='-changed',p=>p.defaultModel='gemini-3.8-flash',p=>p.dailyRequests--,
  p=>p.models[model].local.rpd++,p=>p.paidAllowed=true,p=>p.toolsAllowed=true,
  p=>delete p.verification.binding.keySha256,p=>p.verification.mode='unlimited',p=>p.verification.validUntil='2099-01-01T00:00:00Z']){
  const p=policy();mutate(p);assert.throws(()=>validatePolicy(p));
 }
});
test('persistent free mode still rejects future confirmation, disabled mode and depleted daily budgets',()=>{
 let now=base-1;const p=policy();p.dailyRequests=1;p.verification.binding.limitsSha256=freePolicyFingerprint(p);
 const g=new UsageGuard(':memory:',p,{now:()=>now});
 assert.throws(()=>g.reserve(model,10),e=>e.code==='free_tier_verification_expired');
 now=base;g.reserve(model,10);assert.throws(()=>g.reserve(model,10),e=>e.code==='daily_request_limit');g.close();
 const q=policy();q.enabled=false;const h=new UsageGuard(':memory:',q,{now:()=>base});
 assert.throws(()=>h.reserve(model,10),e=>e.code==='disabled');h.close();
});
test('the same persistent confirmation continues across daily reset but retains the ledger',()=>{
 let now=base;const p=policy();p.dailyRequests=1;p.verification.binding.limitsSha256=freePolicyFingerprint(p);
 const g=new UsageGuard(':memory:',p,{now:()=>now});g.reserve(model,10);
 now+=86400000;assert.equal(g.status(true).models[model].used_requests_today,0);g.reserve(model,10);
 assert.equal(g.db.prepare('SELECT count(*) n FROM events').get().n,2);g.close();
});
