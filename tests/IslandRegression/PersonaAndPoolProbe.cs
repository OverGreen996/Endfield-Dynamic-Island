using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class PersonaAndPoolProbe
{
    internal static string Key(int i)=>"AIzaSyntheticPersonaPoolNeverNetwork00000"+i;
    internal const string Chat="""{"action":"answer","answer":"自然回答","search_query":"","memory":[],"operations":[]}""";
    internal static HttpResponseMessage Json(string text,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(text,Encoding.UTF8,"application/json")};
    internal static HttpResponseMessage Gemini(string text)=>Json(new JsonObject{["candidates"]=new JsonArray(new JsonObject{["content"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=text})}}),["usageMetadata"]=new JsonObject{["promptTokenCount"]=40,["candidatesTokenCount"]=20,["totalTokenCount"]=60}}.ToJsonString());
    internal static async Task RunAsync()
    {
        var count=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
        void Reject(Action action,string name){try{action();throw new Exception("FAIL: "+name);}catch(Exception e)when(!e.Message.StartsWith("FAIL:")){Check(true,name);}}
        var root=AiBackupProbe.Root();Directory.CreateDirectory(root);var file=Path.Combine(root,"personas.dpapi");
        var store=new AssistantPersonaStore(file);Check(store.Active.Id=="default"&&!File.Exists(file),"default read makes no profile file or API call");
        var a=store.Save(null,"Alpha","PersonaAlpha","RuleAlpha");var b=store.Save(null,"Beta","PersonaBeta","RuleBeta");
        Check(store.Active.Id=="default"&&store.Profiles.Count==3,"save and activation are separate");store.Activate(a.Id);
        var reload=new AssistantPersonaStore(file);Check(reload.Active==a&&reload.Profiles.Contains(b),"encrypted library and selected persona survive restart");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("PersonaAlpha"),"private persona text is encrypted on disk");
        var snapshot=store.Active;store.Save(a.Id,"Alpha renamed","PersonaAlphaUpdated","RuleAlpha");Check(snapshot.Character=="PersonaAlpha"&&store.Active.Character=="PersonaAlphaUpdated","immutable turn snapshot survives editing active profile");
        var before=File.ReadAllBytes(file);Reject(()=>store.Save(null," ","x","y"),"empty persona name rejected");Reject(()=>store.Save(null,"long",new string('a',6001),""),"overlong persona rejected");
        Reject(()=>store.Save(null,"combined",new string('a',6000),new string('b',5000)),"combined prompt bound enforced");Check(before.SequenceEqual(File.ReadAllBytes(file)),"invalid drafts never overwrite saved data");
        Reject(()=>store.Delete("default"),"default persona cannot be deleted");Reject(()=>store.Activate("missing"),"missing persona cannot be activated");
        store.Delete(a.Id);Check(store.Active.Id=="default"&&new AssistantPersonaStore(file).Active.Id=="default","deleting active profile safely restores default");
        var stale=new AssistantPersonaStore(file);store.Save(null,"new","changed","");Reject(()=>stale.Save(null,"stale","x",""),"stale owner cannot overwrite another owner's updates");
        var corrupt=Path.Combine(root,"corrupt.dpapi");File.WriteAllBytes(corrupt,[1,2,3]);var damaged=new AssistantPersonaStore(corrupt);Reject(()=>damaged.Save(null,"test","x",""),"corrupt library blocks writes");Check(damaged.StorageError&&damaged.Active.Id=="default"&&File.ReadAllBytes(corrupt).SequenceEqual(new byte[]{1,2,3}),"corrupt library preserved with default fallback");
        Check(AssistantPersonaStore.Apply("base",store.Active)=="base","default does not change existing prompts");
        var wideFile=Path.Combine(root,"unicode.dpapi");var unicodeStore=new AssistantPersonaStore(wideFile);var unicode=unicodeStore.Save(null,"中文人格",new string('語',5000),new string('氣',5000));
        Check(new AssistantPersonaStore(wideFile).Profiles.Contains(unicode),"maximum-length Chinese profile reloads without escaping overflow");
        var cap=new AssistantPersonaStore(Path.Combine(root,"cap.dpapi"));for(var i=0;i<30;i++)cap.Save(null,"P"+i,"","");Reject(()=>cap.Save(null,"extra","",""),"library is bounded to thirty custom personas");

        var apiRoot=AiBackupProbe.Root();var config=new NativeConfiguration(apiRoot);config.Initialize();config.Configure(Key(1),true);
        var calls=new List<int>();var prompts=new List<string>();var contextBodies=new List<JsonNode>();var quota=new HashSet<int>();var auth=new HashSet<int>();var allTransient=false;var searchMode=false;var planned=false;var interruptPersona=false;
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+60000;NativeAssistantService? serviceRef=null;AssistantPersona? beta=null;
        using var http=new HttpClient(new AiBackupProbe.Handler(async(request,token)=>{
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            if(request.RequestUri!.Host=="api.exa.ai"){
                if(interruptPersona){serviceRef!.Personas.Activate(beta!.Id);interruptPersona=false;}
                return Json("""{"results":[{"title":"test","url":"https://example.com/test","text":"This is sufficiently detailed synthetic search evidence to test the persona summary path."}],"costDollars":{"total":0.005}}""");
            }
            if(request.RequestUri.Host!="generativelanguage.googleapis.com"){
                var id=request.RequestUri.Host=="api.groq.com"?"groq":"cloudflare";var backupReply=J.S(body["messages"]![0],"content").Contains("你現在只負責")?"{\"answer\":\"自然回答\"}":Chat;calls.Add(id=="groq"?6:7);prompts.Add(J.S(body["messages"]![0],"content"));
                if(id=="groq"&&allTransient)return Json("{}",HttpStatusCode.ServiceUnavailable);
                return id=="groq"?Json(new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=backupReply}}),["usage"]=new JsonObject{["total_tokens"]=20}}.ToJsonString()):Json(new JsonObject{["success"]=true,["result"]=new JsonObject{["response"]=backupReply,["usage"]=new JsonObject{["prompt_tokens"]=10,["completion_tokens"]=10}}}.ToJsonString());
            }
            var key=request.Headers.GetValues("x-goog-api-key").Single();var slot=Enumerable.Range(1,5).Single(i=>Key(i)==key);
            if(quota.Contains(slot))return Json("""{"error":{"message":"quota exhausted","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]}]}}""",HttpStatusCode.TooManyRequests);
            if(auth.Contains(slot))return Json("{}",HttpStatusCode.Unauthorized);
            if(allTransient)return Json("{}",HttpStatusCode.ServiceUnavailable);
            if(request.RequestUri.AbsolutePath.EndsWith(":countTokens"))return Json("{\"totalTokens\":40}");
            calls.Add(slot);prompts.Add(J.S(body["systemInstruction"]!["parts"]![0],"text"));contextBodies.Add(body.DeepClone());
            if(body["contents"]![body["contents"]!.AsArray().Count-1]!["parts"]!.AsArray().Any(p=>p?["inlineData"] is not null))return Gemini("看到了圖片");
            if(searchMode){if(!planned){planned=true;return Gemini("""{"action":"search","answer":"","search_query":"synthetic evidence","memory":[],"operations":[]}""");}return Gemini("""{"answer":"整理完成"}""");}
            return Gemini(body["generationConfig"]?["responseFormat"]?["text"]?["schema"]?["properties"]?.AsObject().Count==1?"{\"answer\":\"自然回答\"}":Chat);
        }));
        using(var service=new NativeAssistantService(apiRoot,http,()=>now,(_,_)=>Task.FromResult<SearchRow?>(null))){
            serviceRef=service;var alpha=service.Personas.Save(null,"A","PersonaAlpha","RuleAlpha");beta=service.Personas.Save(null,"B","PersonaBeta","RuleBeta");service.Personas.Activate(alpha.Id);
            await service.ConfigureGeminiPoolAsync(Enumerable.Range(2,4).Select(Key).ToArray(),new HashSet<int>(),true,default);
            Check(calls.Count==0&&service.GeminiPool.Status().All(s=>s.Configured),"five-account settings saved with no model calls");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(apiRoot,"data","gemini-pool-keys.dpapi"))).Contains(Key(2)),"pool credentials encrypted");
            var history=new[]{new ChatMessage("user","context marker"),new ChatMessage("model","previous answer")};
            for(var i=0;i<6;i++)await service.AskAsync("接著聊",history,"chat",default);
            Check(calls.SequenceEqual(Enumerable.Repeat(1,12)),"primary account stays in use until unavailable");
            Check(contextBodies.All(p=>p["contents"]!.AsArray().Count==3&&J.S(p["contents"]![0]!["parts"]![0],"text")=="context marker"),"same selected conversation context reaches every account");
            Check(prompts.All(p=>p.Contains("PersonaAlpha")&&p.Contains("RuleAlpha")&&!p.Contains("PersonaBeta")),"only the active character and rules reach the primary account");
            Check(J.N(service.Usage(),"models","gemini-3.5-flash-lite","used_requests_today")==12,"combined usage counts each actual generation once");
            var keyBefore=File.ReadAllBytes(Path.Combine(apiRoot,"data","gemini-pool-keys.dpapi"));
            try{await service.ConfigureGeminiPoolAsync(new[]{Key(2),Key(2),"",""},new HashSet<int>(),true,default);throw new Exception("FAIL: duplicate accepted");}catch(AssistantFailure e){Check(e.Code=="duplicate_gemini_key","duplicate keys rejected before saving");}
            try{await service.ConfigureAsync(Key(2),true,default);throw new Exception("FAIL: primary duplicate accepted");}catch(AssistantFailure e){Check(e.Code=="duplicate_gemini_key","primary cannot duplicate an extra account");}
            Check(File.ReadAllBytes(Path.Combine(apiRoot,"data","gemini-pool-keys.dpapi")).SequenceEqual(keyBefore),"invalid configuration preserves existing credentials");
            await service.Search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["exa"]="exa-synthetic-test-key"},new HashSet<string>(),default);
            calls.Clear();prompts.Clear();searchMode=true;interruptPersona=true;
            await service.AskAsync("搜尋資料",history,"auto",default);
            Check(calls.Count==2&&calls[0]==calls[1],"planner and search synthesis keep the same account within a turn");
            Check(prompts.All(p=>p.Contains("PersonaAlpha")&&!p.Contains("PersonaBeta")),"switch during search cannot change the in-flight persona snapshot");
            searchMode=false;calls.Clear();prompts.Clear();await service.AskAsync("下一句",history,"chat",default);Check(prompts.All(p=>p.Contains("PersonaBeta")),"new active persona applies on the next turn");
            var usageBefore=service.GeminiPool.Status().Sum(s=>s.Requests);var rotationBefore=calls[0];calls.Clear();await service.AskAsync("只搜尋不生成",[],"web",default);Check(calls.Count==0&&service.GeminiPool.Status().Sum(s=>s.Requests)==usageBefore,"search-only neither calls Gemini nor consumes its rotation");
            calls.Clear();prompts.Clear();quota.Add(1);await service.AskAsync("繼續第二組",history,"chat",default);
            Check(calls.SequenceEqual(new[]{2,2}),"account 2 starts only after account 1 becomes unavailable");
            calls.Clear();quota.Add(2);await service.AskAsync("繼續第三組",history,"chat",default);
            Check(calls.SequenceEqual(new[]{3,3})&&service.GeminiPool.Status().Where(s=>s.Slot<=2).All(s=>s.Reason=="provider_daily_quota"),"quota failures skip accounts and persist daily cooldowns");
            await service.ConfigureGeminiPoolAsync(["","","",""],new HashSet<int>(),true,default);Check(service.GeminiPool.Status().Where(s=>s.Slot<=2).All(s=>s.Reason=="provider_daily_quota"),"blank settings save keeps cooldowns and usage");
            await service.ConfigureBackupsAsync(AiBackupProbe.Account,AiBackupProbe.CfKey,AiBackupProbe.GroqKey,true,true,false,false,default);
            calls.Clear();quota.Add(3);await service.AskAsync("繼續第四組",history,"chat",default);Check(calls.SequenceEqual(new[]{4,4}),"fourth Gemini precedes any backup");
            calls.Clear();quota.Add(4);await service.AskAsync("繼續第五組",history,"chat",default);Check(calls.SequenceEqual(new[]{5,5}),"fifth Gemini precedes any backup");
            Check(contextBodies.All(p=>p["contents"]!.AsArray().Count==3&&J.S(p["contents"]![0]!["parts"]![0],"text")=="context marker"),"conversation history is continuous across all five accounts");
            quota.UnionWith([3,4,5]);calls.Clear();prompts.Clear();await service.AskAsync("接手",history,"chat",default);
            Check(calls.SequenceEqual(new[]{6,6})&&prompts.All(p=>p.Contains("PersonaBeta")),"Groq takes over only after all Gemini accounts are unavailable, with the active persona");
            allTransient=true;calls.Clear();prompts.Clear();var cfReply=await service.AskAsync("第二後援",history,"chat",default);Check(calls.SequenceEqual(new[]{6,7})&&prompts.All(p=>p.Contains("PersonaBeta"))&&cfReply.model==NativeBackupModels.CloudflareModel&&cfReply.text=="自然回答","Cloudflare follows Groq with one combined reply and the same active persona");
            quota.Clear();allTransient=false;now=GeminiLedger.NextReset(now)+1000;calls.Clear();prompts.Clear();await service.AskAsync("額度恢復",[],"chat",default);Check(calls.Count==2&&calls[0]==calls[1]&&calls[0]<=5,"daily reset restores Gemini before backup providers");
            calls.Clear();var image=new ImageInput("image/png","iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j0n0AAAAASUVORK5CYII=");
            var requestsBeforeImage=service.GeminiPool.Status().Sum(s=>s.Requests);
            try{await service.AskAsync("",history,"chat",default,image);throw new Exception("FAIL: blank image accepted as text");}
            catch(AssistantFailure e){Check(e.Code=="image_ocr_no_text"&&calls.Count==0&&service.GeminiPool.Status().Sum(s=>s.Requests)==requestsBeforeImage,"local OCR of a blank image never consumes the Gemini pool");}
        }
        using(var reopened=new NativeAssistantService(apiRoot,http,()=>now)){
            Check(reopened.GeminiPool.Status().All(s=>s.Configured)&&reopened.GeminiPool.Status().Sum(s=>s.Requests)==2,"account keys and daily usage survive process restart");
            calls.Clear();await reopened.AskAsync("重新啟動",[],"chat",default);Check(calls.SequenceEqual(new[]{1,1}),"priority order resumes after restart");
            auth.Add(1);calls.Clear();await reopened.AskAsync("金鑰失效",[],"chat",default);Check(calls.SequenceEqual(new[]{2,2})&&reopened.GeminiPool.Status().First().Reason=="provider_auth","invalid primary credentials pause without blocking other accounts");
            calls.Clear();await reopened.AskAsync("保持第二組",[],"chat",default);Check(calls.SequenceEqual(new[]{2,2}),"paused primary is not repeatedly requested");
            var requests=reopened.GeminiPool.Status().Sum(s=>s.Requests);await reopened.ConfigureAsync(Key(1),true,default);
            Check(reopened.GeminiPool.Status().First().Reason=="ready"&&reopened.GeminiPool.Status().Sum(s=>s.Requests)==requests,"reconfirming primary clears auth pause without resetting any usage");
            var corruptKeys=Path.Combine(apiRoot,"data","gemini-pool-keys.dpapi");File.WriteAllBytes(corruptKeys,[4,5,6]);
        }
        using(var damagedPool=new NativeAssistantService(apiRoot,http,()=>now))Check(damagedPool.GeminiPool.StorageError&&damagedPool.GeminiPool.Status().First().Configured,"damaged extra-key storage preserves original primary access");
        Console.WriteLine($"{count}/{count} native persona and Gemini pool checks PASS; synthetic requests only, no live APIs.");
    }
}
