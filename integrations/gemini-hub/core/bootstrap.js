import {existsSync,mkdirSync,readFileSync,writeFileSync,openSync,closeSync,unlinkSync} from 'node:fs';
import {resolve} from 'node:path';
import {randomBytes,createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {DatabaseSync} from 'node:sqlite';
import {UsageGuard,validatePolicy,freePolicyFingerprint} from './guard.js';

export function initialPolicy(){
 return {schema:1,enabled:false,project:'local-free-project',defaultModel:'gemini-3.5-flash-lite',paidAllowed:false,toolsAllowed:false,
  providerLimitsVerified:false,
  verification:{tier:'free',mode:'time-limited',keySuffix:'none',observedAt:'2026-01-01T00:00:00Z',validUntil:'2026-01-02T00:00:00Z',source:'Not confirmed; disabled initial setup'},
  dailyRequests:20,dailyTotalTokens:4000000,maxInputTokens:32768,maxOutputTokens:2048,maxBodyBytes:131072,preflightPerMinute:6,
  models:{'gemini-3.5-flash-lite':{official:null,local:{rpm:3,tpm:15000,rpd:20},outputReservation:65536}}};
}
export function initialize(root){
 root=resolve(root);mkdirSync(root,{recursive:true});const data=resolve(root,'data');mkdirSync(data,{recursive:true});
 const lock=resolve(data,'bootstrap.lock');const handle=openSync(lock,'wx');
 try{
  const policyPath=resolve(root,'policy.json'),db=resolve(data,'usage.sqlite'),token=resolve(data,'hub.token');
  // A missing policy alongside existing secrets/usage is damage, not a fresh install.
  const fresh=!existsSync(policyPath);
  if(fresh&&(existsSync(db)||existsSync(db+'.initialized')||existsSync(resolve(data,'gemini-key.dpapi'))||existsSync(token)))throw Error('existing_configuration_incomplete');
  if(!existsSync(policyPath))writeFileSync(policyPath,JSON.stringify(initialPolicy(),null,2)+'\n',{flag:'wx'});
  const policy=validatePolicy(JSON.parse(readFileSync(policyPath,'utf8')));
  if(existsSync(db+'.initialized')&&!existsSync(db))throw Error('usage_database_missing');
  if(!fresh&&!existsSync(db)&&!existsSync(db+'.initialized'))throw Error('usage_database_missing');
  if(!existsSync(token))writeFileSync(token,randomBytes(32).toString('hex'),{flag:'wx'});
  if(readFileSync(token,'utf8').trim().length<48)throw Error('hub_auth_missing');
  if(fresh){const guard=new UsageGuard(db,policy,{initialize:true});guard.close();}
  else{
   if(!existsSync(db+'.initialized'))throw Error('usage_database_marker_missing');
   const ledger=new DatabaseSync(db,{readOnly:true});
   try{if(ledger.prepare("SELECT value FROM meta WHERE key='schema'").get()?.value!=='1')throw Error('usage_database_schema');ledger.prepare('SELECT id FROM events LIMIT 1').get();}
   finally{ledger.close();}
  }
  return {ok:true,enabled:policy.enabled,provider_limits_verified:policy.providerLimitsVerified!==false};
 }finally{closeSync(handle);unlinkSync(lock);}
}
export function confirmedPolicy(existing,{keyHash,keySuffix,freeConfirmed},now=new Date()){
 if(freeConfirmed!==true||!/^[a-f0-9]{64}$/.test(keyHash??'')||!/^[A-Za-z0-9_-]{4}$/.test(keySuffix??''))throw Error('free_confirmation_required');
 validatePolicy(existing);const p=structuredClone(existing);
 const sameKey=p.verification.mode==='project-pinned'&&p.verification.binding.keySha256===keyHash;
 // A new key has unknown provider limits. Preserve the ledger/project identity,
 // lower local budgets, and never present a guessed 500-RPD allowance as fact.
 if(!sameKey){const conservative=initialPolicy();p.providerLimitsVerified=false;p.dailyRequests=conservative.dailyRequests;
  p.models=conservative.models;p.preflightPerMinute=conservative.preflightPerMinute;}
 p.enabled=true;p.verification={tier:'free',mode:'project-pinned',keySuffix,observedAt:now.toISOString(),validUntil:null,
  source:'User explicit Free tier confirmation; not live cloud billing verification',
  binding:{project:p.project,model:p.defaultModel,keySha256:keyHash,limitsSha256:''}};
 p.verification.binding.limitsSha256=freePolicyFingerprint(p);return validatePolicy(p);
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
 try{
  const action=process.argv[2],root=resolve(process.argv[3]??'.');
  if(action==='init')console.log(JSON.stringify(initialize(root)));
  else if(action==='confirm'){
   let input='';for await(const chunk of process.stdin){input+=chunk;if(input.length>2000)throw Error('input_too_large');}
   console.log(JSON.stringify(confirmedPolicy(JSON.parse(readFileSync(resolve(root,'policy.json'),'utf8')),JSON.parse(input)),null,2));
  }else throw Error('unknown_action');
 }catch{console.error('local_setup_failed');process.exitCode=2;}
}
