using EndfieldChargePlus.Assistant.Native;

namespace EndfieldChargePlus.Assistant;

/// <summary>In-process validation and Windows user-bound encrypted storage.</summary>
public sealed class GeminiCredentialClient
{
    private readonly string? _root;
    public GeminiCredentialClient(string? root=null)=>_root=root;
    public Task ConfigureAsync(string key,CancellationToken cancellation)=>ConfigureFreeAsync(key,true,cancellation);
    public Task ConfigureFreeAsync(string key,bool freeConfirmed,CancellationToken cancellation)=>Task.Run(async ()=>
    {
        if(!freeConfirmed)throw new InvalidOperationException(LocalizationManager.Text("請先確認此金鑰的專案使用免費方案，且未啟用付費。","Confirm that this key's project is on the free tier with billing disabled."));
        try {
            if(_root is null)await NativeAssistantService.Shared.ConfigureAsync(key,freeConfirmed,cancellation);
            else {using var service=new NativeAssistantService(_root);await service.ConfigureAsync(key,freeConfirmed,cancellation);}
        }
        catch(AssistantFailure e){throw new InvalidOperationException(AssistantClient.ErrorText(e.Code));}
    },cancellation);
    public Task InitializeAsync(CancellationToken cancellation)=>Task.Run(()=>
    {
        cancellation.ThrowIfCancellationRequested();
        if(_root is null)_=NativeAssistantService.Shared;
        else {using var service=new NativeAssistantService(_root);}
    },cancellation);
}