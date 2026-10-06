using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant.Native;

internal sealed class NativeConfiguration
{
    internal static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"ChatGPT","GeminiHub");
    internal string Root{get;}
    internal string LedgerPath=>Path.Combine(Root,"data","usage.sqlite");
    internal string SearchPath=>Path.Combine(Root,"data","search");
    private string PolicyPath=>Path.Combine(Root,"policy.json");
    private string KeyPath=>Path.Combine(Root,"data","gemini-key.dpapi");
    public NativeConfiguration(string? root=null)=>Root=root??DefaultRoot;
    internal JsonObject Policy()=>JsonNode.Parse(File.ReadAllText(PolicyPath))!.AsObject();
    internal static JsonObject InitialPolicy()=>JsonNode.Parse("""{"schema":1,"enabled":false,"project":"local-free-project","defaultModel":"gemini-3.5-flash-lite","paidAllowed":false,"toolsAllowed":false,"providerLimitsVerified":false,"verification":{"tier":"free","mode":"time-limited","keySuffix":"none","observedAt":"2026-01-01T00:00:00Z","validUntil":"2026-01-02T00:00:00Z","source":"Not confirmed; disabled initial setup"},"dailyRequests":20,"dailyTotalTokens":4000000,"maxInputTokens":32768,"maxOutputTokens":2048,"maxBodyBytes":131072,"preflightPerMinute":6,"models":{"gemini-3.5-flash-lite":{"official":null,"local":{"rpm":3,"tpm":15000,"rpd":20},"outputReservation":65536}}}""")!.AsObject();
    internal void Initialize(Func<long>? now=null)
    {
        Directory.CreateDirectory(Root);Directory.CreateDirectory(Path.Combine(Root,"data"));
        using var write=new FileStream(Path.Combine(Root,"data","native-bootstrap.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var fresh=!File.Exists(PolicyPath);
        if(fresh&&(File.Exists(LedgerPath)||File.Exists(LedgerPath+".initialized")||File.Exists(KeyPath)||File.Exists(Path.Combine(Root,"data","hub.token"))))throw new AssistantFailure("existing_configuration_incomplete");
        if(fresh)File.WriteAllText(PolicyPath,InitialPolicy().ToJsonString(J.Json),new UTF8Encoding(false));
        if(!fresh&&!File.Exists(LedgerPath))throw new AssistantFailure("usage_database_missing");
        using var ledger=new GeminiLedger(LedgerPath,Policy(),fresh,now);
    }
    internal string LoadKey()
    {
        if(!File.Exists(KeyPath))return "";var encrypted=File.ReadAllBytes(KeyPath);byte[]? plain=null;
        try{plain=ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser);return Encoding.UTF8.GetString(plain).Trim();}
        finally{CryptographicOperations.ZeroMemory(encrypted);if(plain is not null)CryptographicOperations.ZeroMemory(plain);}
    }
    internal static bool KeyValid(string key,JsonObject policy)
    {
        if(!Regex.IsMatch(key,@"^(?:AIza[A-Za-z0-9_-]{30,}|AQ\.[A-Za-z0-9_-]{30,})$")||!key.EndsWith(J.S(policy,"verification","keySuffix"),StringComparison.OrdinalIgnoreCase))return false;
        if(J.S(policy,"verification","mode")!="project-pinned")return true;
        try{return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),Convert.FromHexString(J.S(policy,"verification","binding","keySha256")));}catch{return false;}
    }
    internal static JsonObject Confirm(JsonObject existing,string key,bool confirmed,DateTimeOffset? time=null)
    {
        if(!confirmed||key.Length>160||!Regex.IsMatch(key,@"^(?:AIza[A-Za-z0-9_-]{30,}|AQ\.[A-Za-z0-9_-]{30,})$"))throw new AssistantFailure("free_confirmation_required");
        GeminiLedger.Validate(existing);var p=(JsonObject)existing.DeepClone();var hash=J.Hash(key);
        var same=J.S(p,"verification","mode")=="project-pinned"&&J.S(p,"verification","binding","keySha256")==hash;
        if(!same){var defaults=InitialPolicy();p["providerLimitsVerified"]=false;p["dailyRequests"]=defaults["dailyRequests"]!.DeepClone();p["models"]=defaults["models"]!.DeepClone();p["preflightPerMinute"]=defaults["preflightPerMinute"]!.DeepClone();}
        p["enabled"]=true;p["verification"]=new JsonObject{["tier"]="free",["mode"]="project-pinned",["keySuffix"]=key[^4..],["observedAt"]=(time??DateTimeOffset.UtcNow).ToString("O"),["validUntil"]=null,["source"]="User explicit Free tier confirmation; not live cloud billing verification",["binding"]=new JsonObject{["project"]=J.S(p,"project"),["model"]=J.S(p,"defaultModel"),["keySha256"]=hash,["limitsSha256"]=""}};
        p["verification"]!["binding"]!["limitsSha256"]=GeminiLedger.Fingerprint(p);GeminiLedger.Validate(p);return p;
    }
    internal void Configure(string key,bool confirmed,Func<long>? now=null)
    {
        Initialize(now);using var write=new FileStream(Path.Combine(Root,"data","key-write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        key=key.Trim();var policy=Confirm(Policy(),key,confirmed);var policyOld=File.ReadAllText(PolicyPath);var keyOld=File.Exists(KeyPath)?File.ReadAllBytes(KeyPath):null;
        var bytes=Encoding.UTF8.GetBytes(key);byte[] encrypted;
        try{encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(bytes);}
        var suffix=".pending-"+Guid.NewGuid().ToString("N");var keyWritten=false;
        try
        {
            File.WriteAllBytes(KeyPath+suffix,encrypted);File.WriteAllText(PolicyPath+suffix,policy.ToJsonString(J.Json),new UTF8Encoding(false));
            File.Move(KeyPath+suffix,KeyPath,true);keyWritten=true;File.Move(PolicyPath+suffix,PolicyPath,true);
        }
        catch{if(keyWritten){if(keyOld is not null)File.WriteAllBytes(KeyPath,keyOld);else File.Delete(KeyPath);}File.WriteAllText(PolicyPath,policyOld,new UTF8Encoding(false));throw new AssistantFailure("local_configuration_failed");}
        finally{CryptographicOperations.ZeroMemory(encrypted);if(keyOld is not null)CryptographicOperations.ZeroMemory(keyOld);foreach(var file in new[]{KeyPath+suffix,PolicyPath+suffix})if(File.Exists(file))File.Delete(file);}
    }
}
