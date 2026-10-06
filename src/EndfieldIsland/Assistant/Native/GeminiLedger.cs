using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant.Native;

internal static class J
{
    public static JsonNode? At(JsonNode? node,params string[] keys){foreach(var key in keys)node=node?[key];return node;}
    public static string S(JsonNode? n,params string[] keys)=>At(n,keys)?.GetValue<string>()??"";
    public static long N(JsonNode? n,params string[] keys){var value=At(n,keys);if(value is null)return 0;if(value.GetValueKind()!=JsonValueKind.Number||!long.TryParse(value.ToJsonString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var result))throw new FormatException("invalid_integer");return result;}
    public static bool B(JsonNode? n,params string[] keys)=>At(n,keys)?.GetValue<bool>()??false;
    public static readonly JsonSerializerOptions Json=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

internal sealed class GeminiLedger : IDisposable
{
    private readonly object _gate=new();
    private readonly LocalSqlite _db;
    private readonly Func<long> _now;
    public JsonObject Policy {get;}
    public string Model=>J.S(Policy,"defaultModel");
    public string Project=>J.S(Policy,"project");
    internal bool ProviderManagedLimits{get;}
    public GeminiLedger(string file,JsonObject policy,bool initialize=false,Func<long>? now=null,bool providerManagedLimits=false)
    {
        ProviderManagedLimits=providerManagedLimits;
        Validate(policy);Policy=(JsonObject)policy.DeepClone();_now=now??(()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(file!=":memory:"&&!File.Exists(file)&&(!initialize||File.Exists(file+".initialized")))throw new AssistantFailure("usage_database_missing");
        _db=new LocalSqlite(file);
        try
        {
            if(file==":memory:"||initialize&&!File.Exists(file+".initialized"))
            {
                _db.Exec("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT NOT NULL); INSERT OR IGNORE INTO meta VALUES('schema','1'),('last_ms','0'); CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY,project TEXT NOT NULL,model TEXT NOT NULL,ms INTEGER NOT NULL,day TEXT NOT NULL,input INTEGER NOT NULL,output INTEGER NOT NULL,prompt INTEGER,candidate INTEGER,thought INTEGER,status TEXT NOT NULL,reason TEXT); CREATE INDEX IF NOT EXISTS by_time ON events(project,model,ms); CREATE INDEX IF NOT EXISTS by_day ON events(project,day); CREATE TABLE IF NOT EXISTS locks(project TEXT NOT NULL,model TEXT NOT NULL,until_ms INTEGER NOT NULL,reason TEXT NOT NULL,PRIMARY KEY(project,model)); CREATE TABLE IF NOT EXISTS preflights(project TEXT NOT NULL,ms INTEGER NOT NULL);");
                if(file!=":memory:")File.WriteAllText(file+".initialized","1");
            }
            if(Convert.ToString(_db.One("SELECT value FROM meta WHERE key='schema'")?["value"])!="1")throw new AssistantFailure("usage_database_schema");
            _db.Query("SELECT id FROM events LIMIT 1");ReconcileTemporaryFailures();
        }
        catch{_db.Dispose();throw;}
    }
    public static string Fingerprint(JsonObject p)
    {
        var v=p["models"]?[J.S(p,"defaultModel")];
        var models=new JsonArray(((JsonObject?)p["models"]??new()).Select(x=>x.Key).Order(StringComparer.Ordinal).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
        var values=new JsonArray();
        foreach(var key in new[]{"project","defaultModel","paidAllowed","toolsAllowed","dailyRequests","dailyTotalTokens","maxInputTokens","maxOutputTokens","maxBodyBytes","preflightPerMinute"})values.Add(p[key]?.DeepClone());
        values.Add(models);
        foreach(var section in new[]{"official","local"})foreach(var key in new[]{"rpm","tpm","rpd"})values.Add(v?[section]?[key]?.DeepClone());
        values.Add(v?["outputReservation"]?.DeepClone());return J.Hash(values.ToJsonString(J.Json));
    }
    public static void Validate(JsonObject p)
    {
        try
        {
            var v=p["verification"]!;var mode=J.S(v,"mode");if(mode=="")mode="time-limited";
            var observed=DateTimeOffset.Parse(J.S(v,"observedAt"),CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
            if(mode=="project-pinned")
            {
                if(v["validUntil"] is not null||!Regex.IsMatch(J.S(v,"binding","keySha256"),"^[a-f0-9]{64}$")||J.S(v,"binding","project")!=J.S(p,"project")||J.S(v,"binding","model")!=J.S(p,"defaultModel")||J.S(v,"binding","limitsSha256")!=Fingerprint(p))throw new AssistantFailure("invalid_verification_binding");
            }
            else if(mode=="time-limited")
            {
                var expires=DateTimeOffset.Parse(J.S(v,"validUntil"),CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();if(expires<=observed||expires-observed>86400000)throw new AssistantFailure("invalid_verification_binding");
            }
            else throw new AssistantFailure("invalid_verification_binding");
            if(J.N(p,"schema")!=1||p["paidAllowed"]?.GetValue<bool>()!=false||p["toolsAllowed"]?.GetValue<bool>()!=false||J.S(v,"tier")!="free"||!Regex.IsMatch(J.S(p,"project"),"^[a-z0-9-]+$")||!Regex.IsMatch(J.S(v,"keySuffix"),"^[A-Za-z0-9_-]{4}$"))throw new AssistantFailure("invalid_policy");
            foreach(var key in new[]{"dailyRequests","dailyTotalTokens","maxInputTokens","maxOutputTokens","maxBodyBytes","preflightPerMinute"})if(J.N(p,key)<=0||J.N(p,key)>9007199254740991)throw new AssistantFailure("invalid_policy");
            if(J.N(p,"maxInputTokens")>32768||J.N(p,"maxOutputTokens")>2048||J.N(p,"maxBodyBytes")>131072||p["models"] is not JsonObject models||!models.ContainsKey(J.S(p,"defaultModel")))throw new AssistantFailure("invalid_policy");
            foreach(var (model,config) in models)
            {
                var conservative=p["providerLimitsVerified"]?.GetValue<bool>()==false;
                if(model!="gemini-3.5-flash-lite"||J.N(config,"outputReservation")<65536||J.N(config,"outputReservation")<J.N(p,"maxOutputTokens"))throw new AssistantFailure("invalid_model_policy");
                if(conservative&&(config?["official"] is not null||J.N(p,"dailyRequests")>20||J.N(config,"local","rpm")>3||J.N(config,"local","tpm")>15000||J.N(config,"local","rpd")>20))throw new AssistantFailure("invalid_model_policy");
                foreach(var key in new[]{"rpm","tpm","rpd"})if(J.N(config,"local",key)<=0||!conservative&&(J.N(config,"official",key)<=0||J.N(config,"local",key)>J.N(config,"official",key)))throw new AssistantFailure("invalid_model_policy");
            }
        }
        catch(AssistantFailure){throw;}catch{throw new AssistantFailure("invalid_policy");}
    }
    public static string Day(long ms)=>TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms),TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time")).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public static long NextReset(long ms){var day=Day(ms);var lo=ms;var hi=ms+27*3600000;while(hi-lo>1){var mid=lo+(hi-lo)/2;if(Day(mid)==day)lo=mid;else hi=mid;}return hi;}
    private T Tx<T>(Func<T> action){lock(_gate)return _db.Transaction(action);}
    private long Time(){var ms=_now();var last=Convert.ToInt64(_db.One("SELECT value FROM meta WHERE key='last_ms'")?["value"]);if(ms<last)throw new AssistantFailure("clock_rollback");_db.Query("UPDATE meta SET value=? WHERE key='last_ms'",ms.ToString(CultureInfo.InvariantCulture));return ms;}
    private void Gate(long ms)
    {
        if(!J.B(Policy,"enabled"))throw new AssistantFailure("disabled");
        var verified=Policy["verification"]!;if(!ProviderManagedLimits&&(ms<DateTimeOffset.Parse(J.S(verified,"observedAt"),CultureInfo.InvariantCulture).ToUnixTimeMilliseconds()||J.S(verified,"mode")!="project-pinned"&&ms>=DateTimeOffset.Parse(J.S(verified,"validUntil"),CultureInfo.InvariantCulture).ToUnixTimeMilliseconds()))throw new AssistantFailure("free_tier_verification_expired");
        var row=_db.One("SELECT until_ms,reason FROM locks WHERE project=? AND model=?",Project,Model);
        var reason=Convert.ToString(row?["reason"]);
        if(row is not null&&Convert.ToInt64(row["until_ms"])>ms&&(!ProviderManagedLimits||reason is "provider_daily_quota" or "provider_rate_limit" or "provider_temporary_unavailable" or "provider_auth"))throw new AssistantFailure(reason!);
    }
    private (long Requests,long Tokens,long ModelRequests,long MinuteRequests,long MinuteInput) Totals(long ms)
    {
        var day=Day(ms);return (_db.Number("SELECT count(*) FROM events WHERE project=? AND day=?",Project,day),_db.Number("SELECT coalesce(sum(input+output),0) FROM events WHERE project=? AND day=?",Project,day),_db.Number("SELECT count(*) FROM events WHERE project=? AND model=? AND day=?",Project,Model,day),_db.Number("SELECT count(*) FROM events WHERE project=? AND model=? AND ms>?",Project,Model,ms-60000),_db.Number("SELECT coalesce(sum(input),0) FROM events WHERE project=? AND model=? AND ms>?",Project,Model,ms-60000));
    }
    public void Preflight()=>Tx(()=>
    {
        var ms=Time();Gate(ms);if(ProviderManagedLimits)return 0;var t=Totals(ms);var v=Policy["models"]![Model]!;
        if(t.Requests>=J.N(Policy,"dailyRequests")||t.ModelRequests>=J.N(v,"local","rpd"))throw new AssistantFailure("daily_request_limit",429);
        if(t.MinuteRequests>=J.N(v,"local","rpm"))throw new AssistantFailure("minute_request_limit",429);
        if(t.Tokens+J.N(v,"outputReservation")>=J.N(Policy,"dailyTotalTokens"))throw new AssistantFailure("daily_token_limit",429);
        if(_db.Number("SELECT count(*) FROM preflights WHERE project=? AND ms>?",Project,ms-60000)>=J.N(Policy,"preflightPerMinute"))throw new AssistantFailure("preflight_rate_limit",429);
        _db.Query("DELETE FROM preflights WHERE ms<=?",ms-60000);_db.Query("INSERT INTO preflights VALUES(?,?)",Project,ms);return 0;
    });
    public string Reserve(long counted)=>Tx(()=>
    {
        if(counted<=0||counted>9007199254740000)throw new AssistantFailure("invalid_token_count");
        var ms=Time();Gate(ms);var input=counted+256;var t=Totals(ms);var v=Policy["models"]![Model]!;var output=J.N(v,"outputReservation");
        if(!ProviderManagedLimits){
        if(input>J.N(Policy,"maxInputTokens"))throw new AssistantFailure("input_token_limit",413);
        if(t.Requests+1>J.N(Policy,"dailyRequests")||t.ModelRequests+1>J.N(v,"local","rpd"))throw new AssistantFailure("daily_request_limit",429);
        if(t.MinuteRequests+1>J.N(v,"local","rpm"))throw new AssistantFailure("minute_request_limit",429);
        if(t.MinuteInput+input>J.N(v,"local","tpm"))throw new AssistantFailure("minute_input_token_limit",429);
        if(t.Tokens+input+output>J.N(Policy,"dailyTotalTokens"))throw new AssistantFailure("daily_token_limit",429);
        }
        var id=Guid.NewGuid().ToString();_db.Query("INSERT INTO events(id,project,model,ms,day,input,output,status) VALUES(?,?,?,?,?,?,?,?)",id,Project,Model,ms,Day(ms),input,output,"reserved");return id;
    });
    private void Lock(string reason,long until)=>_db.Query("INSERT INTO locks VALUES(?,?,?,?) ON CONFLICT(project,model) DO UPDATE SET until_ms=max(until_ms,excluded.until_ms),reason=excluded.reason",Project,Model,until,reason);
    public bool Finish(string id,JsonNode? usage)=>Tx(()=>
    {
        var e=_db.One("SELECT * FROM events WHERE id=? AND project=? AND status='reserved'",id,Project)??throw new AssistantFailure("reservation_state");var ms=Time();
        long prompt=-1,candidate=-1,thought=-1,total=-1,tool=0;
        try{prompt=J.N(usage,"promptTokenCount");candidate=J.N(usage,"candidatesTokenCount");thought=J.N(usage,"thoughtsTokenCount");total=J.N(usage,"totalTokenCount");tool=J.N(usage,"toolUsePromptTokenCount");if(usage?["promptTokenCount"] is null||usage?["totalTokenCount"] is null)prompt=-1;}catch{}
        if(prompt<0||candidate<0||thought<0||prompt>9007199254740991||candidate>9007199254740991||thought>9007199254740991||total>9007199254740991||total<prompt+candidate+thought||tool!=0){_db.Query("UPDATE events SET status='uncertain',reason='invalid_usage_metadata' WHERE id=?",id);if(!ProviderManagedLimits)Lock("unknown_usage_lock",NextReset(ms));return ProviderManagedLimits;}
        var output=Math.Max(candidate+thought,total-prompt);_db.Query("UPDATE events SET input=?,output=?,prompt=?,candidate=?,thought=?,status='finished' WHERE id=?",Math.Max(Convert.ToInt64(e["input"]),prompt),output,prompt,candidate,thought,id);
        if(!ProviderManagedLimits&&(prompt>Convert.ToInt64(e["input"])||output>Convert.ToInt64(e["output"])))Lock("reservation_exceeded_lock",NextReset(ms));return true;
    });
    public void Fail(string id,int status,long retryMs=60000,bool dailyQuota=false)=>Tx(()=>
    {
        var e=_db.One("SELECT * FROM events WHERE id=? AND project=? AND status='reserved'",id,Project);if(e is null)return 0;var ms=Time();var temporary=status is 500 or 503;
        if(status==400){_db.Query("UPDATE events SET status='rejected',reason='http_400',input=0,output=0 WHERE id=?",id);return 0;}
        _db.Query("UPDATE events SET status=?,reason=? WHERE id=?",temporary?"failed":"uncertain",status==0?"transport_unknown":"http_"+status,id);
        if(ProviderManagedLimits){if(status!=0||retryMs>0)ProviderLock(status,retryMs,dailyQuota,ms);return 0;}
        var existing=_db.One("SELECT * FROM locks WHERE project=? AND model=?",Project,Model);
        if(!temporary||existing is null||Convert.ToInt64(existing["until_ms"])<=ms||Convert.ToString(existing["reason"])=="provider_temporary_unavailable")Lock(temporary?"provider_temporary_unavailable":status==429?"provider_429_lock":"unknown_usage_lock",temporary?ms+60000:NextReset(ms));return 0;
    });
    public void PreflightFailure(int status,long retryMs=60000,bool dailyQuota=false){if(ProviderManagedLimits)Tx(()=>{ProviderLock(status,retryMs,dailyQuota,Time());return 0;});else if(status==429)Tx(()=>{var ms=Time();Lock("provider_429_lock",NextReset(ms));return 0;});}
    private void ProviderLock(int status,long retryMs,bool dailyQuota,long ms)
    {
        var reason=status==429?(dailyQuota?"provider_daily_quota":"provider_rate_limit"):status is 401 or 403?"provider_auth":"provider_temporary_unavailable";
        var until=status==429&&dailyQuota?NextReset(ms):ms+Math.Clamp(status is 401 or 403?3600000:retryMs,1000,172800000);
        _db.Query("INSERT INTO locks VALUES(?,?,?,?) ON CONFLICT(project,model) DO UPDATE SET until_ms=excluded.until_ms,reason=excluded.reason",Project,Model,until,reason);
    }
    internal void ClearAuthenticationCooldown()=>Tx(()=>{_db.Query("DELETE FROM locks WHERE project=? AND model=? AND reason='provider_auth'",Project,Model);return 0;});
    private void ReconcileTemporaryFailures()=>Tx(()=>
    {
        var ms=Time();var day=Day(ms);var rows=_db.Query("SELECT ms,model FROM events WHERE project=? AND day=? AND status='uncertain' AND reason IN ('http_500','http_503')",Project,day);
        var rejected=_db.Number("SELECT count(*) FROM events WHERE project=? AND day=? AND status='uncertain' AND reason='http_400'",Project,day);
        _db.Query("UPDATE events SET status='rejected',input=0,output=0 WHERE project=? AND day=? AND status='uncertain' AND reason='http_400'",Project,day);
        _db.Query("UPDATE events SET status='failed' WHERE project=? AND day=? AND status='uncertain' AND reason IN ('http_500','http_503')",Project,day);
        var existing=_db.One("SELECT * FROM locks WHERE project=? AND model=?",Project,Model);
        if(rejected>0&&Convert.ToString(existing?["reason"])=="unknown_usage_lock"&&_db.Number("SELECT count(*) FROM events WHERE project=? AND model=? AND day=? AND status IN('reserved','uncertain')",Project,Model,day)==0)
            _db.Query("DELETE FROM locks WHERE project=? AND model=?",Project,Model);
        if(rows.Count>0&&Convert.ToString(existing?["reason"])=="unknown_usage_lock"&&_db.Number("SELECT count(*) FROM events WHERE project=? AND model=? AND day=? AND status IN('reserved','uncertain')",Project,Model,day)==0){var until=rows.Where(x=>Convert.ToString(x["model"])==Model).Select(x=>Convert.ToInt64(x["ms"])+60000).DefaultIfEmpty(0).Max();if(until>ms)_db.Query("UPDATE locks SET until_ms=?,reason='provider_temporary_unavailable' WHERE project=? AND model=?",until,Project,Model);else _db.Query("DELETE FROM locks WHERE project=? AND model=?",Project,Model);}return 0;
    });
    public JsonObject Status(bool key)=>Tx(()=>
    {
        var ms=Time();var t=Totals(ms);string? reason=null;try{Gate(ms);}catch(AssistantFailure e){reason=e.Code;}
        var v=Policy["models"]![Model]!;var row=_db.One("SELECT until_ms FROM locks WHERE project=? AND model=?",Project,Model);
        var model=new JsonObject{["local_limits"]=ProviderManagedLimits?null:v["local"]!.DeepClone(),["used_requests_today"]=t.ModelRequests,["used_requests_last_minute"]=t.MinuteRequests,["remaining_requests_today"]=ProviderManagedLimits?null:JsonValue.Create(Math.Max(0,J.N(v,"local","rpd")-t.ModelRequests)),["locked_reason"]=reason,["retry_at"]=reason is not null?Convert.ToInt64(row?["until_ms"]):0};
        return new JsonObject{["key_present"]=key,["provider_managed_limits"]=ProviderManagedLimits,["daily_local_request_limit"]=ProviderManagedLimits?null:JsonValue.Create(J.N(Policy,"dailyRequests")),["accounted_tokens_today"]=t.Tokens,["next_daily_reset"]=DateTimeOffset.FromUnixTimeMilliseconds(NextReset(ms)).ToString("O"),["models"]=new JsonObject{[Model]=model}};
    });
    public void Dispose(){lock(_gate)_db.Dispose();}
}
