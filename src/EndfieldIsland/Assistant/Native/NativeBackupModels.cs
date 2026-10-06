using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant.Native;

internal sealed record BackupModelInfo(string Id,string Model,bool Configured,string Reason,long RetryAt,long Requests,long Tokens);
internal sealed record BackupCredentials(string Account="",string Cloudflare="",string Groq="",bool Enabled=true,bool FreeConfirmed=false);
internal sealed record BackupModelState(long RetryAt=0,string Reason="",long Requests=0,long Tokens=0);

/// <summary>Direct text APIs, independent from Gemini's ledger and all search provider state.</summary>
internal sealed class NativeBackupModels
{
    internal const string CloudflareModel="@cf/qwen/qwen3.8-27b";
    internal const string GroqModel="openai/gpt-oss-120b";
    private readonly string _keysPath,_statePath;
    private readonly HttpClient _http;
    private readonly Func<long> _now;
    private readonly object _gate=new();
    private BackupCredentials _keys=new();
    private Dictionary<string,BackupModelState> _states=new(){["cloudflare"]=new(),["groq"]=new()};
    private string? _storageError;
    internal string LastModel{get;private set;}="";
    internal NativeBackupModels(string root,HttpClient http,Func<long>? now=null)
    {
        _keysPath=Path.Combine(root,"data","assistant-backup-keys.dpapi");_statePath=Path.Combine(root,"data","assistant-backup-state.json");
        _http=http;_now=now??(()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try {
            if(File.Exists(_keysPath)){
                if(new FileInfo(_keysPath).Length>16384)throw new InvalidDataException();
                var plain=ProtectedData.Unprotect(File.ReadAllBytes(_keysPath),Encoding.UTF8.GetBytes("Endfield.Assistant.Backups.v1"),DataProtectionScope.CurrentUser);
                try{_keys=JsonSerializer.Deserialize<BackupCredentials>(plain)??throw new InvalidDataException();}finally{CryptographicOperations.ZeroMemory(plain);}
                ValidateKeys(_keys);
            }
            if(File.Exists(_statePath)){
                if(new FileInfo(_statePath).Length>16384)throw new InvalidDataException();
                var loaded=JsonSerializer.Deserialize<Dictionary<string,BackupModelState>>(File.ReadAllText(_statePath))??throw new InvalidDataException();
                foreach(var id in _states.Keys.ToArray())if(loaded.TryGetValue(id,out var state)){
                    if(state.RetryAt<0||state.RetryAt>4102444800000||state.Requests is <0 or >1000000000000||state.Tokens is <0 or >9007199254740000)throw new InvalidDataException();_states[id]=state;
                }
            }
        }catch{_storageError="backup_storage_error";_keys=new();_states=new(){["groq"]=new(),["cloudflare"]=new()};}
    }
    internal bool HasConfigured {get{lock(_gate)return _storageError is null&&_keys.Enabled&&_keys.FreeConfirmed&&(_keys.Cloudflare.Length>0||_keys.Groq.Length>0);}}
    internal bool HasAvailable=>Status().Any(p=>p.Reason=="ready");
    internal (bool Enabled,bool FreeConfirmed) Preferences{get{lock(_gate)return(_keys.Enabled,_keys.FreeConfirmed);}}
    private bool KeyPresent(string id)=>_keys.Enabled&&_keys.FreeConfirmed&&(id=="cloudflare"?_keys.Account.Length>0&&_keys.Cloudflare.Length>0:_keys.Groq.Length>0);
    internal BackupModelInfo[] Status()
    {
        lock(_gate)return new[]{"groq","cloudflare"}.Select(id=>{
            var state=_states[id];var configured=id=="cloudflare"?_keys.Cloudflare.Length>0&&_keys.Account.Length>0:_keys.Groq.Length>0;
            var reason=_storageError??(!configured?"not_configured":!_keys.Enabled?"disabled":!_keys.FreeConfirmed?"free_unconfirmed":state.Reason=="auth"?"auth":state.RetryAt>_now()?state.Reason:"ready");
            return new BackupModelInfo(id,id=="cloudflare"?CloudflareModel:GroqModel,configured,reason,state.RetryAt,state.Requests,state.Tokens);
        }).ToArray();
    }
    private static void ValidateKeys(BackupCredentials keys)
    {
        if(keys.Account.Length>0&&!Regex.IsMatch(keys.Account,"^[a-fA-F0-9]{32}$"))throw new AssistantFailure("backup_account_invalid");
        foreach(var key in new[]{keys.Cloudflare,keys.Groq})if(key.Length>0&&(key.Length is <20 or >512||key.Any(char.IsWhiteSpace)||key.Any(char.IsControl)))throw new AssistantFailure("backup_key_invalid");
        if(keys.Cloudflare.Length>0&&keys.Account.Length==0)throw new AssistantFailure("backup_account_invalid");
        if(keys.Groq.Length>0&&!keys.Groq.StartsWith("gsk_",StringComparison.Ordinal))throw new AssistantFailure("backup_key_invalid");
    }
    internal void Configure(string account,string cloudflare,string groq,bool enabled,bool freeConfirmed,bool clearCloudflare=false,bool clearGroq=false)
    {
        lock(_gate){
            if(_storageError is not null)throw new AssistantFailure(_storageError);
            var next=new BackupCredentials(clearCloudflare?"":string.IsNullOrWhiteSpace(account)?_keys.Account:account.Trim(),
                clearCloudflare?"":string.IsNullOrWhiteSpace(cloudflare)?_keys.Cloudflare:cloudflare.Trim(),
                clearGroq?"":string.IsNullOrWhiteSpace(groq)?_keys.Groq:groq.Trim(),enabled,freeConfirmed);
            ValidateKeys(next);if(enabled&&(next.Cloudflare.Length>0||next.Groq.Length>0)&&!freeConfirmed)throw new AssistantFailure("backup_free_confirmation_required");
            var states=_states.ToDictionary(p=>p.Key,p=>p.Value);
            if(next.Account!=_keys.Account||next.Cloudflare!=_keys.Cloudflare)states["cloudflare"]=states["cloudflare"] with{Reason="",RetryAt=0};
            if(next.Groq!=_keys.Groq)states["groq"]=states["groq"] with{Reason="",RetryAt=0};
            Directory.CreateDirectory(Path.GetDirectoryName(_keysPath)!);
            var suffix="."+Guid.NewGuid().ToString("N")+".tmp";var plain=JsonSerializer.SerializeToUtf8Bytes(next);
            var previous=File.Exists(_keysPath)?File.ReadAllBytes(_keysPath):null;var committed=false;
            try{
                File.WriteAllBytes(_keysPath+suffix,ProtectedData.Protect(plain,Encoding.UTF8.GetBytes("Endfield.Assistant.Backups.v1"),DataProtectionScope.CurrentUser));
                File.WriteAllBytes(_statePath+suffix,JsonSerializer.SerializeToUtf8Bytes(states));
                File.Move(_keysPath+suffix,_keysPath,true);committed=true;File.Move(_statePath+suffix,_statePath,true);
                _keys=next;_states=states;
            }catch{
                if(committed){if(previous is not null)WriteAtomic(_keysPath,previous);else File.Delete(_keysPath);}
                throw;
            }finally{
                CryptographicOperations.ZeroMemory(plain);if(previous is not null)CryptographicOperations.ZeroMemory(previous);
                foreach(var temporary in new[]{_keysPath+suffix,_statePath+suffix})if(File.Exists(temporary))File.Delete(temporary);
            }
        }
    }
    private static void WriteAtomic(string path,byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,data);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private void SaveState()=>WriteAtomic(_statePath,JsonSerializer.SerializeToUtf8Bytes(_states));
    private void Pause(string id,string reason,long until)
    {
        lock(_gate){_states[id]=_states[id] with{Reason=reason,RetryAt=until};SaveState();}
    }
    internal async Task ProbeAsync(string id,CancellationToken token)
    {
        if(id is not ("groq" or "cloudflare"))throw new AssistantFailure("backup_key_invalid");
        BackupCredentials keys;lock(_gate){if(!KeyPresent(id)||_storageError is not null)throw new AssistantFailure("backup_models_unavailable");keys=_keys;}
        try{await CallAsync(id,keys,[new("user","只輸出 {\"answer\":\"OK\"}，不搜尋、不建立記憶。")],"這是連線測試，只回傳 JSON。",JsonNode.Parse("""{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}""")!.AsObject(),token);}
        catch(BackupHttpFailure error){Pause(id,error.Reason,error.Until);throw new AssistantFailure("backup_models_unavailable");}
    }
    internal async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,string instructions,JsonObject? schema,CancellationToken token,bool fastReply=false)
    {
        foreach(var id in new[]{"groq","cloudflare"}){
            token.ThrowIfCancellationRequested();BackupCredentials keys;
            lock(_gate){if(_storageError is not null)throw new AssistantFailure(_storageError);if(!KeyPresent(id)||_states[id].Reason=="auth"||_states[id].RetryAt>_now())continue;keys=_keys;}
            try{
                var result=await CallAsync(id,keys,messages,instructions,schema,token,fastReply);
                LastModel=id=="cloudflare"?CloudflareModel:GroqModel;return result;
            }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
            catch(BackupHttpFailure failure){Pause(id,failure.Reason,failure.Until);}
            catch(HttpRequestException){Pause(id,"temporary",_now()+60000);}
            catch(OperationCanceledException){Pause(id,"temporary",_now()+60000);}
            catch(JsonException){Pause(id,"invalid_response",_now()+60000);}
            catch(FormatException){Pause(id,"invalid_response",_now()+60000);}
            catch(AssistantFailure failure)when(failure.Code=="invalid_backup_response"){Pause(id,"invalid_response",_now()+60000);}
            catch(InvalidOperationException){Pause(id,"invalid_response",_now()+60000);}
        }
        throw new AssistantFailure("backup_models_unavailable");
    }
    private async Task<string> CallAsync(string id,BackupCredentials keys,IReadOnlyList<ChatMessage> messages,string instructions,JsonObject? schema,CancellationToken token,bool fastReply=false)
    {
        var system=instructions;
        if(id=="cloudflare"&&schema?["properties"]?["action"] is not null)system+="\n"+DialogueInstructions.CombinedReply+"\n"+NativePrompts.VoiceExamples;
        if(schema is not null&&id=="groq")system+="\n只輸出 response_format 指定的 JSON，不加 markdown、思考過程或額外文字。";
        var prompt=new JsonArray(new JsonObject{["role"]="system",["content"]=system});
        foreach(var message in messages)prompt.Add(new JsonObject{["role"]=message.role=="model"?"assistant":"user",["content"]=message.text});
        var body=new JsonObject{["messages"]=prompt,["stream"]=false};
        if(id=="groq"){
            body["model"]=GroqModel;body["max_completion_tokens"]=2048;body["reasoning_effort"]="low";
            if(schema is not null)body["response_format"]=new JsonObject{["type"]="json_schema",["json_schema"]=new JsonObject{["name"]="assistant_response",["strict"]=true,["schema"]=schema.DeepClone()}};
        }else{
            body["max_completion_tokens"]=fastReply?2048:4096;body["temperature"]=.7;body["top_p"]=.8;body["store"]=false;
            body["chat_template_kwargs"]=new JsonObject{["enable_thinking"]=false};
            if(schema is not null)body["response_format"]=new JsonObject{["type"]="json_schema",["json_schema"]=new JsonObject{["name"]="assistant_response",["strict"]=true,["schema"]=schema.DeepClone()}};
        }
        var url=id=="groq"?"https://api.groq.com/openai/v1/chat/completions":"https://api.cloudflare.com/client/v4/accounts/"+keys.Account+"/ai/run/"+CloudflareModel;
        using var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",id=="groq"?keys.Groq:keys.Cloudflare);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(id=="cloudflare"?30:12));
        lock(_gate){_states[id]=_states[id] with{Requests=_states[id].Requests+1};SaveState();}
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(response.Content.Headers.ContentLength>1048576)throw new AssistantFailure("invalid_backup_response");
        using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();var bytes=new byte[8192];
        int read;while((read=await stream.ReadAsync(bytes,timeout.Token))>0){if(buffer.Length+read>1048576)throw new AssistantFailure("invalid_backup_response");buffer.Write(bytes,0,read);}
        JsonNode? data;try{data=JsonNode.Parse(buffer.ToArray());}catch(JsonException){if(response.IsSuccessStatusCode)throw;data=null;}
        if(!response.IsSuccessStatusCode||data?["success"]?.GetValue<bool>()==false)throw Failure(id,response,data);
        var result=id=="cloudflare"?data?["result"]:data;
        var content=result?["response"]??result?["choices"]?[0]?["message"]?["content"];
        var raw=content is JsonValue value&&value.TryGetValue<string>(out var text)?text:content is JsonObject obj?obj.ToJsonString():"";
        raw=Regex.Replace(raw,@"^\s*<think>[\s\S]*?</think>\s*","",RegexOptions.CultureInvariant).Trim();
        raw=Regex.Replace(raw,@"^```(?:json)?\s*|\s*```$","").Trim();
        if(raw.Length==0||raw.Length>64000)throw new AssistantFailure("invalid_backup_response");
        if(schema is not null&&!SchemaMatches(JsonNode.Parse(raw),schema))throw new AssistantFailure("invalid_backup_response");
        var total=J.N(result,"usage","total_tokens");
        if(total<=0)total=J.N(result,"usage","prompt_tokens")+J.N(result,"usage","completion_tokens");
        lock(_gate){_states[id]=_states[id] with{Reason="",RetryAt=0,Tokens=_states[id].Tokens+Math.Clamp(total,0,1000000)};SaveState();}
        return raw;
    }
    internal static bool SchemaMatches(JsonNode? node,JsonObject schema)
    {
        var type=J.S(schema,"type");
        if(type=="object"){
            if(node is not JsonObject obj)return false;
            var properties=schema["properties"] as JsonObject??new();
            if(schema["required"] is JsonArray required&&required.Any(key=>!obj.ContainsKey(key!.GetValue<string>())))return false;
            if(schema["additionalProperties"]?.GetValue<bool>()==false&&obj.Any(pair=>!properties.ContainsKey(pair.Key)))return false;
            return obj.All(pair=>properties[pair.Key] is not JsonObject rule||SchemaMatches(pair.Value,rule));
        }
        if(type=="array")return node is JsonArray array&&(schema["maxItems"] is null||array.Count<=J.N(schema,"maxItems"))&&array.All(item=>schema["items"] is not JsonObject rule||SchemaMatches(item,rule));
        if(type=="string")return node is JsonValue value&&value.TryGetValue<string>(out var str)&&(schema["enum"] is not JsonArray options||options.Any(option=>option?.GetValue<string>()==str));
        if(type=="boolean")return node is JsonValue boolean&&boolean.TryGetValue<bool>(out _);
        return false;
    }
    private BackupHttpFailure Failure(string id,HttpResponseMessage response,JsonNode? data)
    {
        var status=(int)response.StatusCode;var now=_now();
        if(status is 401 or 403)return new("auth",0);
        if(id=="cloudflare"&&J.N((data?["errors"] as JsonArray)?.FirstOrDefault(),"code")==3036)return new("daily_quota",DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime.Date.AddDays(1).Subtract(DateTime.UnixEpoch).Ticks/TimeSpan.TicksPerMillisecond);
        var retry=response.Headers.RetryAfter?.Delta?.TotalMilliseconds??(response.Headers.RetryAfter?.Date?.ToUnixTimeMilliseconds()-now)??60000;
        if(id=="groq"&&response.Headers.TryGetValues("x-ratelimit-remaining-requests",out var remaining)&&remaining.FirstOrDefault()=="0"&&response.Headers.TryGetValues("x-ratelimit-reset-requests",out var reset))retry=Math.Max(retry,ResetDelay(reset.FirstOrDefault()));
        if(id=="groq"&&response.Headers.TryGetValues("x-ratelimit-remaining-tokens",out var tokens)&&tokens.FirstOrDefault()=="0"&&response.Headers.TryGetValues("x-ratelimit-reset-tokens",out var tokenReset))retry=Math.Max(retry,ResetDelay(tokenReset.FirstOrDefault()));
        if(id=="groq"&&status==429){var delay=Regex.Match(J.S(data?["error"],"message"),@"(?:try again in|retry in)\s+((?:\d+(?:\.\d+)?(?:ms|d|h|m|s)\s*)+)",RegexOptions.IgnoreCase);if(delay.Success)retry=Math.Max(retry,ResetDelay(delay.Groups[1].Value));}
        return new(status==429?"rate_limit":"temporary",now+(long)Math.Clamp(retry,1000,172800000));
    }
    internal static double ResetDelay(string? text)
    {
        double seconds=0;
        foreach(Match match in Regex.Matches(text??"",@"(\d+(?:\.\d+)?)(ms|d|h|m|s)")){
            var count=double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
            seconds+=count*(match.Groups[2].Value switch{"d"=>86400,"h"=>3600,"m"=>60,"ms"=>.001,_=>1});
        }
        return seconds*1000;
    }
    private sealed class BackupHttpFailure(string reason,long until):Exception
    {internal string Reason{get;}=reason;internal long Until{get;}=until;}
}
