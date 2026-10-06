using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant.Native;

internal sealed record SearchRow(string title,string url,string body,string coverage,string? date,string retrieved_at);
internal sealed record SearchResult(string provider_id,SearchRow[] results);
internal sealed class SearchFailure : Exception
{
    public string Code{get;}
    public long RetryMs{get;}
    public SearchFailure(string code,long retry=60000):base(code){Code=code;RetryMs=retry;}
}

internal static class NativeHttp
{
    public static HttpClient Create()=>new(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=Timeout.InfiniteTimeSpan};
    public static async Task<JsonObject> JsonAsync(HttpClient http,string url,string keyHeader,string key,JsonNode? body,CancellationToken token,int timeout=6000)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(Math.Max(1,timeout));
        using var request=new HttpRequestMessage(body is null?HttpMethod.Get:HttpMethod.Post,url);
        request.Headers.TryAddWithoutValidation(keyHeader,keyHeader=="Authorization"?"Bearer "+key:key);
        if(body is not null)request.Content=new StringContent(body.ToJsonString(J.Json),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(!response.IsSuccessStatusCode)
        {
            var seconds=response.Headers.RetryAfter?.Delta?.TotalMilliseconds??(response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow)?.TotalMilliseconds??60000;
            var daily=false;
            if((int)response.StatusCode==429&&request.RequestUri?.Host=="generativelanguage.googleapis.com"){
                try{
                    await using var errorStream=await response.Content.ReadAsStreamAsync(deadline.Token);using var errorBytes=new MemoryStream();var chunk=new byte[4096];int n;
                    while((n=await errorStream.ReadAsync(chunk,deadline.Token))>0){if(errorBytes.Length+n>65536)break;errorBytes.Write(chunk,0,n);}
                    var error=JsonNode.Parse(errorBytes.ToArray());
                    if(error?["error"]?["details"] is JsonArray details)foreach(var detail in details){
                        if(detail?["violations"] is JsonArray violations)foreach(var violation in violations){var metric=J.S(violation,"quotaMetric")+" "+J.S(violation,"quotaId");daily|=Regex.IsMatch(metric,"per.?day|daily",RegexOptions.IgnoreCase);}
                        var delay=J.S(detail,"retryDelay");if(delay.Length>0)seconds=Math.Max(1000,NativeBackupModels.ResetDelay(delay));
                    }
                }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}catch{}
            }
            throw new ProviderResponseFailure((int)response.StatusCode,(long)Math.Clamp(seconds,1000,172800000),daily);
        }
        if(response.Content.Headers.ContentLength>2000000)throw new SearchFailure("invalid");
        await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var memory=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await stream.ReadAsync(buffer,deadline.Token))>0){if(memory.Length+read>2000000)throw new SearchFailure("invalid");memory.Write(buffer,0,read);}
        try{return JsonNode.Parse(memory.ToArray())?.AsObject()??throw new SearchFailure("invalid");}catch(JsonException){throw new SearchFailure("invalid");}
    }
}
internal sealed class ProviderResponseFailure : Exception
{
    public int Status{get;}public long RetryMs{get;}public bool DailyQuota{get;}
    public ProviderResponseFailure(int status,long retry=60000,bool dailyQuota=false):base("provider_http_"+status){Status=status;RetryMs=retry;DailyQuota=dailyQuota;}
}

internal sealed class NativeSearch : IDisposable
{
    internal static readonly string[] Ids=["exa","tavily","firecrawl"];
    internal static readonly string[] Names=["Exa Auto","Tavily Basic","Firecrawl Search"];
    internal static bool UsesMonthlyRetry(string id)=>id is "exa" or "tavily";
    private static readonly HashSet<string> Permanent=["auth","key_budget","paid_plan"];
    private static readonly Dictionary<string,double> Units=new(){["exa"]=.007,["tavily"]=1,["firecrawl"]=2};
    private static readonly Dictionary<string,string> Bases=new(){["exa"]="https://api.exa.ai",["tavily"]="https://api.tavily.com",["firecrawl"]="https://api.firecrawl.dev"};
    private readonly SemaphoreSlim _engine=new(1,1);
    private readonly object _sync=new();
    private readonly LocalSqlite _db;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _keyFile;
    private readonly Func<long> _now;
    private readonly Func<string,CancellationToken,Task<SearchRow?>>? _reader;
    private Dictionary<string,string> _keys=[];
    private long _keyVersion=-1;
    public NativeSearch(string directory,HttpClient? http=null,Func<long>? now=null,Func<string,CancellationToken,Task<SearchRow?>>? reader=null)
    {
        Directory.CreateDirectory(directory);_keyFile=Path.Combine(directory,"search-secrets.dpapi");_db=new LocalSqlite(Path.Combine(directory,"search-state.sqlite"));
        _http=http??NativeHttp.Create();_ownsHttp=http is null;_now=now??(()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());_reader=reader??PublicPageReader.ReadAsync;
        _db.Exec("CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS providers(id TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS locks(id TEXT PRIMARY KEY,owner TEXT NOT NULL,expires REAL NOT NULL); CREATE TABLE IF NOT EXISTS cache(id TEXT PRIMARY KEY,value TEXT NOT NULL,expires REAL NOT NULL); CREATE TABLE IF NOT EXISTS rotation(id INTEGER PRIMARY KEY,next_index INTEGER NOT NULL);");
        _db.Exec("INSERT OR IGNORE INTO rotation VALUES(1,0)");
        _db.Query("INSERT OR IGNORE INTO settings VALUES(1,?)",DefaultSettings().ToJsonString(J.Json));
        var legacy=JsonNode.Parse(Convert.ToString(_db.One("SELECT value FROM settings WHERE id=1")!["value"])!)!.AsObject();
        var original=legacy.ToJsonString(J.Json);NormalizeSettings(legacy);
        if(legacy.ToJsonString(J.Json)!=original)_db.Query("UPDATE settings SET value=? WHERE id=1",legacy.ToJsonString(J.Json));
    }
    internal static JsonObject DefaultSettings()=>JsonNode.Parse("""{"enabled":true,"rotationMode":"priority","order":["exa","tavily","firecrawl"],"timeoutMs":20000,"cooldownMs":60000,"providers":{"exa":{"enabled":true},"tavily":{"enabled":true},"firecrawl":{"enabled":true,"billingDay":1,"billingMinute":5}}}""")!.AsObject();
    private static void NormalizeSettings(JsonObject settings)
    {
        settings["rotationMode"]??="priority";
        foreach(var id in Ids)settings["providers"]![id]!.AsObject().Remove("cap");
        settings["providers"]!["firecrawl"]!["billingDay"]??=1;settings["providers"]!["firecrawl"]!["billingMinute"]??=5;
    }
    private static double D(JsonNode? node,params string[] keys){var value=J.At(node,keys);if(value is null)return 0;if(value.GetValueKind()!=JsonValueKind.Number)throw new SearchFailure("invalid");return double.Parse(value.ToJsonString(),CultureInfo.InvariantCulture);}
    private JsonObject Settings()
    {
        var settings=JsonNode.Parse(Convert.ToString(_db.One("SELECT value FROM settings WHERE id=1")!["value"])!)!.AsObject();NormalizeSettings(settings);return settings;
    }
    internal static long NextBilling(long ms,JsonNode options)
    {
        var date=DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TimeSpan.FromHours(8));var day=(int)J.N(options,"billingDay");var minutes=(int)J.N(options,"billingMinute");
        DateTimeOffset At(DateTimeOffset month)=>new(month.Year,month.Month,Math.Min(day,DateTime.DaysInMonth(month.Year,month.Month)),minutes/60,minutes%60,0,TimeSpan.FromHours(8));
        var next=At(date);if(next<=date)next=At(date.AddMonths(1));return next.ToUnixTimeMilliseconds();
    }
    private string BillingKey()=>J.N(Settings(),"providers","firecrawl","billingDay")+":"+J.N(Settings(),"providers","firecrawl","billingMinute");
    private Dictionary<string,string> Keys()
    {
        var version=File.Exists(_keyFile)?File.GetLastWriteTimeUtc(_keyFile).Ticks:0;
        if(_keyVersion!=version)
        {
            if(version==0)_keys=[];
            else
            {
                byte[]? plain=null;
                try{plain=ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(_keyFile)),null,DataProtectionScope.CurrentUser);_keys=JsonSerializer.Deserialize<Dictionary<string,string>>(plain)??[];}
                finally{if(plain is not null)CryptographicOperations.ZeroMemory(plain);}
            }
            _keyVersion=version;
        }
        return new(_keys);
    }
    private static (long Start,long End) Month(long ms){var date=DateTimeOffset.FromUnixTimeMilliseconds(ms);var start=new DateTimeOffset(date.Year,date.Month,1,0,0,0,TimeSpan.Zero);return(start.ToUnixTimeMilliseconds(),start.AddMonths(1).ToUnixTimeMilliseconds());}
    internal static long NextMonthlyRetry(long ms)
    {
        var taipei=DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TimeSpan.FromHours(8));
        return new DateTimeOffset(taipei.Year,taipei.Month,2,0,5,0,TimeSpan.FromHours(8)).AddMonths(1).ToUnixTimeMilliseconds();
    }
    private JsonObject State(string id)
    {
        var stored=_db.One("SELECT value FROM providers WHERE id=?",id);
        if(stored is not null){
            var state=JsonNode.Parse(Convert.ToString(stored["value"])!)!.AsObject();
            if(UsesMonthlyRetry(id)){
                var reason=J.S(state,"reason");
                // Earlier Tavily errors could come from the mandatory balance lookup,
                // before any search was dispatched. Only search failures use the new stop.
                if(id=="tavily"&&reason!=""&&(reason!="tavily_failed"||J.S(state,"failureOrigin")!="search"&&J.N(state,"requests")==0&&D(state,"used")==0)){
                    state["reason"]=null;state["disabledUntil"]=0;state["failureReason"]=null;Put(id,state);return state;
                }
                if(id=="tavily"&&reason=="tavily_failed"&&J.S(state,"failureOrigin")==""&&J.N(state,"requests")>0){state["failureOrigin"]="search";Put(id,state);}
                // Old local-budget stops no longer apply. Actual failures remain persisted.
                if(reason=="quota"&&J.S(state,"failureReason")!="quota"){state["reason"]=null;state["disabledUntil"]=0;Put(id,state);}
                else if(reason!=""&&reason!=id+"_failed"){
                    state["failureReason"]=reason;state["reason"]=id+"_failed";state["disabledUntil"]=NextMonthlyRetry(_now());Put(id,state);
                }
                else if(reason==id+"_failed"&&J.N(state,"disabledUntil")<=_now()){
                    state["reason"]=null;state["disabledUntil"]=0;Put(id,state);
                    // A recent fallback cache must not postpone the scheduled provider retry.
                    _db.Exec("DELETE FROM cache");
                }
            }
            if(id=="firecrawl"){
                var reason=J.S(state,"reason");
                if(reason=="paid_plan"||reason=="quota"&&J.S(state,"failureReason")!="quota"){state["reason"]=null;state["disabledUntil"]=0;Put(id,state);}
                else if(reason=="quota"&&J.S(state,"billingSchedule")!=BillingKey()){
                    state["billingSchedule"]=BillingKey();state["disabledUntil"]=NextBilling(_now(),Settings()["providers"]![id]!);Put(id,state);
                }
            }
            if(id=="firecrawl"&&J.S(state,"reason")=="quota"&&J.N(state,"disabledUntil")<=_now()&&J.N(state,"retryCacheStamp")!=J.N(state,"disabledUntil")){state["retryCacheStamp"]=J.N(state,"disabledUntil");Put(id,state);_db.Exec("DELETE FROM cache");}
            return state;
        }
        var period=Month(_now());return new JsonObject{["start"]=period.Start,["end"]=period.End,["used"]=0,["requests"]=0,["reason"]=null,["disabledUntil"]=0,["official"]=null};
    }
    private void Put(string id,JsonObject state)=>_db.Query("INSERT INTO providers VALUES(?,?) ON CONFLICT(id) DO UPDATE SET value=excluded.value",id,state.ToJsonString(J.Json));
    internal JsonObject Status()
    {
        lock(_sync)
        {
            var settings=Settings();var keys=Keys();var providers=new JsonArray();var configured=false;
            for(var index=0;index<Ids.Length;index++)
            {
                var id=Ids[index];var state=State(id);var enabled=J.B(settings,"enabled")&&J.B(settings,"providers",id,"enabled");var hasKey=keys.TryGetValue(id,out var key)&&!string.IsNullOrEmpty(key);configured|=enabled&&hasKey;
                var reason=!enabled?"disabled":!hasKey?"missing_key":(!UsesMonthlyRetry(id)&&Permanent.Contains(J.S(state,"reason")))||J.N(state,"disabledUntil")>_now()?J.S(state,"reason"):id=="firecrawl"&&J.S(state,"reason")=="quota"?"awaiting_balance":"ready";
                providers.Add(new JsonObject{["id"]=id,["name"]=Names[index],["hasKey"]=hasKey,["reason"]=reason,["used"]=D(state,"used"),["cap"]=null,["nextBilling"]=id=="firecrawl"?JsonValue.Create(NextBilling(_now(),settings["providers"]![id]!)):null,["requests"]=J.N(state,"requests"),["disabledUntil"]=J.N(state,"disabledUntil"),["failureReason"]=J.S(state,"failureReason"),["end"]=J.N(state,"end"),["officialCheckedAt"]=J.N(state,"official","checkedAt"),["officialRemaining"]=state["official"]?["remaining"]?.DeepClone()});
            }
            return new JsonObject{["configured"]=configured,["settings"]=settings,["providers"]=providers};
        }
    }
    internal async Task ConfigureAsync(JsonObject settings,Dictionary<string,string> replacements,ISet<string> clear,CancellationToken token)
    {
        settings=(JsonObject)settings.DeepClone();NormalizeSettings(settings);
        ValidateSettings(settings);await _engine.WaitAsync(token);
        try
        {
            lock(_sync)
            {
                var previous=Settings();
                var resetRotation=J.S(previous,"rotationMode")!=J.S(settings,"rotationMode")||previous["order"]!.ToJsonString()!=settings["order"]!.ToJsonString();
                var keys=Keys();foreach(var id in clear){if(!Ids.Contains(id))throw new SearchFailure("invalid");keys[id]="";}
                foreach(var (id,value) in replacements){if(!Ids.Contains(id)||!Regex.IsMatch(value,"^[\\x21-\\x7e]{10,512}$"))throw new SearchFailure("invalid");keys[id]=value;}
                var plain=JsonSerializer.SerializeToUtf8Bytes(keys);byte[] encrypted;
                try{encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(plain);}
                var tmp=_keyFile+".pending";var old=File.Exists(_keyFile)?File.ReadAllText(_keyFile):null;
                try
                {
                    File.WriteAllText(tmp,Convert.ToBase64String(encrypted));File.Move(tmp,_keyFile,true);
                    _db.Transaction(()=>{_db.Query("UPDATE settings SET value=? WHERE id=1",settings.ToJsonString(J.Json));if(resetRotation)_db.Exec("UPDATE rotation SET next_index=0 WHERE id=1");_db.Exec("DELETE FROM cache");foreach(var id in Ids){var state=State(id);var hash=J.Hash(keys.GetValueOrDefault(id,""));if(J.S(state,"keyFingerprint")!=hash){state["keyFingerprint"]=hash;state["official"]=null;if(Permanent.Contains(J.S(state,"reason"))){state["reason"]=null;state["disabledUntil"]=0;}Put(id,state);}}return 0;});
                    _keyVersion=-1;
                }
                catch{if(old is not null)File.WriteAllText(_keyFile,old);else if(File.Exists(_keyFile))File.Delete(_keyFile);throw;}
                finally{CryptographicOperations.ZeroMemory(encrypted);if(File.Exists(tmp))File.Delete(tmp);}
            }
        }
        finally{_engine.Release();}
    }
    internal static void ValidateSettings(JsonObject s)
    {
        try
        {
            if(s["enabled"] is not JsonValue||s["order"] is not JsonArray order||order.Count!=3||order.Select(x=>x!.GetValue<string>()).Distinct().Count()!=3||order.Any(x=>!Ids.Contains(x!.GetValue<string>())))throw new SearchFailure("invalid");
            _=s["enabled"]!.GetValue<bool>();if(J.N(s,"timeoutMs") is <3000 or >30000||J.N(s,"cooldownMs") is <1000 or >3600000)throw new SearchFailure("invalid");
            if(J.S(s,"rotationMode") is not ("priority" or "per-turn"))throw new SearchFailure("invalid");
            foreach(var id in Ids)_=s["providers"]![id]!["enabled"]!.GetValue<bool>();
            var day=D(s,"providers","firecrawl","billingDay");var minute=D(s,"providers","firecrawl","billingMinute");if(!double.IsFinite(day)||!double.IsFinite(minute)||day<1||day>31||minute<0||minute>1439||Math.Truncate(day)!=day||Math.Truncate(minute)!=minute)throw new SearchFailure("invalid");
        }
        catch(SearchFailure){throw;}catch{throw new SearchFailure("invalid");}
    }
    private async Task<JsonObject> RequestAsync(string id,string key,string route,JsonNode? body,CancellationToken token,int timeout)
    {
        try{return await NativeHttp.JsonAsync(_http,Bases[id]+route,id=="exa"?"x-api-key":"Authorization",key,body,token,timeout);}
        catch(ProviderResponseFailure e){throw new SearchFailure(e.Status switch{401 or 403=>"auth",402=>id=="exa"?"key_budget":"quota",432=>"quota",429=>"rate_limit",_=>"unavailable"},e.RetryMs);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new SearchFailure("timeout");}
        catch(HttpRequestException){throw new SearchFailure("unavailable");}
    }
    private async Task RefreshAsync(string id,string key,long deadline,CancellationToken token,bool manual=false)
    {
        JsonObject state;lock(_sync)state=State(id);
        if(UsesMonthlyRetry(id)&&!manual)
        {
            lock(_sync){if(_now()>=J.N(state,"end")){var month=Month(_now());state["start"]=month.Start;state["end"]=month.End;state["used"]=0;state["requests"]=0;state["official"]=null;}Put(id,state);}return;
        }
        if(!manual&&state["official"] is not null&&_now()<J.N(state,"end")&&_now()-J.N(state,"official","checkedAt")<60000)return;
        var data=await RequestAsync(id,key,id=="tavily"?"/usage":"/v2/team/credit-usage",null,token,(int)Math.Max(1,Math.Min(4000,deadline-_now())));
        long start,end;double remaining;
        try
        {
            if(id=="tavily")
            {
                var account=data["account"]!;var entry=data["key"]!;
                foreach(var pair in new[]{(account,"plan_usage"),(account,"plan_limit"),(entry,"usage"),(entry,"limit")})if(pair.Item1[pair.Item2] is null||D(pair.Item1,pair.Item2)<0||!double.IsFinite(D(pair.Item1,pair.Item2)))throw new SearchFailure("invalid");
                var month=Month(_now());start=month.Start;end=month.End;remaining=Math.Max(0,Math.Min(D(account,"plan_limit")-D(account,"plan_usage"),D(entry,"limit")-D(entry,"usage")));
            }
            else
            {
                var entry=data["data"]!;
                if(!J.B(data,"success")||entry["remainingCredits"] is null||D(entry,"remainingCredits")<0||!double.IsFinite(D(entry,"remainingCredits")))throw new SearchFailure("invalid");
                remaining=D(entry,"remainingCredits");start=J.N(state,"start");end=NextBilling(_now(),Settings()["providers"]![id]!);
                if(DateTimeOffset.TryParse(J.S(entry,"billingPeriodStart"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var officialStart)&&DateTimeOffset.TryParse(J.S(entry,"billingPeriodEnd"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var officialEnd)&&officialEnd>officialStart){start=officialStart.ToUnixTimeMilliseconds();end=officialEnd.ToUnixTimeMilliseconds();}
            }
        }
        catch(SearchFailure){throw;}catch{throw new SearchFailure("invalid");}
        lock(_sync)
        {
            if(J.B(state,"periodVerified")&&_now()>=J.N(state,"end")&&start>=J.N(state,"end")){state["used"]=0;state["requests"]=0;}
            state["start"]=start;state["end"]=end;state["periodVerified"]=true;state["official"]=new JsonObject{["remaining"]=remaining,["checkedAt"]=_now()};
            if(id=="firecrawl"){
                if(remaining<Units[id]){
                    var wasDue=J.S(state,"reason")=="quota"&&J.N(state,"disabledUntil")<=_now();
                    state["reason"]="quota";state["failureReason"]="quota";state["billingSchedule"]=BillingKey();
                    state["disabledUntil"]=wasDue?_now()+3600000:NextBilling(_now(),Settings()["providers"]![id]!);
                }else if(J.S(state,"reason") is "auth" or "paid_plan" or "quota"){state["reason"]=null;state["disabledUntil"]=0;_db.Exec("DELETE FROM cache");}
            }
            Put(id,state);
        }
    }
    private void Failure(string id,SearchFailure error)
    {
        lock(_sync){var s=State(id);s["reason"]=UsesMonthlyRetry(id)?id+"_failed":error.Code;s["failureReason"]=error.Code;if(UsesMonthlyRetry(id))s["failureOrigin"]="search";s["disabledUntil"]=UsesMonthlyRetry(id)?NextMonthlyRetry(_now()):Permanent.Contains(error.Code)?null:error.Code=="quota"?NextBilling(_now(),Settings()["providers"]!["firecrawl"]!):_now()+(error.Code=="rate_limit"?error.RetryMs:J.N(Settings(),"cooldownMs"));if(id=="firecrawl"&&error.Code=="quota"){s["billingSchedule"]=BillingKey();if(s["official"] is not null)s["official"]!["remaining"]=0;}Put(id,s);}
    }
    internal async Task RefreshUsageAsync(string id,CancellationToken token)
    {
        if(!Ids.Contains(id))throw new SearchFailure("invalid");if(id=="exa")throw new SearchFailure("balance_not_supported");await _engine.WaitAsync(token);
        try{string key;lock(_sync)key=Keys().GetValueOrDefault(id,"");if(key=="")throw new SearchFailure("missing_key");try{await RefreshAsync(id,key,_now()+10000,token,true);}catch(SearchFailure e){if(!UsesMonthlyRetry(id))Failure(id,e);throw;}}
        finally{_engine.Release();}
    }
    internal async Task<SearchResult> SearchAsync(string query,CancellationToken token)
    {
        query=query.Trim();if(query.Length is <2 or >500)throw new SearchFailure("invalid");
        JsonObject settings;lock(_sync){if(!J.B(Status(),"configured"))throw new SearchFailure("missing_key");settings=Settings();}
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter((int)J.N(settings,"timeoutMs"));await _engine.WaitAsync(limit.Token);
        try
        {
            var deadline=_now()+J.N(settings,"timeoutMs");var cacheId=J.Hash("native4:"+query.Normalize(NormalizationForm.FormKC).ToLowerInvariant());
            Dictionary<string,string> keys;int start;
            lock(_sync){var cached=_db.One("SELECT value FROM cache WHERE id=? AND expires>?",cacheId,_now());if(cached is not null)return JsonSerializer.Deserialize<SearchResult>(Convert.ToString(cached["value"])!)!;settings=Settings();keys=Keys();start=J.S(settings,"rotationMode")=="per-turn"?Math.Clamp(Convert.ToInt32(_db.One("SELECT next_index FROM rotation WHERE id=1")!["next_index"]),0,Ids.Length-1):0;}
            var order=settings["order"]!.AsArray().Select(n=>n!.GetValue<string>()).ToArray();
            for(var offset=0;offset<order.Length;offset++)
            {
                limit.Token.ThrowIfCancellationRequested();var index=(start+offset)%order.Length;var id=order[index];if(!J.B(settings,"enabled")||!J.B(settings,"providers",id,"enabled")||!keys.TryGetValue(id,out var key)||key=="")continue;
                JsonObject state;lock(_sync)state=State(id);if((!UsesMonthlyRetry(id)&&Permanent.Contains(J.S(state,"reason")))||J.N(state,"disabledUntil")>_now())continue;
                var dispatched=false;var attempted=false;
                try
                {
                    lock(_sync){if(J.S(state,"keyFingerprint")!=J.Hash(key)){state["keyFingerprint"]=J.Hash(key);state["official"]=null;Put(id,state);}}
                    await RefreshAsync(id,key,deadline,limit.Token);
                    lock(_sync)
                    {
                        state=State(id);if(id=="firecrawl"&&state["official"] is not null&&D(state,"official","remaining")<Units[id]){if(J.S(state,"reason")!="quota")Failure(id,new SearchFailure("quota"));continue;}
                        state["used"]=D(state,"used")+Units[id];state["requests"]=J.N(state,"requests")+1;if(state["official"] is not null)state["official"]!["remaining"]=Math.Max(0,D(state,"official","remaining")-Units[id]);Put(id,state);
                    }
                    JsonObject body=id switch{
                        "exa"=>new(){["query"]=query,["type"]="auto",["numResults"]=3},
                        "tavily"=>new(){["query"]=query,["search_depth"]="basic",["auto_parameters"]=false,["max_results"]=3,["include_answer"]=false,["include_raw_content"]=false,["include_images"]=false,["include_usage"]=true,["topic"]=Regex.IsMatch(query,"新聞|news",RegexOptions.IgnoreCase)?"news":"general"},
                        _=>new(){["query"]=query,["limit"]=3,["sources"]=new JsonArray("web"),["country"]="TW",["timeout"]=6000}
                    };
                    dispatched=true;attempted=true;
                    var data=await RequestAsync(id,key,id=="firecrawl"?"/v2/search":"/search",body,limit.Token,(int)Math.Max(1,Math.Min(6000,deadline-_now())));
                    dispatched=false;
                    var resultRows=id=="firecrawl"?data["data"]?["web"]:data["results"];if(resultRows is not JsonArray rows||id=="firecrawl"&&!J.B(data,"success"))throw new SearchFailure("invalid");
                    var costNode=id=="exa"?data["costDollars"]?["total"]:id=="tavily"?data["usage"]?["credits"]:data["creditsUsed"];double cost=Units[id];try{if(costNode is not null)cost=Math.Max(cost,costNode.GetValue<double>());}catch{}if(!double.IsFinite(cost))throw new SearchFailure("invalid");
                    lock(_sync){state=State(id);var extra=Math.Max(0,cost-Units[id]);state["used"]=D(state,"used")+extra;if(state["official"] is not null)state["official"]!["remaining"]=Math.Max(0,D(state,"official","remaining")-extra);state["reason"]=null;state["disabledUntil"]=0;Put(id,state);}
                    var normalized=Normalize(rows);
                    if(_reader is not null)
                    {
                        normalized=await Task.WhenAll(normalized.Select(async row=>{try{using var reading=CancellationTokenSource.CreateLinkedTokenSource(limit.Token);reading.CancelAfter(4000);var page=await _reader(row.url,reading.Token);return page is null?row:row with{body=page.body,coverage=page.coverage,retrieved_at=page.retrieved_at};}catch{return row;}}));
                    }
                    limit.Token.ThrowIfCancellationRequested();var result=new SearchResult(id,normalized);var ttl=Regex.IsMatch(query,"新聞|news",RegexOptions.IgnoreCase)?60000:Regex.IsMatch(query,"最新|今天|今日|價格|latest|today|price",RegexOptions.IgnoreCase)?120000:900000;
                    // Cursor belongs to execution state, so saving an older settings draft
                    // cannot rewind it. Cache hits and cancelled searches never advance it.
                    lock(_sync)_db.Transaction(()=>{_db.Query("INSERT INTO cache VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET value=excluded.value,expires=excluded.expires",cacheId,JsonSerializer.Serialize(result),_now()+ttl);if(J.S(settings,"rotationMode")=="per-turn")_db.Query("UPDATE rotation SET next_index=? WHERE id=1",(index+1)%order.Length);return 0;});return result;
                }
                catch(OperationCanceledException){if(UsesMonthlyRetry(id)&&dispatched&&!token.IsCancellationRequested)Failure(id,new SearchFailure("timeout"));throw;}
                catch(SearchFailure e){if(!UsesMonthlyRetry(id)||attempted)Failure(id,e);}
                catch{if(!UsesMonthlyRetry(id)||attempted)Failure(id,new SearchFailure("unavailable"));}
            }
            throw new SearchFailure("unavailable");
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new SearchFailure("timeout");}
        finally{_engine.Release();}
    }
    private SearchRow[] Normalize(JsonArray rows)
    {
        var results=new List<SearchRow>();var seen=new HashSet<string>();
        foreach(var r in rows)
        {
            if(!Uri.TryCreate(J.S(r,"url"),UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.UserInfo!=""||!seen.Add(uri.AbsoluteUri))continue;
            var body=new[]{"content","description","text","summary"}.Select(k=>J.S(r,k)).FirstOrDefault(x=>x!="")??"";
            if(body==""&&r?["highlights"] is JsonArray highlights)body=string.Join("\n",highlights.Select(x=>x?.GetValue<string>()));body=body[..Math.Min(body.Length,12000)];
            var title=J.S(r,"title");if(title=="")title=uri.Host;title=title[..Math.Min(title.Length,500)];var date=J.S(r,"publishedDate");if(date=="")date=J.S(r,"published_date");
            results.Add(new(title,uri.AbsoluteUri,body,body==""?"headline-only":"search-excerpt",DateTimeOffset.TryParse(date,out _)?date:null,DateTimeOffset.FromUnixTimeMilliseconds(_now()).ToString("O")));if(results.Count==3)break;
        }
        return results.ToArray();
    }
    public void Dispose(){lock(_sync)_db.Dispose();if(_ownsHttp)_http.Dispose();}
}
