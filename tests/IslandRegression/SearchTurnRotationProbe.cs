using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant.Native;

internal static class SearchTurnRotationProbe
{
    private static int _checks;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);_checks++;Console.WriteLine("PASS: "+name);}
    private static string Root()=>Path.Combine(Path.GetTempPath(),"island-search-rotation-"+Guid.NewGuid().ToString("N"));
    private static JsonObject Options(bool rotate=true){var s=NativeSearch.DefaultSettings();s["rotationMode"]=rotate?"per-turn":"priority";return s;}
    private static readonly Dictionary<string,string> Keys=new(){["exa"]="exa-synthetic-key",["tavily"]="tavily-synthetic-key",["firecrawl"]="firecrawl-synthetic-key"};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>run(request,token);}
    private static HttpResponseMessage Success(HttpRequestMessage r)=>new(HttpStatusCode.OK){Content=new StringContent(
        r.RequestUri!.AbsolutePath=="/v2/team/credit-usage"?
        """{"success":true,"data":{"remainingCredits":999}}""":r.RequestUri.Host=="api.firecrawl.dev"?
        """{"success":true,"data":{"web":[{"title":"result","url":"https://example.com","description":"synthetic evidence"}]},"creditsUsed":2}""":
        """{"results":[{"title":"result","url":"https://example.com","text":"synthetic evidence"}],"costDollars":{"total":0.007},"usage":{"credits":1}}""",Encoding.UTF8,"application/json")};
    private static NativeSearch Open(string root,HttpClient http)=>new(root,http,reader:(_,_)=>Task.FromResult<SearchRow?>(null));
    private static Task Configure(NativeSearch s,JsonObject o,Dictionary<string,string>? keys=null)=>s.ConfigureAsync(o,keys??new(),new HashSet<string>(),default);
    internal static async Task<int> RunAsync()
    {
        _checks=0;var calls=new List<string>();var failExa=false;var cancelExa=false;
        using var http=new HttpClient(new Handler(async(r,t)=>{
            if(r.RequestUri!.AbsolutePath!="/v2/team/credit-usage")calls.Add(r.RequestUri.Host);
            if(r.RequestUri.Host=="api.exa.ai"&&cancelExa){await Task.Delay(10000,t);}
            return r.RequestUri.Host=="api.exa.ai"&&failExa?new(HttpStatusCode.TooManyRequests):Success(r);
        }));
        var root=Root();var draft=Options();
        using(var s=Open(root,http)){
            await Configure(s,NativeSearch.DefaultSettings(),Keys);
            Check((await s.SearchAsync("預設優先第一次",default)).provider_id=="exa"&&(await s.SearchAsync("預設優先第二次",default)).provider_id=="exa","Priority mode remains the default");
            await Configure(s,draft);
            Check((await s.SearchAsync("輪替第一輪",default)).provider_id=="exa","Rotation starts at the configured first provider");
            var before=calls.Count;
            Check((await s.SearchAsync("輪替第一輪",default)).provider_id=="exa"&&calls.Count==before,"Cached search makes no API request");
            Check((await s.SearchAsync("輪替第二輪",default)).provider_id=="tavily","Cache hit does not advance the provider cursor");
            Check((await s.SearchAsync("輪替第三輪",default)).provider_id=="firecrawl","Rotation reaches the third provider");
            Check((await s.SearchAsync("輪替第四輪",default)).provider_id=="exa","Rotation wraps back to the first provider");
            await Configure(s,draft);
            Check((await s.SearchAsync("保存舊草稿不倒退",default)).provider_id=="tavily","Saving a stale settings draft preserves rotation progress");
        }
        using(var s=Open(root,http)){
            Check((await s.SearchAsync("重新啟動仍接續",default)).provider_id=="firecrawl","Rotation progress survives app restart");
            var custom=Options();custom["order"]=new JsonArray("tavily","firecrawl","exa");await Configure(s,custom);
            Check((await s.SearchAsync("變更順序從頭",default)).provider_id=="tavily"&&(await s.SearchAsync("自訂順序第二輪",default)).provider_id=="firecrawl","Rotation follows custom order and resets only on order change");
            await Configure(s,Options(false));
            Check((await s.SearchAsync("關閉輪替",default)).provider_id=="exa"&&(await s.SearchAsync("繼續優先",default)).provider_id=="exa","Disabling rotation immediately restores priority mode");
            await Configure(s,Options());cancelExa=true;using var cancel=new CancellationTokenSource(80);
            try{await s.SearchAsync("取消正在輪替的搜尋",cancel.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            cancelExa=false;
            Check((await s.SearchAsync("取消後再次搜尋",default)).provider_id=="exa","User cancellation neither advances rotation nor disables the provider");
        }
        using(var s=Open(Root(),http)){
            await Configure(s,Options(),Keys);failExa=true;
            Check((await s.SearchAsync("故障使用第二家",default)).provider_id=="tavily","Failed provider falls over within the same turn");
            Check((await s.SearchAsync("故障後接第三家",default)).provider_id=="firecrawl","Next turn starts after the provider that actually succeeded");
            Check((await s.SearchAsync("月度封鎖自動跳過",default)).provider_id=="tavily","Persisted monthly failure is skipped on later cycles");
            Check(J.S(s.Status()["providers"]!.AsArray().First(p=>J.S(p,"id")=="exa"),"reason")=="exa_failed","Rotation does not bypass existing provider recovery rules");
            failExa=false;
        }
        using(var s=Open(Root(),http)){
            var options=Options();options["providers"]!["tavily"]!["enabled"]=false;await Configure(s,options,Keys);
            Check((await s.SearchAsync("啟用第一家",default)).provider_id=="exa"&&(await s.SearchAsync("跳過停用的第二家",default)).provider_id=="firecrawl","Rotation skips manually disabled providers");
            await Configure(s,options);await s.ConfigureAsync(options,new(),new HashSet<string>{"firecrawl"},default);
            Check((await s.SearchAsync("只剩一家金鑰",default)).provider_id=="exa"&&(await s.SearchAsync("跳過無金鑰",default)).provider_id=="exa","Rotation skips missing keys and remains usable with one provider");
        }
        var failureRoot=Root();
        using(var s=Open(failureRoot,http)){
            var options=Options();await Configure(s,options,new(){["exa"]=Keys["exa"]});failExa=true;
            try{await s.SearchAsync("全部不可用",default);throw new Exception("Expected search failure");}catch(SearchFailure){}
            using(var db=new LocalSqlite(Path.Combine(failureRoot,"search-state.sqlite"))){
                Check(Convert.ToInt32(db.One("SELECT next_index FROM rotation WHERE id=1")!["next_index"])==0,"A fully failed search never advances the cursor");
            }
            failExa=false;
        }
        var legacyRoot=Root();
        using(var s=Open(legacyRoot,http)){
            var invalid=Options();invalid["rotationMode"]="random";
            try{await Configure(s,invalid);throw new Exception("Expected invalid settings");}catch(SearchFailure e){Check(e.Code=="invalid","Invalid rotation modes are rejected");}
            using var db=new LocalSqlite(Path.Combine(legacyRoot,"search-state.sqlite"));var legacy=NativeSearch.DefaultSettings();legacy.Remove("rotationMode");db.Query("UPDATE settings SET value=? WHERE id=1",legacy.ToJsonString());
        }
        using(var s=Open(legacyRoot,http))Check(J.S(s.Status()["settings"],"rotationMode")=="priority","Existing installations migrate without enabling rotation");
        return _checks;
    }
}
