using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant.Native;

internal static class FirecrawlBillingProbe
{
    private static int _checks;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);_checks++;Console.WriteLine("PASS: "+name);}
    private static long Ms(string value)=>DateTimeOffset.Parse(value).ToUnixTimeMilliseconds();
    private static JsonObject Provider(NativeSearch search)=>search.Status()["providers"]![2]!.AsObject();
    private static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(run(request));}
    private static JsonObject Settings(int day=6,int minute=81){var s=NativeSearch.DefaultSettings();s["providers"]!["firecrawl"]!["billingDay"]=day;s["providers"]!["firecrawl"]!["billingMinute"]=minute;return s;}
    private static string Root()=>Path.Combine(Path.GetTempPath(),"island-firecrawl-billing-"+Guid.NewGuid().ToString("N"));
    private static async Task Unavailable(NativeSearch search,string query){try{await search.SearchAsync(query,default);throw new Exception("Expected unavailable");}catch(SearchFailure){}}
    internal static async Task<int> RunAsync()
    {
        _checks=0;var time=Ms("2026-10-06T09:00:00+08:00");var next=Ms("2026-11-06T01:21:00+08:00");var settings=Settings();var options=settings["providers"]!["firecrawl"]!;
        Check(NativeSearch.NextBilling(time,options)==next,"Firecrawl honors manual day 6 and time 01:21 Taiwan");
        Check(NativeSearch.NextBilling(Ms("2026-10-06T01:20:00+08:00"),options)==Ms("2026-10-06T01:21:00+08:00"),"Firecrawl selects the next renewal in the current month when possible");
        Check(NativeSearch.NextBilling(Ms("2026-12-31T23:59:00+08:00"),options)==Ms("2027-01-06T01:21:00+08:00"),"Firecrawl billing year rollover");
        Check(NativeSearch.NextBilling(Ms("2027-02-01T00:00:00+08:00"),Settings(31,5)["providers"]!["firecrawl"]!)==Ms("2027-02-28T00:05:00+08:00"),"Firecrawl clamps day 31 in February");
        Check(NativeSearch.NextBilling(Ms("2028-02-01T00:00:00+08:00"),Settings(31,5)["providers"]!["firecrawl"]!)==Ms("2028-02-29T00:05:00+08:00"),"Firecrawl supports leap-year February");
        foreach(var pair in new[]{(0,81),(32,81),(6,-1),(6,1440)}){try{NativeSearch.ValidateSettings(Settings(pair.Item1,pair.Item2));throw new Exception("Expected invalid billing setting");}catch(SearchFailure){Check(true,"Firecrawl invalid billing schedule rejected");}}
        var root=Root();var remaining=0;var lookups=0;var searches=0;var usageError=false;var searchQuota=false;
        using var http=new HttpClient(new Handler(request=>{
            if(request.RequestUri!.AbsolutePath=="/v2/team/credit-usage"){
                lookups++;if(usageError)return new(HttpStatusCode.ServiceUnavailable);
                return Json(new JsonObject{["success"]=true,["data"]=new JsonObject{["remainingCredits"]=remaining,["planCredits"]=5000,["billingPeriodStart"]="2026-10-01T00:00:00Z",["billingPeriodEnd"]="2026-11-01T00:00:00Z"}}.ToJsonString());
            }
            searches++;if(searchQuota)return new(HttpStatusCode.PaymentRequired);
            return Json("""{"success":true,"data":{"web":[{"title":"synthetic","url":"https://example.com","description":"mock evidence"}]},"creditsUsed":2}""");
        }));
        using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            settings["providers"]!["firecrawl"]!["cap"]=0;
            await search.ConfigureAsync(settings,new(){["firecrawl"]="fc-synthetic-key0000"},new HashSet<string>(),default);
            Check(Provider(search)["cap"] is null&&search.Status()["settings"]!["providers"]!["firecrawl"]!["cap"] is null,"Firecrawl removes configured and reported local caps");
            await Unavailable(search,"沒有官方點數");
            Check(lookups==1&&searches==0&&J.S(Provider(search),"reason")=="quota","Firecrawl checks official balance and avoids spending when empty");
            Check(J.N(Provider(search),"disabledUntil")==next,"Firecrawl quota uses manual renewal instead of API November 1 period");
            await Unavailable(search,"第二個查詢");Check(lookups==1,"Firecrawl stays paused without repeated usage queries before renewal");
        }
        using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            Check(J.N(Provider(search),"disabledUntil")==next,"Firecrawl quota pause survives restart");
            await search.ConfigureAsync(Settings(7),new(),new HashSet<string>(),default);
            Check(J.N(Provider(search),"disabledUntil")==Ms("2026-10-07T01:21:00+08:00"),"Changing Firecrawl billing day updates the paused retry date");
            await search.ConfigureAsync(Settings(),new(),new HashSet<string>(),default);
            time=next;await Unavailable(search,"帳單日尚未補點數");
            Check(lookups==2&&J.N(Provider(search),"disabledUntil")==time+3600000,"Firecrawl overdue empty balance retries after one hour");
            await Unavailable(search,"一小時之前");Check(lookups==2,"Firecrawl does not poll constantly while waiting for renewal");
            time+=3600000;remaining=2500;var result=await search.SearchAsync("補回點數",default);
            Check(result.provider_id=="firecrawl"&&searches==1&&J.S(Provider(search),"reason")=="ready","Firecrawl resumes only after official credits are available");
            Check(J.N(Provider(search),"officialRemaining")==2498,"Firecrawl reports official credits reconciled with search usage");
            using(var db=new LocalSqlite(Path.Combine(root,"search-state.sqlite"))){
                db.Query("UPDATE providers SET value=? WHERE id=?",new JsonObject{["start"]=time,["end"]=time+86400000,["used"]=1500,["requests"]=1000,["reason"]="quota",["failureReason"]="rate_limit",["disabledUntil"]=time+86400000}.ToJsonString(),"firecrawl");
            }
            Check((await search.SearchAsync("超過舊九百點",default)).provider_id=="firecrawl"&&J.N(Provider(search),"requests")==1001,"Firecrawl old local quota and 900-credit cap do not block searches or erase counts");
            searchQuota=true;await Unavailable(search,"搜尋回傳官方點數不足");
            Check(J.S(Provider(search),"reason")=="quota"&&J.N(Provider(search),"disabledUntil")==NativeSearch.NextBilling(time,options),"Firecrawl search quota error pauses until the selected billing date");
            searchQuota=false;await search.RefreshUsageAsync("firecrawl",default);
            Check(J.S(Provider(search),"reason")=="ready","Manual official refresh can restore Firecrawl when credits are available");
            usageError=true;await search.ConfigureAsync(Settings(),new(),new HashSet<string>(),default);time+=61000;await Unavailable(search,"額度服務離線");
            Check(J.S(Provider(search),"reason")=="unavailable"&&J.N(Provider(search),"disabledUntil")<time+3600000,"Firecrawl usage endpoint outage uses a short cooldown, not a billing-month pause");
        }
        using(var minimal=new HttpClient(new Handler(r=>r.RequestUri!.AbsolutePath.EndsWith("credit-usage")?Json("""{"success":true,"data":{"remainingCredits":10}}"""):Json("""{"success":true,"data":{"web":[]},"creditsUsed":2}"""))))
        using(var search=new NativeSearch(Root(),minimal,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            await search.ConfigureAsync(Settings(),new(){["firecrawl"]="fc-synthetic-key0000"},new HashSet<string>(),default);
            Check((await search.SearchAsync("沒有官方帳單日期",default)).provider_id=="firecrawl","Firecrawl only needs remaining credits; manual calendar works without API period metadata");
        }
        return _checks;
    }
}
