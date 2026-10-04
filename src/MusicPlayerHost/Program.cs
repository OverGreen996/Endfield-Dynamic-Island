using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize();Application.Run(new PlayerForm()); }
}
internal sealed class PlayerForm : Form
{
    private CoreWebView2Controller? _controller;
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=1000};
    private bool _busy,_stopping;
    private string _script="",_playlist="";
    private long _generation;
    private bool? _shufflePreference;
    private int? _repeatPreference;
    private DateTimeOffset _started;
    private readonly HashSet<long> _cancelled=new();
    protected override bool ShowWithoutActivation=>true;
    public PlayerForm()
    {
        Width=1280;Height=720;Opacity=0;ShowInTaskbar=false;Text="靈動島音樂播放器";
        Shown+=async(_,_)=>await InitializeAsync();
        _timer.Tick+=async(_,_)=>await PollAsync();
        FormClosed+=(_,_)=>{_stopping=true;_timer.Stop();_controller?.Close();};
    }
    private static void Write(object value){Console.WriteLine(JsonSerializer.Serialize(value));Console.Out.Flush();}
    private async Task InitializeAsync()
    {
        try
        {
            using var resource=typeof(PlayerForm).Assembly.GetManifestResourceStream("MusicPlayerHost.PlayerBridge.js")!;
            using var reader=new StreamReader(resource);_script=await reader.ReadToEndAsync();
            var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EndfieldChargePlus","MusicPlayerProfile");
            var env=await CoreWebView2Environment.CreateAsync(null,profile,new(){AreBrowserExtensionsEnabled=true});
            _controller=await env.CreateCoreWebView2ControllerAsync(Handle);_controller.Bounds=new Rectangle(0,0,1280,720);
            var core=_controller.CoreWebView2;
            core.NewWindowRequested+=(_,e)=>e.Handled=true;core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.NavigationStarting+=(_,e)=>{if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var u)||u.Scheme!="https"||u.Host!="www.youtube.com")e.Cancel=true;};
            var nonstop=Path.Combine(AppContext.BaseDirectory,"nonstop");
            if(File.Exists(Path.Combine(nonstop,"manifest.json")))
                try{var extension=await core.Profile.AddBrowserExtensionAsync(nonstop);Write(new{type="extension",name=extension.Name,enabled=extension.IsEnabled});}catch{Write(new{type="extension",enabled=false});}
            Write(new{type="host-ready"});_timer.Start();_ = Task.Run(ReadCommandsAsync);
        }
        catch(Exception ex){Write(new{type="error",message="背景播放器無法啟動："+ex.Message});Close();}
    }
    private async Task ReadCommandsAsync()
    {
        // This stream is an anonymous pipe owned by the parent, never a network endpoint.
        while(!_stopping)
        {
            var line=await Console.In.ReadLineAsync();if(line is null){if(!_stopping)BeginInvoke(new Action(Close));return;}
            if(line.Length>8192)continue;
            try
            {
                using var doc=JsonDocument.Parse(line);var request=doc.RootElement.Clone();
                // Marshal onto the WinForms STA; WebView2 APIs must stay on its owning thread.
                BeginInvoke(new Action(async()=>await HandleAsync(request)));
            }
            catch{Write(new{type="error",message="無效播放器指令"});}
        }
    }
    private async Task<JsonElement> Script(string script)
    {using var doc=JsonDocument.Parse(await _controller!.CoreWebView2.ExecuteScriptAsync(script));return doc.RootElement.Clone();}
    private async Task HandleAsync(JsonElement r)
    {
        long id=0;bool ownsBusy=false;
        try
        {
            id=r.GetProperty("id").GetInt64();var kind=r.GetProperty("kind").GetString();
            if(kind=="Close"){Close();return;}
            if(kind=="Cancel"){if(_cancelled.Count>=256)_cancelled.Remove(_cancelled.Min());_cancelled.Add(r.GetProperty("target").GetInt64());return;}
            if(kind=="Open")
            {
                var url=r.GetProperty("url").GetString()??"";
                if(!Regex.IsMatch(url,@"^https://www\.youtube\.com/playlist\?list=[A-Za-z0-9_-]{3,150}$"))throw new InvalidDataException("不合法的播放清單");
                _playlist=url.Split("list=")[1];_generation=r.GetProperty("generation").GetInt64();_started=DateTimeOffset.UtcNow;
                _shufflePreference=null;_repeatPreference=null;
                Opacity=0;Show();_controller!.IsVisible=true;
                _controller.CoreWebView2.Navigate(url);Write(new{type="ack",id,ok=true});return;
            }
            var expires=r.GetProperty("expires").GetInt64();
            while(_busy&&!_stopping&&!_cancelled.Contains(id)&&DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()<expires)await Task.Delay(20);
            if(_busy||_stopping||_cancelled.Remove(id)||r.GetProperty("generation").GetInt64()!=_generation||expires<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {Write(new{type="ack",id,ok=false});return;}
            _busy=true;ownsBusy=true;
            var value=r.GetProperty("value").GetDouble();if(!double.IsFinite(value))throw new InvalidDataException();
            var args=JsonSerializer.Serialize(new{kind,value});
            var commandGeneration=_generation;
            var result=await Script($"(()=>{{if(location.hostname!=='www.youtube.com')return false;{_script};return window.islandMusic.command({args});}})()");
            if(commandGeneration!=_generation||_cancelled.Remove(id)||expires<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()){Write(new{type="ack",id,ok=false});return;}
            if(result.ValueKind==JsonValueKind.True){if(kind=="Shuffle")_shufflePreference=value==1;if(kind=="Repeat")_repeatPreference=(int)value;}
            Write(new{type="ack",id,ok=result.ValueKind==JsonValueKind.True});await PollAsync();
        }
        catch{Write(new{type="ack",id,ok=false});}
        finally{if(ownsBusy)_busy=false;}
    }
    private async Task PollAsync()
    {
        if(_busy||_stopping||_controller is null||_playlist.Length==0)return;_busy=true;
        try
        {
            var generation=_generation;
            var preferences=JsonSerializer.Serialize(new{shuffle=_shufflePreference,repeat=_repeatPreference});
            var state=await Script($"(()=>{{if(location.hostname!=='www.youtube.com')return null;{_script};window.islandMusic.restore({preferences});return window.islandMusic.snapshot();}})()");
            if(generation!=_generation)return;
            if(state.ValueKind==JsonValueKind.Object)
            {
                if(state.TryGetProperty("firstVideo",out var first))
                {
                    Write(new{type="status",message="正在解析播放清單",title=_controller.CoreWebView2.DocumentTitle});
                    var video=first.GetString();
                    if(video is not null&&Regex.IsMatch(video,"^[A-Za-z0-9_-]{11}$"))
                        _controller.CoreWebView2.Navigate("https://www.youtube.com/watch?v="+video+"&list="+_playlist);
                    if((DateTimeOffset.UtcNow-_started).TotalSeconds>60)Write(new{type="error",message="無法解析播放清單，請用右鍵的原頁模式檢查"});return;
                }
                var ready=state.GetProperty("ready").GetBoolean();
                if(ready&&Visible){Hide();_controller.IsVisible=false;_controller.CoreWebView2.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Low;}
                Write(new{type="state",generation=_generation,state,audio=_controller.CoreWebView2.IsDocumentPlayingAudio,hidden=!Visible});
                if(!ready&&(DateTimeOffset.UtcNow-_started).TotalSeconds>90)
                    Write(new{type="error",message="YouTube 載入逾時；請使用原頁模式檢查登入或播放限制"});
            }
        }
        catch{Write(new{type="error",message="播放器資料暫時無法讀取"});}
        finally{_busy=false;}
    }
}
