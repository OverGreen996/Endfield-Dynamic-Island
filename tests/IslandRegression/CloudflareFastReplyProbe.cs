using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class CloudflareFastReplyProbe
{
    internal static async Task RunAsync()
    {
        var count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        string Plan(string action,string answer,string query="",JsonArray? memory=null,JsonArray? operations=null)=>new JsonObject{
            ["action"]=action,["answer"]=answer,["search_query"]=query,["memory"]=memory??new(),["operations"]=operations??new()}.ToJsonString(J.Json);
        JsonArray Pet()=>new(new JsonObject{["category"]="寵物飼養",["quote"]="我養黑王蛇",["text"]="我養黑王蛇",["subject"]="user",["stability"]="durable",["replace_id"]=""});
        HttpResponseMessage Result(string text,bool groq=false)=>new(HttpStatusCode.OK){Content=new StringContent(groq?
            new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=text}}),["usage"]=new JsonObject{["total_tokens"]=40}}.ToJsonString(J.Json):
            new JsonObject{["success"]=true,["result"]=new JsonObject{["response"]=text,["usage"]=new JsonObject{["total_tokens"]=40}}}.ToJsonString(J.Json),Encoding.UTF8,"application/json")};
        var variant="chat";var generations=0;var searches=0;
        using var http=new HttpClient(new AiBackupProbe.Handler(async(request,token)=>{
            if(request.RequestUri!.Host=="api.exa.ai"){
                searches++;return new(HttpStatusCode.OK){Content=new StringContent("""{"results":[{"title":"飼養資料","url":"https://example.com/care","text":"黑王蛇飼養測試資料，包含環境、清水及躲藏處，僅供合成 HTTP 驗證。"}]}""",Encoding.UTF8,"application/json")};
            }
            Check(request.RequestUri.Host=="api.cloudflare.com","CF-only flow sends no Gemini or Groq requests");generations++;
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;var schema=body["response_format"]!["json_schema"]!["schema"]!;
            var planning=schema["properties"]?["action"] is not null;var system=J.S(body["messages"]![0],"content");
            Check(system.Contains("PERSONA_CF_TEST"),"active persona reaches both combined and separate CF responses");
            Check(planning==system.Contains(DialogueInstructions.CombinedReply),"combined guidance applies only to understanding schema");
            if(variant=="chat"){
                Check(planning&&body["messages"]!.AsArray().Any(m=>J.S(m,"content").Contains("前文測試內容")),"combined reply preserves prior context and planning contract");
                return Result(Plan("answer","直接聊就好。"));
            }
            if(variant=="memory"){
                var due=DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8)).Date.AddDays(1).ToString("yyyy-MM-dd")+"T09:00:00+08:00";
                return Result(Plan("answer","已為您設定提醒。之後直接講重點。",memory:Pet(),operations:new(new JsonObject{
                    ["kind"]="remind",["quote"]="明早九點叫我拿包裹",["text"]="拿包裹",["due"]=due,["id"]=""})));
            }
            if(variant=="search"){
                if(planning)return Result(Plan("search","","黑王蛇 飼養資料",Pet()));
                Check(schema["properties"]!.AsObject().Count==1,"search writer remains answer-only and cannot replace memory or operations");
                return Result("""{"answer":"提供清水與躲藏處。"}""");
            }
            if(variant=="rewrite")return Result(planning?Plan("answer","黑王蛇這點我知道了。想聊什麼？",memory:Pet()):"""{"answer":"黑王蛇這點我知道了。"}""");
            if(variant=="invitation")return Result(planning?Plan("answer","反覆設定很麻煩。想吐槽就說吧，我聽著。",memory:Pet()):"""{"answer":"反覆設定確實很麻煩，熟悉的操作卻得重來。"}""");
            if(variant=="quoted")return Result(Plan("answer","這句話是在表示願意聽你說：『我聽著』。"));
            if(variant=="clarify")return Result(Plan("clarify","你指的是哪個產品？"));
            if(variant=="empty")return Result(Plan("answer",""));
            if(variant=="cancel"){
                await Task.Delay(10000,token);throw new Exception("FAIL: cancellation ignored");
            }
            throw new Exception("Unknown scenario");
        }));
        using var service=new NativeAssistantService(AiBackupProbe.Root(),http,reader:(_,_)=>Task.FromResult<SearchRow?>(null));
        service.Backups.Configure(AiBackupProbe.Account,AiBackupProbe.CfKey,"",true,true);
        var persona=service.Personas.Save(null,"測試人格","PERSONA_CF_TEST","直接回答");service.Personas.Activate(persona.Id);
        var reply=await service.AskAsync("那現在只聊天呢？",[new("user","前文測試內容"),new("model","需要新資料才搜尋。")],"auto",default);
        Check(generations==1&&reply.text=="直接聊就好。"&&reply.model==NativeBackupModels.CloudflareModel&&!reply.search_used,"ordinary CF chat uses one generation and correct model label");
        variant="memory";generations=0;
        reply=await service.AskAsync("我養黑王蛇，明早九點叫我拿包裹",[],"auto",default);
        Check(generations==1&&reply.memory_suggestions?.Length==1&&reply.personal_actions?.Length==1,"combined reply retains grounded memory and reminder independently");
        Check(!reply.text.Contains("已為您")&&reply.text.Contains("之後直接講重點"),"pending actions cannot be acknowledged before local commit");
        variant="search";generations=0;
        await service.Search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["exa"]="exa-synthetic0000000"},new HashSet<string>(),default);
        reply=await service.AskAsync("我養黑王蛇，幫我查飼養資料",[],"auto",default);
        Check(generations==2&&searches==1&&reply.search_used&&reply.sources?.Length==1&&reply.memory_suggestions?.Length==1,"CF search still understands once and synthesizes once without losing memory");
        variant="rewrite";generations=0;
        reply=await service.AskAsync("我養黑王蛇，今天想聊一下",[],"auto",default);
        Check(generations==2&&!NativeBrain.HasClosingQuestion(reply.text)&&reply.memory_suggestions?.Length==1,"combined answer with an unnecessary closing question rewrites once without reextracting memory");
        variant="invitation";generations=0;
        reply=await service.AskAsync("我養黑王蛇。設定反覆改來改去很麻煩，只想吐槽。",[],"auto",default);
        Check(generations==2&&reply.text.Contains("操作卻得重來")&&reply.memory_suggestions?.Length==1,"closing stock invitation is rewritten once while retaining grounded memory");
        variant="quoted";generations=0;
        reply=await service.AskAsync("『我聽著』是什麼意思？",[],"auto",default);
        Check(generations==1&&reply.text.Contains("『我聽著』"),"explaining a quoted phrase does not trigger a style rewrite");
        variant="clarify";generations=0;
        reply=await service.AskAsync("那個產品",[],"auto",default);
        Check(generations==1&&NativeBrain.HasClosingQuestion(reply.text),"necessary clarification remains one generation and keeps its question");
        variant="empty";generations=0;
        try{await service.AskAsync("你好",[],"auto",default);throw new Exception("FAIL: empty combined reply accepted");}
        catch(AssistantFailure){Check(generations==1,"empty combined reply is rejected rather than committed as a successful answer");}
        variant="cancel";generations=0;using var cancel=new CancellationTokenSource(70);
        try{await service.AskAsync("你好",[],"auto",cancel.Token);throw new Exception("FAIL: cancelled request accepted");}
        catch(OperationCanceledException){Check(generations==1&&service.Backups.Status().Single(p=>p.Id=="cloudflare").Reason=="ready","cancellation does not send another request or pause CF");}

        var groqCalls=0;
        using var groqHttp=new HttpClient(new AiBackupProbe.Handler(async(request,token)=>{
            Check(request.RequestUri!.Host=="api.groq.com","ordinary Groq flow remains Groq-only");groqCalls++;
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            Check(!J.S(body["messages"]![0],"content").Contains(DialogueInstructions.CombinedReply),"CF-only combined rules do not alter Groq instructions");
            return Result(groqCalls==1?Plan("answer","分類草稿"):"""{"answer":"真正答覆"}""",true);
        }));
        using var groqService=new NativeAssistantService(AiBackupProbe.Root(),groqHttp);
        groqService.Backups.Configure("","",AiBackupProbe.GroqKey,true,true);
        reply=await groqService.AskAsync("你好",[],"auto",default);
        Check(groqCalls==2&&reply.text=="真正答覆","Groq still uses its separate writer; the optimization is limited to CF");
        Console.WriteLine($"{count}/{count} native Cloudflare fast reply checks PASS; synthetic HTTP only, zero remote calls.");
    }
}
