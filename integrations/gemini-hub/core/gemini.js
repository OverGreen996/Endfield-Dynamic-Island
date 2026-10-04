import { Locked } from './guard.js';
import { createHash,timingSafeEqual } from 'node:crypto';
import {imageInput,IMAGE_MAX_BYTES} from './image.js';
import {memoryResponseSchema} from './memory.js';
import {planningSchema} from './planning.js';
export function textRequest(input,p) {
  if(!input||typeof input!=='object'||Array.isArray(input)||
     Object.keys(input).some(k=>!['messages','model','max_output_tokens'].includes(k))) throw new Locked('text_chat_only',400);
  const model=input.model??p.defaultModel;
  if(!Object.hasOwn(p.models,model)) throw new Locked('model_not_allowed',400);
  if(!Array.isArray(input.messages)||input.messages.length===0||input.messages.length>64) throw new Locked('invalid_messages',400);
  const contents=input.messages.map(m=>{
    if(!m||Object.keys(m).some(k=>!['role','text'].includes(k))||!['user','model'].includes(m.role)||
       typeof m.text!=='string'||!m.text.trim()) throw new Locked('invalid_text_message',400);
    return {role:m.role,parts:[{text:m.text}]};
  });
  if(contents.some((m,i)=>m.role!==(i%2===0?'user':'model'))||contents.at(-1).role!=='user')
    throw new Locked('messages_must_alternate_and_end_user',400);
  const max=input.max_output_tokens??p.maxOutputTokens;
  if(!Number.isSafeInteger(max)||max<1||max>p.maxOutputTokens) throw new Locked('output_token_limit',400);
  const body={contents,generationConfig:{candidateCount:1,maxOutputTokens:max,
    responseModalities:['TEXT'],thinkingConfig:{thinkingLevel:'LOW'}}};
  if(Buffer.byteLength(JSON.stringify(body))>p.maxBodyBytes) throw new Locked('body_too_large',413);
  return {model,body};
}
export class GeminiChat {
  constructor(guard, {key='',fetcher=fetch}={}) { this.guard=guard; this.key=key; this.fetcher=fetcher; }
  keyValid() {
    const v=this.guard.p.verification;
    if(!/^(?:AIza[A-Za-z0-9_-]{30,}|AQ\.[A-Za-z0-9_-]{30,})$/.test(this.key) ||
       !this.key.toLowerCase().endsWith(v.keySuffix.toLowerCase()))return false;
    if(v.mode!=='project-pinned')return true;
    const actual=createHash('sha256').update(this.key).digest();
    const expected=Buffer.from(v.binding.keySha256,'hex');
    return actual.length===expected.length&&timingSafeEqual(actual,expected);
  }
  async post(model,method,body,signal) {
    try{
      const r=await this.fetcher('https://generativelanguage.googleapis.com/v1beta/models/'+model+':'+method,
        {method:'POST',redirect:'error',signal:signal?AbortSignal.any([signal,AbortSignal.timeout(45000)]):AbortSignal.timeout(45000),
         headers:{'content-type':'application/json','x-goog-api-key':this.key},body:JSON.stringify(body)});
      if(!r.ok) throw new Locked('provider_http_'+r.status,r.status===429?429:502);
      const data=await r.json(); return data;
    }catch(e){ if(e instanceof Locked) throw e; throw new Locked('provider_transport_error',502); }
  }
  async chat(input,signal) {
    const {model,body}=textRequest(input,this.guard.p);
    return this.send(model,body,signal);
  }
  // Trusted local orchestration only. The public /chat contract remains text-only.
  async assistantChat(messages,instructions,signal,{classifyMemory=false}={}) {
    const {model,body}=textRequest({messages},this.guard.p);
    body.systemInstruction={parts:[{text:instructions}]};
    if(classifyMemory)body.generationConfig.responseFormat={text:{mimeType:'APPLICATION_JSON',schema:memoryResponseSchema}};
    if(Buffer.byteLength(JSON.stringify(body))>this.guard.p.maxBodyBytes)throw new Locked('body_too_large',413);
    const answer=await this.send(model,body,signal);
    return classifyMemory?{...answer,memory_response_required:true}:answer;
  }
  async planChat(messages,instructions,signal,{requireSearch=false}={}) {
    const {model,body}=textRequest({messages},this.guard.p);
    body.systemInstruction={parts:[{text:instructions}]};
    const schema=requireSearch?{...planningSchema,properties:{...planningSchema.properties,action:{type:'string',enum:['search','clarify']}}}:planningSchema;
    body.generationConfig.responseFormat={text:{mimeType:'APPLICATION_JSON',schema}};
    if(Buffer.byteLength(JSON.stringify(body))>this.guard.p.maxBodyBytes)throw new Locked('body_too_large',413);
    return this.send(model,body,signal);
  }
  async visionChat(messages,instructions,image,signal){
    const accepted=imageInput(image);
    if(!accepted)throw new Locked('invalid_image',400);
    const {model,body}=textRequest({messages},this.guard.p);
    body.systemInstruction={parts:[{text:instructions}]};
    body.contents.at(-1).parts.push({inlineData:{mimeType:accepted.mime_type,data:accepted.data}});
    if(Buffer.byteLength(JSON.stringify(body))>this.guard.p.maxBodyBytes+Math.ceil(IMAGE_MAX_BYTES/3)*4)throw new Locked('body_too_large',413);
    return this.send(model,body,signal);
  }
  async send(model,body,signal) {
    signal?.throwIfAborted();
    if(!this.keyValid()) throw new Locked('api_key_not_configured_or_mismatch',503);
    this.guard.preflight(model);
    // No generate request is sent unless token preflight succeeds and the atomic reservation commits.
    let counted;
    try{counted=await this.post(model,'countTokens',body.systemInstruction?
      {generateContentRequest:{model:'models/'+model,...body}}:{contents:body.contents},signal);}
    catch(e){this.guard.preflightFailure(model,Number(e.code?.match(/^provider_http_(\d+)$/)?.[1]??0));throw e;}
    signal?.throwIfAborted();
    const id=this.guard.reserve(model,counted.totalTokens);
    let response;
    try { response=await this.post(model,'generateContent',body,signal); }
    catch(e){ const status=Number(e.code?.match(/^provider_http_(\d+)$/)?.[1]??0); this.guard.fail(id,status); throw e; }
    const reconciled=this.guard.finish(id,response.usageMetadata);
    if(!reconciled) throw new Locked('unknown_usage_lock',502);
    const candidate=response.candidates?.[0];
    const text=candidate?.content?.parts?.filter(p=>typeof p.text==='string'&&!p.thought).map(p=>p.text).join('')??'';
    return {id,model,text,finish_reason:candidate?.finishReason??(response.promptFeedback?.blockReason?'BLOCKED':'EMPTY'),
      usage:response.usageMetadata,search_used:false};
  }
}
