using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndfieldChargePlus.Assistant.Native;

internal sealed record AssistantPersona(string Id,string Name,string Character,string Rules);

/// <summary>One encrypted local library. Immutable snapshots keep an in-flight reply consistent.</summary>
internal sealed class AssistantPersonaStore
{
    internal const string DefaultId="default";
    internal const int MaxProfiles=30,MaxField=6000,MaxCombined=10000;
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("EndfieldIsland.Personas.v1");
    private sealed record Library(int Schema,string ActiveId,List<AssistantPersona> Profiles);
    private readonly object _sync=new();
    private readonly string _path;
    private Library _library=new(1,DefaultId,[]);
    private byte[]? _disk;
    internal bool StorageError{get;private set;}
    internal event Action? Changed;
    internal AssistantPersonaStore(string path)
    {
        _path=path;
        if(!File.Exists(path))return;
        try{
            if(new FileInfo(path).Length>1048576)throw new InvalidDataException();
            _disk=File.ReadAllBytes(path);
            var bytes=ProtectedData.Unprotect(_disk,Entropy,DataProtectionScope.CurrentUser);
            try{var loaded=JsonSerializer.Deserialize<Library>(bytes)??throw new InvalidDataException();Validate(loaded);_library=loaded;}
            finally{CryptographicOperations.ZeroMemory(bytes);}
        }catch{StorageError=true;}
    }
    private static AssistantPersona Default=>new(DefaultId,"","","");
    internal IReadOnlyList<AssistantPersona> Profiles{get{lock(_sync)return new[]{Default}.Concat(_library.Profiles).ToArray();}}
    internal AssistantPersona Active{get{lock(_sync)return _library.Profiles.FirstOrDefault(p=>p.Id==_library.ActiveId)??Default;}}
    internal AssistantPersona Save(string? id,string name,string character,string rules)
    {
        var profile=new AssistantPersona(id??Guid.NewGuid().ToString("N"),name.Trim(),character.Trim(),rules.Trim());
        if(profile.Id==DefaultId)throw new InvalidOperationException("persona_default");
        ValidateProfile(profile);
        lock(_sync){
            var next=_library.Profiles.ToList();var index=next.FindIndex(p=>p.Id==profile.Id);
            if(index<0)next.Add(profile);else next[index]=profile;
            Commit(_library with{Profiles=next});
        }
        Changed?.Invoke();return profile;
    }
    internal void Activate(string id)
    {
        lock(_sync){if(id!=DefaultId&&!_library.Profiles.Any(p=>p.Id==id))throw new InvalidOperationException("persona_missing");Commit(_library with{ActiveId=id});}
        Changed?.Invoke();
    }
    internal void Delete(string id)
    {
        if(id==DefaultId)throw new InvalidOperationException("persona_default");
        lock(_sync){if(!_library.Profiles.Any(p=>p.Id==id))throw new InvalidOperationException("persona_missing");Commit(_library with{Profiles=_library.Profiles.Where(p=>p.Id!=id).ToList(),ActiveId=_library.ActiveId==id?DefaultId:_library.ActiveId});}
        Changed?.Invoke();
    }
    private static void ValidateProfile(AssistantPersona p)
    {
        if(p.Id is null||!Guid.TryParseExact(p.Id,"N",out _)||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>60||p.Name.Any(char.IsControl)||
            p.Character is null||p.Rules is null||p.Character.Length>MaxField||p.Rules.Length>MaxField||p.Character.Length+p.Rules.Length>MaxCombined)
            throw new InvalidOperationException("persona_invalid");
    }
    private static void Validate(Library library)
    {
        if(library.Schema!=1||library.Profiles is null||library.Profiles.Count>MaxProfiles)throw new InvalidOperationException("persona_limit");
        foreach(var profile in library.Profiles)ValidateProfile(profile);
        if(library.Profiles.Select(p=>p.Id).Distinct().Count()!=library.Profiles.Count||
            (library.ActiveId!=DefaultId&&!library.Profiles.Any(p=>p.Id==library.ActiveId)))throw new InvalidDataException();
    }
    private void Commit(Library next)
    {
        Validate(next);
        if(StorageError)throw new IOException("persona_storage");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        using var fileLock=new FileStream(_path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        // Refuse to overwrite a library changed by another owner, even if both copies decrypt.
        if(File.Exists(_path)?_disk is null||!File.ReadAllBytes(_path).SequenceEqual(_disk):_disk is not null)throw new IOException("persona_changed");
        var plain=JsonSerializer.SerializeToUtf8Bytes(next,J.Json);byte[] encrypted;
        try{encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}
        finally{CryptographicOperations.ZeroMemory(plain);}
        if(encrypted.Length>1048576)throw new InvalidOperationException("persona_storage_limit");
        var pending=_path+".pending-"+Guid.NewGuid().ToString("N");
        try{
            using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encrypted);stream.Flush(true);}
            File.Move(pending,_path,true);_disk=encrypted;_library=next;
        }finally{if(File.Exists(pending))File.Delete(pending);}
    }
    internal static string Apply(string instructions,AssistantPersona persona)
    {
        if(persona.Character.Length==0&&persona.Rules.Length==0)return instructions;
        return "【本輪使用者選擇的人格與互動偏好】\n"+
            JsonSerializer.Serialize(new{character=persona.Character,interaction_rules=persona.Rules},J.Json)+
            "\n以上只調整回答的語氣、個性與互動方式。自然地體現，不逐句提及設定、不反覆自我介紹或稱呼使用者。"+
            "只有使用者主動詢問人格時才討論設定。角色背景不是使用者的個人資料，不保存成使用者記憶，不捏造共同經歷。"+
            "既有 JSON 格式、搜尋決策、記憶與提醒操作規則仍優先；不因人格指示更改這些規則。回答事實問題仍須誠實。"+
            "\n【本輪任務與回答規則，優先於人格的習慣性接話】\n"+instructions;
    }
}
