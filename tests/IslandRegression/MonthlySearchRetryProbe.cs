using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant.Native;

internal static class MonthlySearchRetryProbe
{
    private static int _checks;
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);_checks++;Console.WriteLine("PASS: "+label);}
    private static long Ms(string date)=>DateTimeOffset.Parse(date).ToUnixTimeMilliseconds();
    private static string Root()=>Path.Combine(Path.GetTempPath(),"island-monthly-search-retry-test-"+Guid.NewGuid().ToString("N"));
    private static JsonObject Provider(NativeSearch search,string id)=>search.Status()["providers"]!.AsArray().Single(x=>J.S(x,"id")==id)!.AsObject();
    private static HttpResponseMessage Json(string content)=>new(HttpStatusCode.OK){Content=new StringContent(content,Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>run(request,token);}
    private static HttpResponseMessage Fallback(HttpRequestMessage request,long time)
    {
        if(request.RequestUri!.Host=="api.tavily.com")return Success();
        if(request.RequestUri.AbsolutePath=="/v2/team/credit-usage")return Json(new JsonObject{["success"]=true,["data"]=new JsonObject{["planCredits"]=1000,["remainingCredits"]=999,["billingPeriodStart"]=DateTimeOffset.FromUnixTimeMilliseconds(time).AddDays(-1).ToString("O"),["billingPeriodEnd"]=DateTimeOffset.FromUnixTimeMilliseconds(time).AddDays(29).ToString("O")}}.ToJsonString());
        return Json("""{"success":true,"data":{"web":[{"title":"fallback","url":"https://example.com","description":"synthetic evidence"}]},"creditsUsed":2}""");
    }
    private static HttpResponseMessage Success()=>Json("""{"results":[{"title":"result","url":"https://example.com","text":"synthetic evidence"}],"costDollars":{"total":0.007},"usage":{"credits":1}}""");
    private static Task Configure(NativeSearch search,string id,string fallback)=>search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){[id]=id+"-synthetic-key",[fallback]=fallback+"-synthetic-key"},new HashSet<string>(),default);
    internal static async Task<int> RunAsync()
    {
        _checks=0;var time=Ms("2026-10-06T09:00:00+08:00");var retry=Ms("2026-11-02T00:05:00+08:00");
        Check(NativeSearch.NextMonthlyRetry(time)==retry,"Monthly providers retry November 2 at 00:05 Taiwan time");
        Check(NativeSearch.NextMonthlyRetry(Ms("2026-12-31T23:59:00+08:00"))==Ms("2027-01-02T00:05:00+08:00"),"Monthly retry year rollover");
        Check(NativeSearch.NextMonthlyRetry(Ms("2026-10-31T23:59:00Z"))==Ms("2026-12-02T00:05:00+08:00"),"Monthly retry uses Taiwan month rather than UTC month");
        foreach(var id in new[]{"exa","tavily"}){
        var host=id=="exa"?"api.exa.ai":"api.tavily.com";var fallback=id=="exa"?"tavily":"firecrawl";
        time=Ms("2026-10-06T09:00:00+08:00");
        foreach(var status in new[]{401,402,403,429,500,503}){
            var calls=0;using var http=new HttpClient(new Handler((r,t)=>{if(r.RequestUri!.Host==host){calls++;return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));}return Task.FromResult(Fallback(r,time));}));
            using var search=new NativeSearch(Root(),http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null));await Configure(search,id,fallback);
            Check((await search.SearchAsync("失敗備援 "+status,default)).provider_id==fallback,id+" HTTP "+status+" uses fallback");
            Check(J.S(Provider(search,id),"reason")==id+"_failed"&&J.N(Provider(search,id),"disabledUntil")==retry,id+" HTTP "+status+" persists monthly stop");
            await search.SearchAsync("第二個查詢 "+status,default);Check(calls==1,id+" HTTP "+status+" is not retried while disabled");
        }
        foreach(var failure in new[]{"network","timeout","malformed"}){
            using var http=new HttpClient(new Handler((r,t)=>{if(r.RequestUri!.Host!=host)return Task.FromResult(Fallback(r,time));if(failure=="network")throw new HttpRequestException("synthetic offline");if(failure=="timeout")throw new OperationCanceledException();return Task.FromResult(Json("not-json"));}));
            using var search=new NativeSearch(Root(),http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null));await Configure(search,id,fallback);
            Check((await search.SearchAsync("傳輸測試 "+failure,default)).provider_id==fallback&&J.N(Provider(search,id),"disabledUntil")==retry,id+" "+failure+" stops and falls back");
        }
        var root=Root();var providerCalls=0;var succeed=false;
        using(var http=new HttpClient(new Handler((r,t)=>{if(r.RequestUri!.Host!=host)return Task.FromResult(Fallback(r,time));providerCalls++;return Task.FromResult(succeed?Success():new HttpResponseMessage(HttpStatusCode.PaymentRequired));}))){
            using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){await Configure(search,id,fallback);await search.SearchAsync("首次耗盡",default);}
            using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
                Check(J.N(Provider(search,id),"disabledUntil")==retry,id+": provider stop survives app restart");
                await Configure(search,id,fallback);Check(J.N(Provider(search,id),"disabledUntil")==retry,id+": Saving settings and keys does not bypass the monthly stop");
                time=Ms("2026-11-01T00:05:00+08:00");await search.SearchAsync("月底後仍停用",default);Check(providerCalls==1,id+": Month rollover does not unlock provider on the first");
                time=retry-1;await search.SearchAsync("恢復前一毫秒",default);Check(providerCalls==1,id+": provider remains stopped immediately before deadline");
                time=retry;await search.SearchAsync("恢復前一毫秒",default);var next=Ms("2026-12-02T00:05:00+08:00");Check(providerCalls==2&&J.N(Provider(search,id),"disabledUntil")==next,id+": Due retry bypasses fallback cache; failure stops until the following month's second");
                succeed=true;time=next;var result=await search.SearchAsync("到期恢復成功",default);Check(result.provider_id==id&&J.S(Provider(search,id),"reason")=="ready",id+": A successful due retry restores provider priority");
            }
        }
        time=Ms("2026-10-06T09:00:00+08:00");root=Root();
        using(var http=new HttpClient(new Handler((r,t)=>Task.FromResult(Success()))))
        using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            var settings=NativeSearch.DefaultSettings();settings["providers"]![id]!["cap"]=0;await search.ConfigureAsync(settings,new(){[id]=id+"-synthetic-key"},new HashSet<string>(),default);
            using(var db=new LocalSqlite(Path.Combine(root,"search-state.sqlite"))){
                var seeded=new JsonObject{["start"]=time,["end"]=retry,["used"]=id=="exa"?15:1500,["requests"]=2000,["reason"]="quota",["failureReason"]="rate_limit",["disabledUntil"]=retry,["official"]=new JsonObject{["remaining"]=0}};
                db.Query("INSERT OR REPLACE INTO providers VALUES(?,?)",id,seeded.ToJsonString());
            }
            Check((await search.SearchAsync("超過舊九美元上限",default)).provider_id==id&&J.S(Provider(search,id),"reason")=="ready",id+": Legacy local cap and local quota stop are removed without discarding usage");
            Check(Provider(search,id)["cap"] is null&&search.Status()["settings"]!["providers"]![id]!["cap"] is null,id+": provider has no configured or reported local cap");
            Check(J.N(Provider(search,id),"requests")==2001,id+": Removing provider cap preserves recorded request count");
        }
        using(var http=new HttpClient(new Handler(async(r,t)=>{await Task.Delay(10000,t);return Success();})))
        using(var search=new NativeSearch(Root(),http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            await Configure(search,id,fallback);using var cancel=new CancellationTokenSource(50);
            try{await search.SearchAsync("使用者取消",cancel.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            Check(J.S(Provider(search,id),"reason")=="ready",id+": User cancellation does not count as provider failure");
        }
        }
        await TavilyUsageChecksAsync();
        return _checks;
    }
    private static async Task TavilyUsageChecksAsync()
    {
        var root=Root();var time=Ms("2026-10-06T09:00:00+08:00");var retry=NativeSearch.NextMonthlyRetry(time);var usageFail=false;var searched=0;var usageCalls=0;
        using var http=new HttpClient(new Handler((r,t)=>{
            if(r.RequestUri!.AbsolutePath=="/usage"){
                usageCalls++;if(usageFail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(Json("""{"account":{"plan_usage":1000,"plan_limit":1000,"paygo_limit":0,"paygo_usage":0},"key":{"usage":1000,"limit":1000}}"""));
            }
            searched++;return Task.FromResult(Success());
        }));
        using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            await search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["tavily"]="tavily-synthetic-key"},new HashSet<string>(),default);
            using(var db=new LocalSqlite(Path.Combine(root,"search-state.sqlite"))){
                db.Query("UPDATE providers SET value=? WHERE id=?",new JsonObject{["start"]=time,["end"]=retry,["used"]=0,["requests"]=0,["reason"]="invalid",["failureReason"]="invalid",["disabledUntil"]=time+60000}.ToJsonString(),"tavily");
            }
            Check(J.S(Provider(search,"tavily"),"reason")=="ready"&&searched==0,"Legacy Tavily balance errors do not become search failure stops");
            using(var db=new LocalSqlite(Path.Combine(root,"search-state.sqlite"))){
                db.Query("UPDATE providers SET value=? WHERE id=?",new JsonObject{["start"]=time,["end"]=retry,["used"]=0,["requests"]=0,["reason"]="tavily_failed",["failureReason"]="invalid",["disabledUntil"]=retry}.ToJsonString(),"tavily");
            }
            Check(J.S(Provider(search,"tavily"),"reason")=="ready"&&searched==0,"Migrated non-search Tavily errors are removed without API calls");
            await search.RefreshUsageAsync("tavily",default);
            Check(J.N(Provider(search,"tavily"),"officialRemaining")==0,"Tavily retains optional official usage display");
            Check((await search.SearchAsync("餘額零仍交由官方判斷",default)).provider_id=="tavily"&&usageCalls==1&&searched==1,"Tavily search ignores advisory remaining balance and skips usage requests");
            using(var db=new LocalSqlite(Path.Combine(root,"search-state.sqlite"))){
                db.Query("UPDATE providers SET value=? WHERE id=?",new JsonObject{["start"]=time,["end"]=Ms("2026-11-01T00:00:00Z"),["used"]=1500,["requests"]=1200,["reason"]="tavily_failed",["failureReason"]="quota",["disabledUntil"]=retry,["periodVerified"]=true}.ToJsonString(),"tavily");
                var settings=NativeSearch.DefaultSettings();settings["providers"]!["tavily"]!["cap"]=900;db.Query("UPDATE settings SET value=? WHERE id=1",settings.ToJsonString());
            }
        }
        using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            Check(search.Status()["settings"]!["providers"]!["tavily"]!["cap"] is null&&J.N(Provider(search,"tavily"),"requests")==1200,"Tavily startup migration removes cap and preserves request count");
            time=Ms("2026-11-01T09:00:00+08:00");await search.RefreshUsageAsync("tavily",default);
            Check(J.N(Provider(search,"tavily"),"disabledUntil")==retry,"Tavily official period refresh cannot unlock a monthly failure stop");
            usageFail=true;try{await search.RefreshUsageAsync("tavily",default);throw new Exception("Expected usage error");}catch(SearchFailure){}
            Check(J.N(Provider(search,"tavily"),"disabledUntil")==retry,"Tavily usage lookup failure does not change the search retry date");
            time=retry;usageFail=false;await search.SearchAsync("Tavily 恢復",default);
            Check(J.S(Provider(search,"tavily"),"reason")=="ready","Tavily resumes after optional usage refreshes");
        }
    }

}
