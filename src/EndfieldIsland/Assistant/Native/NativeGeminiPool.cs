using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EndfieldChargePlus.Assistant.Native;

internal sealed record GeminiSlotInfo(int Slot,bool Configured,string Reason,long RetryAt,long Requests,long Tokens);

/// <summary>The existing primary retains its policy and ledger; four extra accounts have independent ledgers.</summary>
internal sealed class NativeGeminiPool:IDisposable
{
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("Endfield.GeminiPool.v1");
    internal sealed record Entry(int Slot,NativeGemini Client,GeminiLedger Ledger);
    private readonly string _root,_path;
    private readonly HttpClient _http;
    private readonly Func<long>? _now;
    private readonly Func<NativeGemini> _primary;
    private readonly Func<GeminiLedger> _primaryLedger;
    private readonly Func<string> _primaryKey;
    private readonly object _gate=new();
    private List<Entry> _entries=[];
    private string[] _keys=["","","",""];
    private byte[]? _disk;
    internal bool StorageError{get;private set;}
    internal bool HasExtras{get{lock(_gate)return _entries.Count>0;}}
    internal bool HasConfigured=>_primary().HasKey||HasExtras;
    internal NativeGeminiPool(string root,HttpClient http,Func<NativeGemini> primary,Func<GeminiLedger> ledger,Func<string> primaryKey,Func<long>? now=null)
    {
        _root=root;_path=Path.Combine(root,"data","gemini-pool-keys.dpapi");_http=http;_primary=primary;_primaryLedger=ledger;_primaryKey=primaryKey;_now=now;
        if(!File.Exists(_path))return;
        try{
            if(new FileInfo(_path).Length>16384)throw new InvalidDataException();_disk=File.ReadAllBytes(_path);
            var plain=ProtectedData.Unprotect(_disk,Entropy,DataProtectionScope.CurrentUser);
            try{_keys=JsonSerializer.Deserialize<string[]>(plain)??throw new InvalidDataException();Validate(_keys);}
            finally{CryptographicOperations.ZeroMemory(plain);}
            _entries=Open(_keys);
        }catch{StorageError=true;foreach(var e in _entries)e.Ledger.Dispose();_entries=[];_keys=["","","",""];}
    }
    private static void Validate(string[] keys)
    {
        if(keys.Length!=4)throw new AssistantFailure("invalid_gemini_pool");
        foreach(var key in keys)if(key is null||key.Length>160||key.Length>0&&!NativeConfiguration.KeyValid(key,NativeConfiguration.Confirm(NativeConfiguration.InitialPolicy(),key,true)))throw new AssistantFailure("invalid_gemini_pool");
        if(keys.Where(k=>k.Length>0).Distinct().Count()!=keys.Count(k=>k.Length>0))throw new AssistantFailure("duplicate_gemini_key");
    }
    private List<Entry> Open(string[] keys)
    {
        var list=new List<Entry>();
        try{
            for(var i=0;i<4;i++)if(keys[i].Length>0){
                var path=Path.Combine(_root,"data","gemini-accounts",J.Hash(keys[i])+".sqlite");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var policy=NativeConfiguration.Confirm(NativeConfiguration.InitialPolicy(),keys[i],true);
                var ledger=new GeminiLedger(path,policy,initialize:true,now:_now,providerManagedLimits:true);list.Add(new(i+2,new(ledger,keys[i],_http),ledger));
            }
            return list;
        }catch{foreach(var entry in list)entry.Ledger.Dispose();throw;}
    }
    internal void Configure(IReadOnlyList<string> inputs,IReadOnlySet<int> clear,bool freeConfirmed)
    {
        if(inputs.Count!=4||!freeConfirmed)throw new AssistantFailure("free_confirmation_required");
        lock(_gate){
            if(StorageError)throw new AssistantFailure("gemini_pool_storage_error");
            var keys=Enumerable.Range(0,4).Select(i=>clear.Contains(i+2)?"":string.IsNullOrWhiteSpace(inputs[i])?_keys[i]:inputs[i].Trim()).ToArray();Validate(keys);
            if(keys.Any(k=>k.Length>0&&k==_primaryKey()))throw new AssistantFailure("duplicate_gemini_key");
            var replacement=Open(keys);var pending=_path+".pending-"+Guid.NewGuid().ToString("N");var committed=false;
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var writeLock=new FileStream(_path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                if(File.Exists(_path)?_disk is null||!File.ReadAllBytes(_path).SequenceEqual(_disk):_disk is not null)throw new AssistantFailure("gemini_pool_storage_error");
                var plain=JsonSerializer.SerializeToUtf8Bytes(keys);byte[] encrypted;
                try{encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(plain);}
                using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encrypted);stream.Flush(true);}
                File.Move(pending,_path,true);_disk=encrypted;_keys=keys;
                var old=_entries;_entries=replacement;committed=true;foreach(var entry in old)entry.Ledger.Dispose();
            }finally{if(!committed)foreach(var entry in replacement)entry.Ledger.Dispose();if(File.Exists(pending))File.Delete(pending);}
        }
    }
    internal bool ContainsExtraKey(string key){lock(_gate)return _keys.Contains(key);}
    private List<Entry> Entries()=>new[]{new Entry(1,_primary(),_primaryLedger())}.Concat(_entries).ToList();
    internal IReadOnlyList<GeminiSlotInfo> Status()
    {
        lock(_gate){
            var entries=Entries();return Enumerable.Range(1,5).Select(slot=>{
                var entry=entries.FirstOrDefault(e=>e.Slot==slot);
                if(entry is null)return new GeminiSlotInfo(slot,false,StorageError?"storage_error":"not_configured",0,0,0);
                var configured=entry.Client.HasKey;var status=entry.Ledger.Status(configured);var model=status["models"]![entry.Ledger.Model]!;
                return new GeminiSlotInfo(slot,configured,!configured?"not_configured":J.S(model,"locked_reason") is {Length:>0} reason?reason:"ready",J.N(model,"retry_at"),J.N(model,"used_requests_today"),J.N(status,"accounted_tokens_today"));
            }).ToArray();
        }
    }
    internal Turn BeginTurn()
    {
        lock(_gate)return new(Entries().Where(e=>e.Client.HasKey).OrderBy(e=>e.Slot).ToList());
    }
    internal sealed class Turn
    {
        private readonly List<Entry> _order;
        private readonly HashSet<int> _failed=[];
        private Entry? _chosen;
        internal Turn(List<Entry> entries){_order=entries;}
        internal string Model=>_chosen?.Ledger.Model??"";
        internal async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,string instructions,JsonObject? schema,ImageInput? image,CancellationToken token,bool validate,bool backupAvailable)
        {
            AssistantFailure last=new("api_key_not_configured_or_mismatch");
            var order=_chosen is null?_order:new[]{_chosen}.Concat(_order.Where(e=>e.Slot!=_chosen.Slot)).ToList();
            foreach(var entry in order){
                token.ThrowIfCancellationRequested();if(_failed.Contains(entry.Slot))continue;
                if(J.S(entry.Ledger.Status(true),"models",entry.Ledger.Model,"locked_reason") is {Length:>0} locked){last=new(locked);_failed.Add(entry.Slot);continue;}
                try{
                    using var attempt=CancellationTokenSource.CreateLinkedTokenSource(token);
                    if(_order.Count>1)attempt.CancelAfter(TimeSpan.FromSeconds(image is null?10:15));else if(backupAvailable)attempt.CancelAfter(TimeSpan.FromSeconds(16));
                    var result=await entry.Client.GenerateAsync(messages,instructions,schema,image,attempt.Token);
                    if(validate&&schema is not null&&!NativeBackupModels.SchemaMatches(JsonNode.Parse(result),schema))throw new AssistantFailure("invalid_assistant_response");
                    _chosen=entry;return result;
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(AssistantFailure error)when(NativeAssistantService.CanUseBackup(error.Code)||error.Code.StartsWith("provider_http_",StringComparison.Ordinal)){
                    last=error;if(J.S(entry.Ledger.Status(true),"models",entry.Ledger.Model,"locked_reason").Length==0)entry.Ledger.PreflightFailure(503);
                }
                catch(Exception error)when(error is HttpRequestException or OperationCanceledException or JsonException){last=new("provider_temporary_unavailable");entry.Ledger.PreflightFailure(503);}
                _failed.Add(entry.Slot);
            }
            throw last;
        }

    }
    public void Dispose(){lock(_gate){foreach(var e in _entries)e.Ledger.Dispose();}}
}
