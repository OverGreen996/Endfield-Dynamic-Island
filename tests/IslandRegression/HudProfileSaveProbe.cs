using Avalonia.Controls;
using Avalonia.Interactivity;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Views;
using System.Reflection;
using System.Text.Json;

internal static class HudProfileSaveProbe
{
    internal static void Run(Action<bool,string> check)
    {
        var values = new Dictionary<string,object?> {
            ["cpu.usage"]=12d, ["cpu.frequency_ghz"]=4.4d, ["gpu.usage"]=26d,
            ["gpu.dedicated_used_bytes"]=1.9*1073741824d, ["gpu.vram_bytes"]=12*1073741824d,
            ["memory.usage"]=49d, ["memory.used_bytes"]=15.5*1073741824d,
            ["memory.total_bytes"]=31.9*1073741824d
        };
        foreach (var language in new[]{AppLanguage.TraditionalChinese,AppLanguage.English})
        {
            LocalizationManager.SetLanguage(language);
            var config=CustomHudSettings.CreateDefault();
            var overview=config.Profiles.Single(p=>p.BuiltInKey=="system.overview");
            var hud=new HudWindow();var editor=new HudCustomizerView();editor.Load(config,hud);
            void Click(string name)=>editor.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var apply=typeof(HudCustomizerView).GetMethod("ApplyGpuAdapters",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var adapters=new[]{new GpuAdapterInfo("test-first","GPU A",0,12*1073741824d,0,Array.Empty<string>(),Array.Empty<string>()),
                new GpuAdapterInfo("test-second","GPU B",1,8*1073741824d,0,Array.Empty<string>(),Array.Empty<string>())};
            apply.Invoke(editor,new object[]{adapters});
            Click("SaveProfileBtn");var unchanged=editor.ExportSettings();
            check(unchanged.Profiles.Count==config.Profiles.Count&&unchanged.ActiveProfileId==overview.Id,
                "select/save overview does not create a copy after GPU detection: "+language);
            check(unchanged.Profiles.Single(p=>p.Id==overview.Id).GpuAdapterId=="","automatic GPU remains automatic: "+language);
            editor.FindControl<ComboBox>("GpuAdapterCombo")!.SelectedIndex=1;
            Click("SaveProfileBtn");var selected=editor.ExportSettings();
            check(selected.Profiles.Count==config.Profiles.Count&&selected.ActiveProfileId==overview.Id
                &&selected.Profiles.Single(p=>p.Id==overview.Id).GpuAdapterId=="test-second",
                "changing GPU saves preset option without creating a copy: "+language);
            check(!editor.FindControl<TextBlock>("ProfileStatusText")!.Text!.Contains("copy",StringComparison.OrdinalIgnoreCase),
                "device option save feedback does not falsely claim a copy: "+language);
            editor.FindControl<TextBox>("ProfileNameBox")!.Text="My overview 2";
            editor.FindControl<TextBox>("AccentColorBox")!.Text="#FFE500";
            Click("SaveProfileBtn");var saved=editor.ExportSettings();
            var copy=saved.Profiles.Single(p=>p.Id==saved.ActiveProfileId);
            check(!copy.IsBuiltIn&&copy.BuiltInKey==""&&copy.PresentationLayout=="SystemOverview"
                &&copy.Name=="My overview 2"&&copy.GpuAdapterId=="test-second","editable copy preserves overview layout and GPU: "+language);
            check(HudProfileRenderer.GetRequiredVariables(copy).Contains("gpu.usage")
                &&HudProfileRenderer.GetRequiredVariables(copy).Contains("memory.total_bytes")&&HudDataService.CanPrewarm(copy),
                "copy requests all telemetry and remains eligible for prewarming: "+language);
            var rendered=HudProfileRenderer.Render(copy,values);
            check(rendered.Metrics?.Select(m=>m.Label).SequenceEqual(new[]{"CPU","GPU","VRAM","RAM"})==true
                &&rendered.Metrics[1].Value=="26%"&&rendered.Metrics[2].Value=="1.9 GB",
                "saved copy renders four metrics rather than an empty template: "+language);
            var reloaded=HudSettingsNormalizer.Normalize(JsonSerializer.Deserialize<CustomHudSettings>(JsonSerializer.Serialize(saved))!);
            editor.Load(reloaded,hud);var reopened=editor.ExportSettings();var active=reopened.Profiles.Single(p=>p.Id==copy.Id);
            check(reopened.ActiveProfileId==copy.Id&&reopened.Profiles.Count==saved.Profiles.Count
                &&active.AccentColor=="#FFE500"&&HudProfileRenderer.Render(active,values).Metrics?.Count==4,
                "save/reload/reopen preserves custom edits, active ID and four metrics: "+language);
            var legacy=copy with{PresentationLayout="",Name="效能總覽 2"};
            var migrated=HudSettingsNormalizer.Normalize(reloaded with{Profiles=reloaded.Profiles.Select(p=>p.Id==legacy.Id?legacy:p).ToList()});
            var repaired=migrated.Profiles.Single(p=>p.Id==legacy.Id);
            check(migrated.ActiveProfileId==copy.Id&&!repaired.IsBuiltIn&&repaired.Name==legacy.Name
                &&repaired.GpuAdapterId==legacy.GpuAdapterId&&repaired.AccentColor==legacy.AccentColor
                &&HudProfileRenderer.Render(repaired,values).Metrics?.Count==4,"legacy empty copy repaired without replacing user settings: "+language);
            var ordinary=legacy with{PrimaryTemplate="{cpu.usage|0}",TitleTemplate="Custom"};
            check(HudSettingsNormalizer.NormalizeProfile(ordinary).PresentationLayout==""
                &&HudProfileRenderer.Render(HudSettingsNormalizer.NormalizeProfile(ordinary),values).Metrics is null,
                "ordinary custom templates are not converted into an overview: "+language);
            hud.Close();
        }
    }
}
