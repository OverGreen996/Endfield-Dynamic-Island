using System.Text.Json;
using Avalonia;
using EndfieldChargePlus.Assistant;
if(args.Contains("--gpu-live")){await GpuTelemetryProbe.LiveAsync();return;}
if(args.Contains("--gpu-live-ui")){Environment.ExitCode=AppBuilder.Configure<GpuLiveApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--gpu-unit")){GpuTelemetryProbe.Unit();return;}
if(args.Contains("--settings-chrome")||args.Contains("--settings-ui")){Environment.ExitCode=AppBuilder.Configure<SettingsChromeApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--performance-baseline")){await StartupPerformanceProbe.BaselineAsync();return;}
if(args.Contains("--performance-unit")){await StartupPerformanceProbe.UnitAsync();return;}
if(args.Contains("--performance-monitor")){await StartupPerformanceProbe.MonitorAsync();return;}
if(args.Contains("--performance-ui")){AppBuilder.Configure<StartupPerformanceApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--image-live-ui")){AppBuilder.Configure<PastedImageLiveApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--image-unit")){ImageInputProbe.Run();return;}
if(args.Contains("--industrial-unit")){IndustrialFeatureProbe.Run();return;}
if(args.Contains("--industrial-test")||args.Contains("--industrial-ui")){Environment.ExitCode=AppBuilder.Configure<IndustrialUiApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--collapse-test")||args.Contains("--collapse-ui")){AppBuilder.Configure<CollapseApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--personal-unit")){PersonalAssistantProbe.Run();return;}
if(args.Contains("--all-edge-test")){AppBuilder.Configure<AllEdgeApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--all-ui-layout")){AllUiLayoutProbe.Run();return;}
if(args.Contains("--music-edge-ui")||args.Contains("--music-edge-test")){AppBuilder.Configure<MusicEdgeApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--notification-unit")){NotificationProbe.Unit();return;}
if(args.Contains("--notification-ui")||args.Contains("--notification-routing")){AppBuilder.Configure<NotificationLiveApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--notification-monitor")){AppBuilder.Configure<NotificationMonitorProbe>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);return;}
if(args.Contains("--music-layout")){MusicLayoutProbe.Run();return;}
if(args.Contains("--music-unit")){await MusicProbe.Run();return;}
if(args.Contains("--bubble-layout")){BubbleLayoutProbe.Run();return;}
var root = Path.Combine(AppContext.BaseDirectory,"session-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed=0;
void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
AssistantReply Reply(string text,EvidenceSource[]? sources=null)=>new(text,"model","gemini-3.5-flash-lite",sources is not null,sources,null,null);
var path=Path.Combine(root,"session.dpapi");
var session=new AssistantSession(path);session.Append("alpha",Reply("beta"));
var reloaded=new AssistantSession(path);
Check(reloaded.Turns.Count==1&&reloaded.History()[1].text=="beta"&&reloaded.StorageNotice is null,"DPAPI save/reload preserves conversation");
Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("alpha"),"disk file is encrypted");
session.Clear();Check(new AssistantSession(path).Turns.Count==0,"new conversation persists reset");
var metadataPath=Path.Combine(root,"metadata.dpapi");var metadataSession=new AssistantSession(metadataPath);
var sources=Enumerable.Range(0,20).Select(i=>new EvidenceSource("S"+i,"official","https://example.com/"+new string('p',1500),"official",null,null)).ToArray();
for(int i=0;i<50;i++)metadataSession.Append("question "+i,Reply("response "+i,sources));
var metadataReload=new AssistantSession(metadataPath);
Check(metadataSession.OlderTurnsRemoved&&metadataSession.Turns.Count>0&&metadataReload.StorageNotice is null&&metadataReload.Turns.Count==metadataSession.Turns.Count&&metadataReload.Turns[^1].Question=="question 49","large source metadata is bounded and latest turn reloads");
Check(new FileInfo(metadataPath).Length<1500000,"encrypted session fits loader limit");
var longSession=new AssistantSession(Path.Combine(root,"long.dpapi"));
for(int i=0;i<40;i++)longSession.Append("question "+i+new string('a',14000),Reply(new string('b',14000)));
Check(longSession.OlderTurnsRemoved&&JsonSerializer.SerializeToUtf8Bytes(longSession.History()).Length<=900000,"outbound history stays below request budget");
var corruptPath=Path.Combine(root,"corrupt.dpapi");File.WriteAllBytes(corruptPath,new byte[]{1,2,3,4});
Check(new AssistantSession(corruptPath).StorageNotice is not null&&File.ReadAllBytes(corruptPath).SequenceEqual(new byte[]{1,2,3,4}),"corrupt existing session remains intact");
var blockedParent=Path.Combine(root,"not-a-directory");File.WriteAllText(blockedParent,"keep");var blocked=new AssistantSession(Path.Combine(blockedParent,"session.dpapi"));blocked.Append("kept",Reply("in memory"));
Check(blocked.StorageNotice is not null&&blocked.Turns.Count==1&&File.ReadAllText(blockedParent)=="keep","save failure preserves live conversation and reports failure");
Check(IslandGeometry.Width(0,1000)==420&&IslandGeometry.Width(2000,1000)==840&&IslandGeometry.Width(2000,250)==250,"horizontal size respects screen budget");
Check(!IslandGeometry.Contains(0,0,420,140)&&!IslandGeometry.Contains(-1,50,420,140)&&!IslandGeometry.Contains(420,50,420,140)&&IslandGeometry.Contains(24,24,420,140)&&IslandGeometry.Contains(210,70,420,140),"transparent rounded corners excluded from body hit area");
Console.WriteLine($"{passed}/{passed} PASS");
