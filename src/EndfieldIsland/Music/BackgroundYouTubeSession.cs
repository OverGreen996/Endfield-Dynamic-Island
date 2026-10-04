using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Music;

/// <summary>One dedicated original-page player, with private inherited pipes instead of global media keys.</summary>
public sealed class BackgroundYouTubeSession : IPlaylistMusicSession
{
    private readonly string _executable;
    private readonly object _gate=new();
    private readonly SemaphoreSlim _writeGate=new(1,1);
    private readonly ConcurrentDictionary<long,TaskCompletionSource<bool>> _acks=new();
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(3)};
    private Process? _process;
    private MusicSnapshot _snapshot=MusicSnapshot.Empty;
    private DateTimeOffset _updated;
    private string _error="",_artVideo="";
    private byte[]? _art;
    private long _id,_generation;
    private bool _disposed;
    public bool AudioObserved { get; private set; }
    public bool HostHidden { get; private set; }
    public BackgroundYouTubeSession(string? executable=null)=>_executable=executable??Path.Combine(AppContext.BaseDirectory,"MusicPlayerHost","MusicPlayerHost.exe");
    public void Open(string url)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);url=PlaylistStore.Normalize(url);
        if(!File.Exists(_executable))throw new FileNotFoundException("背景播放器未安裝，請使用原頁播放",_executable);
        if(_process is null||_process.HasExited)
        {
            _process?.Dispose();
            _process=Process.Start(new ProcessStartInfo(_executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(_executable)!})!;
            _ = ReceiveAsync(_process);_ = DrainErrorsAsync(_process);
        }
        lock(_gate){_generation++;_error="";_snapshot=MusicSnapshot.Empty with {Title="正在載入播放清單",Artist="背景播放器啟動中"};_updated=DateTimeOffset.UtcNow;_artVideo="";_art=null;}
        foreach(var ack in _acks.Values)ack.TrySetResult(false);_acks.Clear();
        _ = WriteOpenAsync(url,_generation);
    }
    private async Task WriteOpenAsync(string url,long generation)
    {try{await WriteAsync(new{id=Interlocked.Increment(ref _id),kind="Open",url,generation},CancellationToken.None);}catch{lock(_gate)_error="背景播放器啟動失敗，請重新開啟清單";}}
    private async Task WriteAsync(object message,CancellationToken token)
    {
        await _writeGate.WaitAsync(token);
        try{token.ThrowIfCancellationRequested();if(_process is null||_process.HasExited)throw new IOException("播放器已關閉");await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(),token);await _process.StandardInput.FlushAsync(token);}
        finally{_writeGate.Release();}
    }
    private async Task DrainErrorsAsync(Process process)
    {try{while(await process.StandardError.ReadLineAsync() is not null){}}catch{}}
    private async Task ReceiveAsync(Process process)
    {
        try
        {
            while(!_disposed&&ReferenceEquals(_process,process)&&await process.StandardOutput.ReadLineAsync() is string line)
            {
                if(line.Length>16384)continue;
                using var doc=JsonDocument.Parse(line);var r=doc.RootElement;
                var type=r.GetProperty("type").GetString();
                if(type=="status"){lock(_gate){_updated=DateTimeOffset.UtcNow;_snapshot=_snapshot with{Notice=r.GetProperty("message").GetString()};}}
                if(type=="ack"&&_acks.TryRemove(r.GetProperty("id").GetInt64(),out var ack))ack.TrySetResult(r.GetProperty("ok").GetBoolean());
                if(type=="error"){lock(_gate)_error=r.GetProperty("message").GetString()??"播放器錯誤";}
                if(type!="state"||r.GetProperty("generation").GetInt64()!=_generation)continue;
                AudioObserved|=r.GetProperty("audio").GetBoolean();HostHidden=r.GetProperty("hidden").GetBoolean();
                var s=r.GetProperty("state");
                var parsed=JsonSerializer.Deserialize<YouTubePlayerState>(s.GetRawText());
                if(parsed is null||!YouTubePlayerState.Valid(parsed))continue;
                var ad=s.GetProperty("ad").GetBoolean();var canShuffle=s.GetProperty("canShuffle").GetBoolean();var canRepeat=s.GetProperty("canRepeat").GetBoolean();
                lock(_gate)
                {
                    _updated=DateTimeOffset.UtcNow;
                    if(!string.IsNullOrEmpty(parsed.error))_error=parsed.error;else if(parsed.ready)_error="";
                    _snapshot=new(parsed.ready,parsed.title,parsed.artist,"專用 YouTube 背景播放器",parsed.playing,parsed.position,parsed.duration,
                        parsed.ready,parsed.ready,parsed.ready&&parsed.count>1&&!ad,parsed.ready&&parsed.count>1&&!ad,parsed.ready&&!ad,null,
                        parsed.ready&&canShuffle&&!ad,parsed.shuffle,parsed.ready&&canRepeat&&!ad,parsed.repeat,ad?"YouTube 正在播放廣告":_error.Length>0?_error:"指定清單 · 背景播放");
                    if(_artVideo!=parsed.video){_artVideo=parsed.video;_art=null;}
                }
            }
        }
        catch{lock(_gate)_error="背景播放器連線中斷，請重新開啟清單";}
        finally{foreach(var ack in _acks.Values)ack.TrySetResult(false);_acks.Clear();}
    }
    public async Task<MusicSnapshot> ReadAsync(CancellationToken token)
    {
        MusicSnapshot snapshot;string video;
        lock(_gate){snapshot=_snapshot;video=_artVideo;if(_error.Length>0)snapshot=snapshot with{Notice=_error};}
        if(_process is null||_process.HasExited||DateTimeOffset.UtcNow-_updated>TimeSpan.FromSeconds(8))
            return MusicSnapshot.Empty with {Title="背景播放器尚未連線",Artist="按封面啟動指定清單",Notice=_error};
        if(_art is null&&Regex.IsMatch(video,"^[A-Za-z0-9_-]{11}$"))
        {
            try
            {
                using var response=await _http.GetAsync("https://i.ytimg.com/vi/"+video+"/mqdefault.jpg",HttpCompletionOption.ResponseHeadersRead,token);
                if(response.IsSuccessStatusCode&&response.Content.Headers.ContentLength is >0 and <=500000)
                {var bytes=await response.Content.ReadAsByteArrayAsync(token);lock(_gate){if(_artVideo==video)_art=bytes;}}
            }
            catch(OperationCanceledException){throw;}catch{}
        }
        lock(_gate)return snapshot with{Artwork=_artVideo==video?_art:null};
    }
    public async Task<bool> SendAsync(MusicCommand command,double position,CancellationToken token)
    {
        if(_disposed||!double.IsFinite(position)||_acks.Count>=8)return false;
        if(command==MusicCommand.Repeat&&position is not(0 or 1 or 2)||command==MusicCommand.Shuffle&&position is not(0 or 1))return false;
        lock(_gate){if(!_snapshot.Available||DateTimeOffset.UtcNow-_updated>TimeSpan.FromSeconds(8))return false;}
        long id=Interlocked.Increment(ref _id);var ack=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);_acks[id]=ack;
        try{await WriteAsync(new{id,kind=command.ToString(),value=position,generation=_generation,expires=DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeMilliseconds()},token);return await ack.Task.WaitAsync(token);}
        finally{_acks.TryRemove(id,out _);if(token.IsCancellationRequested)_=CancelAsync(id);}
    }
    private async Task CancelAsync(long target)
    {try{await WriteAsync(new{id=Interlocked.Increment(ref _id),kind="Cancel",target},CancellationToken.None);}catch{}}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        foreach(var ack in _acks.Values)ack.TrySetResult(false);_acks.Clear();
        try{_process?.StandardInput.Close();}catch{}
        if(_process is not null)
        {
            var process=_process;_ = Task.Run(()=>{try{if(!process.WaitForExit(2000))process.Kill(true);}catch{}finally{process.Dispose();}});
        }
        _http.Dispose();
    }
}
