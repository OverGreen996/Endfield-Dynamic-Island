import { createServer } from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { UsageGuard,Locked } from './guard.js';
import { GeminiChat } from './gemini.js';
import { Assistant } from './assistant.js';
import { XngConnection } from './xng.js';
import { root,loadPolicy,dbPath } from './config.js';

export function createHub(guard,chat,token,{xng=new XngConnection(),assistant=new Assistant(chat,{xng})}={}) {
  if(typeof token!=='string'||token.length<48) throw new Error('hub_auth_missing');
  const server=createServer(async(req,res)=>{
    const cancellation=new AbortController();
    res.once('close',()=>{if(!res.writableEnded)cancellation.abort();});
    const send=(status,data)=>{res.writeHead(status,{'content-type':'application/json; charset=utf-8',
      'cache-control':'no-store','x-content-type-options':'nosniff'});res.end(JSON.stringify(data));};
    try{
      const got=Buffer.from(String(req.headers.authorization??''));
      const expected=Buffer.from('Bearer '+token);
      if(req.headers.origin || got.length!==expected.length || !timingSafeEqual(got,expected)) return send(403,{error:'local_auth_required'});
      const port=server.address()?.port;
      if(!['127.0.0.1:'+port,'localhost:'+port].includes(req.headers.host)) return send(403,{error:'invalid_host'});
      if(req.method==='GET'&&req.url==='/usage') return send(200,guard.status(chat.keyValid()));
      if(req.method==='GET'&&req.url==='/health') return send(200,{service:'Gemini Usage Hub',version:'0.3.0',capabilities:{image_input:true,memory_classification:true,structured_memory_response:true,gemini_first_planning:true,trusted_taipei_clock:true},search_tools:false,paid_allowed:false,key_present:chat.keyValid()});
      if(req.method==='GET'&&req.url==='/xng/status') return send(200,await xng.status({refresh:true,signal:cancellation.signal}));
      if(req.method!=='POST'||!['/chat','/assistant'].includes(req.url)) return send(404,{error:'route_not_found'});
      if(!req.headers['content-type']?.startsWith('application/json')) return send(415,{error:'json_required'});
      const chunks=[];let size=0;
      for await(const chunk of req){size+=chunk.length;if(size>(req.url==='/assistant'?4194304:guard.p.maxBodyBytes)) throw new Locked('body_too_large',413);chunks.push(chunk);}
      let input;try{input=JSON.parse(Buffer.concat(chunks).toString('utf8'));}catch{throw new Locked('invalid_json',400);}
      const result=req.url==='/assistant'?await assistant.run(input,cancellation.signal):await chat.chat(input,cancellation.signal);
      if(!res.destroyed)return send(200,result);
    }catch(e){if(!res.headersSent)send(e instanceof Locked?e.status:503,{error:e instanceof Locked?e.code:'guard_or_database_error'});}
  });
  return server;
}
export function startHub() {
  const guard=new UsageGuard(dbPath,loadPolicy());
  const chat=new GeminiChat(guard,{key:process.env.GEMINI_API_KEY??''});
  delete process.env.GEMINI_API_KEY;
  const token=readFileSync(resolve(root,'data','hub.token'),'utf8').trim();
  const server=createHub(guard,chat,token);
  server.requestTimeout=100000;server.headersTimeout=10000;server.maxConnections=32;
  server.on('error',()=>{console.error('local_server_error');guard.close();process.exitCode=1;});
  server.listen(8890,'127.0.0.1',()=>console.log('Gemini Usage Hub: 127.0.0.1:8890; local authentication required; no automatic API calls.'));
  for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>server.close(()=>{guard.close();process.exit(0);}));
  return server;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)startHub();
