import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {pathToFileURL} from 'node:url';
import {root as hubRoot} from './config.js';

const version=v=>typeof v==='string'&&/^\d[A-Za-z0-9._-]{0,63}$/.test(v)?v:'內建';
const samePath=(a,b)=>typeof a==='string'&&path.resolve(a).toLowerCase()===path.resolve(b).toLowerCase();
function readJson(file){if(fs.statSync(file).size>8192)throw Error('xng_config_size');return JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));}
export class XngConnection {
 constructor({standaloneRoot=path.resolve(hubRoot,'../XNG'),setupRoot=path.join(process.env.LOCALAPPDATA||path.join(os.homedir(),'AppData/Local'),'XNG'),fetcher=fetch}={}){
  this.roots=[{root:standaloneRoot,installation:'standalone'},{root:setupRoot,installation:'setup'}];this.fetcher=fetcher;this.cached=null;
 }
 candidates(){
  const out=[];
  for(const entry of this.roots){try{
   if(!fs.existsSync(path.join(entry.root,'Start-XNG.ps1'))||!fs.existsSync(path.join(entry.root,'Manage-XNGPlugin.ps1')))continue;
   let port=8889,backend=null;
   if(entry.installation==='setup'){
    const marker=readJson(path.join(entry.root,'xng-setup.json')),config=readJson(path.join(entry.root,'.runtime/connection.json'));
    if(marker.id!=='xng-one-click'||marker.schema!==1||!Number.isInteger(config.port)||config.port<1024||config.port>65535||config.port===8890)continue;
    const u=new URL(config.searxng_url);
    if(u.protocol!=='http:'||!['127.0.0.1','localhost','[::1]'].includes(u.hostname)||u.username||u.password||u.search||u.hash||u.pathname!=='/')continue;
    port=config.port;backend=config.searxng_url;
   }
   out.push({...entry,endpoint:'http://127.0.0.1:'+port,backend});
  }catch{/* Invalid or missing local metadata never becomes a remote destination. */}}
  return out;
 }
 async status({refresh=false,signal}={}){
  if(!refresh&&this.cached&&Date.now()-this.cached.at<15000)return this.cached.value;
  const candidates=this.candidates();
  for(const entry of candidates){try{
   const response=await this.fetcher(entry.endpoint+'/health',{redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(1600)]):AbortSignal.timeout(1600)});
   if(!response.ok)continue;const text=await response.text();if(Buffer.byteLength(text)>32768)continue;const health=JSON.parse(text);
   if(health.service!=='XNG AI Search Hub'||health.schema_version!==1||health.paid!==false||!samePath(health.state_directory,path.join(entry.root,'.runtime'))||(entry.backend&&health.endpoint!==entry.backend))continue;
   const value={connected:true,endpoint:entry.endpoint,installation:entry.installation,installation_root:entry.root,core_version:version(health.plugin?.version),rules_version:version(health.rules?.version),paid:false,schema_version:1};
   this.cached={at:Date.now(),value};return value;
  }catch{if(signal?.aborted)throw signal.reason;}}
  this.cached=null;
  const candidate=candidates[0];return {connected:false,endpoint:candidate?.endpoint??null,installation:candidate?.installation??null,installation_root:candidate?.root??null,error:'xng_unavailable',paid:false,schema_version:1};
 }
 async endpoint(signal){const state=await this.status({signal});if(!state.connected)throw Error('xng_unavailable');return state.endpoint+'/ai/search';}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href&&process.argv[2]==='--status'){
 console.log(JSON.stringify(await new XngConnection().status({refresh:true})));
}
