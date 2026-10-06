using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class AiPersonalIntentProbe
{
    internal static async Task<int> RunAsync()
    {
        var passed=0;
        void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        var root=Path.Combine(Path.GetTempPath(),"island-ai-personal-"+Guid.NewGuid().ToString("N"));
        var now=DateTimeOffset.Parse("2026-10-06T12:00:00+08:00");
        JsonObject Memory(string category,string quote,string? text=null,string replace="")=>new(){
            ["category"]=category,["quote"]=quote,["text"]=text??quote,["subject"]="user",["stability"]="durable",["replace_id"]=replace};
        const string question="我養一條黑王蛇。不要每次回答都叫我名字，幫我查飼養資料。";
        var memories=new JsonArray(Memory("寵物飼養","我養一條黑王蛇"),Memory("回覆偏好","不要每次回答都叫我名字"));
        var plan=new JsonObject{["action"]="search",["answer"]="",["search_query"]="黑王蛇 飼養 資料",["memory"]=memories,["operations"]=new JsonArray()};
        var parsed=NativeBrain.ParsePlan(plan.ToJsonString(),question,NativeBrain.Clock(now));
        Check(parsed.Action=="search"&&parsed.Memory.Length==2,"AI independently selects search plus multiple memories");
        var store=new PersonalAssistantStore(Path.Combine(root,"personal.dpapi"));
        foreach(var item in parsed.Memory)store.AcceptModelSuggestion(question,item,now);
        Check(store.MemoryCategories.SequenceEqual(new[]{"回覆偏好","寵物飼養"}),"AI creates Chinese categories outside old fixed list");
        Check(new PersonalAssistantStore(Path.Combine(root,"personal.dpapi")).Memories.Count==2,"new Chinese categories survive encrypted reload");
        Check(store.AcceptModelSuggestion(question,parsed.Memory[0],now) is null&&store.Memories.Count==2,"duplicate memory produces neither duplicate nor false save notice");
        var preference=store.AcceptModelSuggestion("不用選檔，一律貼上",new("圖片輸入偏好","不用選檔，一律貼上","圖片輸入偏好一律使用貼上","user","durable",""),now);
        Check(preference?.Category=="圖片輸入偏好","contextual colloquial preference needs no trigger phrase or first-person prefix");
        Check(PersonalMemoryFilter.Validate("我偏好深色介面。不要叫我名字。",new("回覆偏好","我偏好深色介面。","偏好深色介面","user","durable","")) is not null,"quote including sentence punctuation accepted before following clause");
        Check(!NativeBrain.GuardPendingActions("已為您設定提醒。建議保留軟體開著。",true).Contains("已為")&&NativeBrain.GuardPendingActions("已為您設定提醒。建議保留軟體開著。",true).Contains("保留軟體"),"model cannot acknowledge an operation before local commit");
        var pet=store.Memories.First(m=>m.Category=="寵物飼養");
        store.AcceptModelSuggestion("我更正一下，我養的是玉米蛇",new("寵物飼養","我養的是玉米蛇","我養一條玉米蛇","user","durable",pet.Id),now);
        Check(store.Memories.Count==3&&store.Memories.Single(m=>m.Id==pet.Id).Text=="我養一條玉米蛇","AI correction replaces selected record without duplicating identity");
        Check(store.AcceptModelSuggestion("我喜歡黑色",new("喜好","我喜歡紅色","我喜歡紅色","user","durable",""),now) is null,"memory quote must come from current user text");
        Check(store.AcceptModelSuggestion("他養蛇",new("寵物飼養","他養蛇","他養蛇","other","durable",""),now) is null,"AI third-person classification cannot become user memory");
        Check(store.AcceptModelSuggestion("今天不開心",new("情緒","今天不開心","今天不開心","user","temporary",""),now) is null,"AI temporary classification cannot become persistent memory");
        const string reminder="明早九點叫我去拿包裹";
        var created=store.ApplyModelAction(reminder,new("remind",reminder,"拿包裹","2026-10-07T09:00:00+08:00",""),now);
        Check(created?.Text.Contains(EndfieldChargePlus.LocalizationManager.Text("已設定提醒","Reminder scheduled"))==true&&store.Reminders.Count==1,"AI reminder works without 提醒我 keyword and follows interface language");
        store.ApplyModelAction(reminder,new("remind",reminder,"拿包裹","2026-10-07T09:00:00+08:00",""),now);
        Check(store.Reminders.Count==1,"AI reminder retry is idempotent");
        Check(store.ApplyModelAction("別刪",new("delete_memory","刪掉寵物記憶","","",pet.Id),now) is null,"operation must be grounded in actual user turn");
        store.ClearMemories();
        var reopened=new PersonalAssistantStore(Path.Combine(root,"personal.dpapi"));
        Check(reopened.Memories.Count==0&&reopened.Reminders.Count==1,"clear palace removes memories while preserving reminder schedules");
        Check(reopened.WithMemory([],"取消包裹提醒").Any(m=>m.text.Contains(reopened.Reminders[0].Id)),"reminder identity available to AI even with empty palace");

        var config=new NativeConfiguration(root);config.Initialize();config.Configure("AIzaTestOnlyNeverSentToGoogle1234567890000",true);
        var generates=0;var searches=0;var forcedAnswer=false;
        HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};
        string Response(JsonObject value)=>new JsonObject{["candidates"]=new JsonArray(new JsonObject{["content"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=value.ToJsonString()})}}),
            ["usageMetadata"]=new JsonObject{["promptTokenCount"]=40,["candidatesTokenCount"]=20,["thoughtsTokenCount"]=0,["totalTokenCount"]=60}}.ToJsonString();
        using var http=new HttpClient(new Handler(async(r,t)=>{
            var body=JsonNode.Parse(await r.Content!.ReadAsStringAsync(t))!;
            if(r.RequestUri!.Host=="api.exa.ai"){searches++;return Json("""{"results":[{"title":"飼養資料","url":"https://example.com/care","text":"Mock reptile care evidence."}],"costDollars":{"total":0.005}}""");}
            if(r.RequestUri.AbsolutePath.EndsWith(":countTokens"))return Json("""{"totalTokens":40}""");
            generates++;
            if(forcedAnswer){
                var writer=body["generationConfig"]?["responseFormat"]?["text"]?["schema"]?["properties"]?.AsObject().Count==1;
                return Json(Response(writer?new JsonObject{["answer"]="新版"}:new JsonObject{["action"]="answer",["answer"]="新版",["search_query"]="",["memory"]=new JsonArray(),["operations"]=new JsonArray()}));
            }
            if(generates%2==1)return Json(Response(plan));
            return Json(Response(new JsonObject{["answer"]="飼養資料 [S1]",["memory"]=new JsonArray(Memory("寵物飼養","我養一條黑王蛇","我養一條獅子")),["operations"]=new JsonArray()}));
        }));
        long testOffset=0;
        using var service=new NativeAssistantService(root,http,now:()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+testOffset,reader:(_,_)=>Task.FromResult<SearchRow?>(null));
        await service.Search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["exa"]="exa-test-key0000"},new HashSet<string>(),default);
        var reply=await service.AskAsync(question,[],"auto",default);
        Check(generates==2&&searches==1&&reply.memory_suggestions?.Length==2,"one understanding call, one search, one summary retain planned memories");
        Check(reply.memory_suggestions![0].text=="我養一條黑王蛇","summary cannot replace planned memory with article/model inventions");
        forcedAnswer=true;var answered=await service.AskAsync("最新兩字請幫我換成新版",[],"auto",default);
        Check(!answered.search_used&&searches==1,"keyword 最新 no longer forces search against AI judgment");
        testOffset+=61000;
        answered=await service.AskAsync("我喜歡藍色",[],"auto",default);
        Check(answered.memory_suggestions?.Length==0,"first-person keyword no longer forces storage against AI judgment");
        Console.WriteLine($"{passed}/{passed} AI personal intent checks PASS");
        return passed;
    }
    internal static async Task LiveAsync()
    {
        var config=new NativeConfiguration();config.Initialize();
        using var ledger=new GeminiLedger(config.LedgerPath,config.Policy());
        var key=config.LoadKey();
        using var http=new HttpClient(new InspectHandler(key)){Timeout=Timeout.InfiniteTimeSpan};var gemini=new NativeGemini(ledger,key,http);
        var now=DateTimeOffset.Now;var clock=NativeBrain.Clock(now);
        var questions=new[]{
            "我養一條黑王蛇，幫我查飼養資料。",
            "我偏好深色介面。不要每次回答都叫我名字。",
            "假設我養黑王蛇，我朋友也養蛇。今天很煩，這段不要記。",
            "明早九點叫我去拿包裹。"
        };
        var plans=new List<(string Action,string Answer,string Query,MemorySuggestion[] Memory,PersonalAction[] Operations)>();
        foreach(var question in questions) {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var raw=await gemini.GenerateAsync([new("user",question)],NativeBrain.PlanningInstructions("auto",clock),
                JsonNode.Parse(NativePrompts.PlanningSchema)!.AsObject(),null,deadline.Token);
            plans.Add(NativeBrain.ParsePlan(raw,question,clock));
            Console.WriteLine("Live case: "+new JsonObject{["question"]=question,["response"]=JsonNode.Parse(raw)}.ToJsonString());
        }
        void Check(bool value,string name){if(!value)throw new Exception("FAIL: live AI "+name);Console.WriteLine("PASS: live AI "+name);}
        Check(plans[0].Action=="search"&&plans[0].Memory.Any(m=>m.text!.Contains("黑王蛇")),"search plus pet memory without explicit remember command");
        Check(plans[1].Action=="answer"&&plans[1].Memory.Length>=2,"chat plus separate preference and interaction memories");
        Check(plans[2].Memory.Length==0&&plans[2].Operations.Length==0,"hypothetical/third-person/temporary/opt-out not saved");
        var store=new PersonalAssistantStore(Path.Combine(Path.GetTempPath(),"island-ai-live-"+Guid.NewGuid().ToString("N"),"personal.dpapi"));
        foreach(var action in plans[3].Operations)store.ApplyModelAction(questions[3],action,now);
        var tomorrow=DateTimeOffset.Parse(JsonNode.Parse(clock)!["tomorrow"]!.GetValue<string>()+"T09:00:00+08:00");
        Check(store.Reminders.Count==1&&store.Reminders[0].Due==tomorrow,"colloquial reminder at correct Taipei time");
        Console.WriteLine("4/4 live AI checks PASS; four Gemini generations, no search API calls, no user memories changed.");
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);
    }
    private sealed class InspectHandler(string key):DelegatingHandler(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false})
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            var response=await base.SendAsync(request,token);
            if(!response.IsSuccessStatusCode) {
                var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
                var message=J.S(data,"error","message").Replace(key,"[redacted]");
                Console.WriteLine("Provider diagnostic: "+message[..Math.Min(message.Length,800)]);
            }
            return response;
        }
    }
}
