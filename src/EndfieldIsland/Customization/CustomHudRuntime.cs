using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Customization;

public sealed class CustomHudRuntime : IDisposable
{
    private readonly HudWindow _hud;
    private readonly HudDataService _data;
    private readonly bool _ownsData;
    public HudDataService Data => _data;
    private int _displayEpoch;
    private string _displayProfileId="";
    private bool _disposed;
    private bool _stopped;
    private CancellationTokenSource? _warming;
    private readonly DispatcherTimer _timer;

    private AppSettings _appSettings = new();
    private CustomHudSettings _settings = CustomHudSettings.CreateDefault();

    private DateTime _nextPersistentRefresh = DateTime.MinValue;
    private DateTime _nextPersistentCycle = DateTime.MinValue;
    private DateTime _nextPowerPoll = DateTime.MinValue;
    private long _lastRenderedLocalSecond = -1;
    private int _profileIndex;
    private int _busy;
    private int _settingsTransitionBusy;
    private bool _persistentShown;
    private string _persistentProfileId = "";
    private bool _previewShown;
    private DateTime _previewHideAt = DateTime.MinValue;
    private HudProfile? _previewProfile;
    private bool _userPinned;
    private bool _assistantActive;
    public AppSettings EffectiveSettings => _appSettings;

    public async Task BeginAssistantAsync()
    {
        _assistantActive = true;
        _timer.Stop();
        ++_displayEpoch;
        _displayProfileId="";
        // Switching the visible surface cancels its animation; sampling stays independent.
        await _hud.HideAnimatedAsync();
        _previewShown = false;
        _persistentShown = false;
    }

    public void EndAssistant()
    {
        _assistantActive = false;
        _hotZoneLatched = true;
        _nextPersistentRefresh = DateTime.MinValue;
        if (!_stopped && !_disposed) _timer.Start();
    }

    private bool _hotZoneLatched;
    private bool? _lastAcOnline;
    private bool? _candidateAcOnline;
    private DateTime _candidateAcSince = DateTime.MinValue;
    private bool _pendingPowerEvent;
    private bool _pendingAcOnline;

    public CustomHudRuntime(HudWindow hud,HudDataService? data=null)
    {
        _hud = hud;
        _data=data??new HudDataService();_ownsData=data is null;
        _hud.PinToggleRequested += OnPinToggleRequested;
        // 100 ms：用於螢幕頂邊熱點偵測和牆鍾秒邊界同步。重型指標仍按需取樣。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public void ApplySettings(AppSettings settings)
    {
        ++_displayEpoch;_displayProfileId="";
        _appSettings = settings ?? new AppSettings();
        _settings = HudSettingsNormalizer.Normalize(_appSettings.CustomHud ?? CustomHudSettings.CreateDefault());
        _profileIndex = _settings.AutoCycle ? 0 : ResolveActiveProfileIndex();
        _nextPersistentRefresh = DateTime.MinValue;
        _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
        _lastRenderedLocalSecond = -1;
        _hotZoneLatched = false;
        _persistentProfileId = "";
        _previewShown = false;
        _previewProfile = null;
        _previewHideAt = DateTime.MinValue;
        _userPinned = false;

        if (!_appSettings.AlwaysVisible || !_appSettings.HudEnabled)
            _persistentShown = false;
    }

    public async Task ApplySettingsWithTransitionAsync(AppSettings settings)
    {
        if (Interlocked.Exchange(ref _settingsTransitionBusy, 1) != 0) return;

        _timer.Stop();
        try
        {
            // Every applied profile/parameter change follows the same full lifecycle:
            // retract old HUD -> apply settings -> summon the new HUD. Never mutate a visible HUD in place.
            if (_hud.IsVisible)
                await _hud.HideAnimatedAsync();

            _persistentShown = false;
            _hud.ApplySettings(settings);
            ApplySettings(settings);

            if (!settings.HudEnabled || _assistantActive)
                return;

            var profile = settings.AlwaysVisible && _settings.AutoCycle
                ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
                : ResolveActiveProfile();
            if (profile is null)
                return;

            if (settings.AlwaysVisible && _settings.AutoCycle)
                profile = ApplyCycleAnimationMode(profile);

            var data = ReadAndRefresh(profile);

            if (_assistantActive) return;



            if (settings.AlwaysVisible)
            {
                await _hud.ShowPersistentAsync(data, quick: true);
                if(_assistantActive||_disposed)return;
                _persistentShown = true;
                _persistentProfileId = profile.Id;
                _nextPersistentRefresh = DateTime.MinValue;
                _lastRenderedLocalSecond = -1;
            }
            else
            {
                await _hud.ShowPreviewAsync(data, allowPin: true);
                if(_assistantActive||_disposed)return;
                _previewShown = true;
                _previewProfile = profile;
                _previewHideAt = DateTime.UtcNow.AddSeconds(Math.Clamp(_appSettings.DisplayDurationSeconds, 3d, 10d));
            }
        }
        finally
        {
            if (!_stopped && !_disposed && !_assistantActive) _timer.Start();
            Interlocked.Exchange(ref _settingsTransitionBusy, 0);
        }
    }

    // Kept for compatibility with older callers.
    public void ApplySettings(CustomHudSettings settings)
    {
        ApplySettings(_appSettings with { CustomHud = settings ?? CustomHudSettings.CreateDefault() });
    }

    public void Start()
    {
        if(_disposed)return;
        _stopped=false;
        _warming??=new CancellationTokenSource();
        if(!_warmingStarted){_warmingStarted=true;_=WarmLoopAsync(_warming.Token);}
        _=RunStartupPresentationAsync();
    }
    private bool _warmingStarted;
    public void Stop()
    {
        _stopped=true;
        _timer.Stop();_warming?.Cancel();_warming?.Dispose();_warming=null;_warmingStarted=false;
    }
    private async Task WarmLoopAsync(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var started=System.Diagnostics.Stopwatch.GetTimestamp();
                var target=await Dispatcher.UIThread.InvokeAsync(()=>
                {
                    var profile=_previewProfile??_settings.Profiles.FirstOrDefault(p=>p.Id==_persistentProfileId)??ResolveActiveProfile();
                    return (Settings:_settings,Profile:profile,Visible:_hud.IsVisible&&!_assistantActive,Enabled:_appSettings.HudEnabled,Epoch:_displayEpoch);
                });
                // Never prefetch HTTP, DNS, developer tools or security data just to warm hardware.
                if(target.Enabled&&target.Profile is {} profile)
                {
                    if(HudDataService.CanPrewarm(profile))
                    {
                        await _data.RefreshAsync(target.Settings,profile,ct).ConfigureAwait(false);
                        if(target.Visible)_=RefreshVisibleAsync(target.Settings,profile,target.Epoch);
                    }
                }
                var period=TimeSpan.FromSeconds(target.Visible?1:5);
                var elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started);
                await Task.Delay(elapsed<period?period-elapsed:TimeSpan.FromMilliseconds(100),ct).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException){}
        catch(ObjectDisposedException){}
    }

    private async Task RunStartupPresentationAsync()
    {
        if (Interlocked.Exchange(ref _settingsTransitionBusy, 1) != 0) return;

        _timer.Stop();
        try
        {
            if (!_appSettings.HudEnabled || _assistantActive)
                return;

            // Every process start presents the currently effective scheme once through
            // its normal summon animation. If AlwaysVisible is enabled, the same animation
            // lands in the persistent C-state and live monitoring continues from there.
            var profile = _appSettings.AlwaysVisible && _settings.AutoCycle
                ? ResolveCycleProfiles().FirstOrDefault() ?? ResolveActiveProfile()
                : ResolveActiveProfile();
            if (profile is null)
                return;

            if (_appSettings.AlwaysVisible && _settings.AutoCycle)
                profile = ApplyCycleAnimationMode(profile);

            var data = ReadAndRefresh(profile);



            if (_assistantActive) return;

            if (_appSettings.AlwaysVisible)
            {
                await _hud.ShowPersistentAsync(data, quick: true);
                if(_assistantActive||_disposed)return;
                _persistentShown = true;
                _persistentProfileId = profile.Id;
                _nextPersistentRefresh = DateTime.MinValue;
                _lastRenderedLocalSecond = -1;
                _nextPersistentCycle = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
            }
            else
            {
                await _hud.ShowPreviewAsync(data, allowPin: true);
                if(_assistantActive||_disposed)return;
                _previewShown = true;
                _previewProfile = profile;
                _previewHideAt = DateTime.UtcNow.AddSeconds(Math.Clamp(_appSettings.DisplayDurationSeconds, 3d, 10d));
                _persistentShown = false;
                _persistentProfileId = string.Empty;
            }
        }
        catch
        {
            // Startup presentation must never prevent the tray/settings application from running.
        }
        finally
        {
            Interlocked.Exchange(ref _settingsTransitionBusy, 0);
            if (!_stopped && !_disposed && !_assistantActive) _timer.Start();
        }
    }

    public async Task PreviewAsync(HudProfile profile)
    {
        if (_assistantActive || _userPinned || _appSettings.AlwaysVisible) return;
        await TriggerTransientProfileAsync(profile);
    }

    public async Task PreviewActiveAsync()
    {
        if (_assistantActive || _userPinned || _appSettings.AlwaysVisible) return;
        var profile = ResolveActiveProfile();
        if (profile is not null)
            await TriggerTransientProfileAsync(profile);
    }

    private async void OnPinToggleRequested()
    {
        if (_assistantActive || !_appSettings.HudEnabled || _appSettings.AlwaysVisible)
            return;

        if (_userPinned)
        {
            _userPinned = false;
            _persistentShown = false;
            _persistentProfileId = "";
            _previewShown = false;
            _previewProfile = null;
            _hotZoneLatched = true;
            await _hud.HidePersistentAsync();
            return;
        }

        if (!_hud.IsVisible)
            return;

        var profile = _previewProfile ?? ResolveActiveProfile();
        _userPinned = true;
        _previewShown = false;
        _previewProfile = null;
        _persistentShown = true;
        _persistentProfileId = profile?.Id ?? "";
        _nextPersistentRefresh = DateTime.MinValue;
        _lastRenderedLocalSecond = -1;
        _hud.PromotePreviewToPersistent();
    }

    private async Task TickAsync()
    {
        if (_assistantActive || Volatile.Read(ref _settingsTransitionBusy) != 0) return;

        if (!_appSettings.HudEnabled)
        {
            if (_persistentShown || _previewShown || _hud.IsVisible)
            {
                await _hud.HidePersistentAsync();
                _persistentShown = false;
                _previewShown = false;
                _previewProfile = null;
                _persistentProfileId = "";
            }
            return;
        }

        if (_appSettings.AlwaysVisible || _userPinned)
        {
            _hotZoneLatched = false;
            _previewShown = false;
            await TickPersistentAsync();
            return;
        }

        if (_persistentShown)
        {
            await _hud.HidePersistentAsync();
            _persistentShown = false;
            _persistentProfileId = "";
        }

        await TickTransientTriggersAsync();
    }

    private async Task TickTransientTriggersAsync()
    {
        PollPowerSource();

        // 電源插拔優先：不受目前選中方案影響，沿用“插電完整 / 斷電簡潔”動畫。
        if (_pendingPowerEvent && !_hud.IsHudBusy && Volatile.Read(ref _busy) == 0)
        {
            bool acOnline = _pendingAcOnline;
            _pendingPowerEvent = false;
            var battery = FindBatteryProfile() with { AnimationMode = acOnline ? "Full" : "Simple" };
            await TriggerTransientProfileAsync(battery, allowPin: false);
            return;
        }

        bool havePointer = WindowsSystemProbe.TryGetCursorPosition(out var pointer);

        if (_previewShown)
        {
            if (!_hud.IsVisible)
            {
                _previewShown = false;
                _previewProfile = null;
            }
            else
            {
                bool interacting = havePointer && _hud.IsPointInsideInteractiveHud(pointer);
                if (interacting)
                {
                    _previewHideAt = DateTime.UtcNow.AddSeconds(Math.Clamp(_appSettings.DisplayDurationSeconds, 3d, 10d));
                    return;
                }

                if (DateTime.UtcNow >= _previewHideAt && !_hud.IsHudBusy)
                {
                    await _hud.HideAnimatedAsync();
                    _previewShown = false;
                    _previewProfile = null;
                }
                return;
            }
        }

        if (!havePointer)
            return;

        bool inHotZone = _hud.IsPointInTopCenterHotZone(pointer);
        if (!inHotZone)
        {
            _hotZoneLatched = false;
            return;
        }

        if (_hotZoneLatched || _hud.IsHudBusy || Volatile.Read(ref _busy) != 0)
            return;

        var active = ResolveActiveProfile();
        if (active is null || IsBatteryProfile(active))
        {
            _hotZoneLatched = true;
            return;
        }

        // 只有真正開始喚出後才鎖住熱點；滑鼠離開頂邊後允許下一次喚出。
        _hotZoneLatched = true;
        await TriggerTransientProfileAsync(active);
    }

    private void PollPowerSource()
    {
        var now = DateTime.UtcNow;
        if (now < _nextPowerPoll) return;
        _nextPowerPoll = now.AddMilliseconds(150);

        if (!WindowsSystemProbe.TryGetAcOnline(out bool acOnline)) return;
        if (_lastAcOnline is null)
        {
            _lastAcOnline = acOnline;
            _candidateAcOnline = null;
            return;
        }

        if (_lastAcOnline.Value == acOnline)
        {
            _candidateAcOnline = null;
            return;
        }

        if (_candidateAcOnline != acOnline)
        {
            _candidateAcOnline = acOnline;
            _candidateAcSince = now;
            return;
        }

        // 與上游專案一致採用雙向穩定確認，過濾 Windows 電源狀態的瞬時抖動。
        if ((now - _candidateAcSince).TotalMilliseconds < 400) return;

        _lastAcOnline = acOnline;
        _candidateAcOnline = null;
        _pendingAcOnline = acOnline;
        _pendingPowerEvent = true;
    }

    private async Task<bool> TriggerTransientProfileAsync(HudProfile profile, bool allowPin = true)
    {
        if (_assistantActive) return false;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
        try
        {
            var data = ReadAndRefresh(profile);

            if (_assistantActive) return false;

            if (!allowPin)
            {

                await _hud.ShowCustomAsync(data,ct=>RefreshDataAsync(profile,ct));
                return true;
            }

            await _hud.ShowPreviewAsync(data, allowPin: true);
                if(_assistantActive||_disposed)return false;
            _previewShown = true;
            _previewProfile = profile;
            _previewHideAt = DateTime.UtcNow.AddSeconds(Math.Clamp(_appSettings.DisplayDurationSeconds, 3d, 10d));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task TickPersistentAsync()
    {
        var profiles = _settings.AutoCycle
            ? ResolveCycleProfiles().ToList()
            : _settings.Profiles.ToList();

        // An explicit empty queue is valid configuration. Keep the currently selected
        // scheme visible rather than inventing queue entries that the user did not add.
        if (_settings.AutoCycle && profiles.Count == 0)
        {
            var active = ResolveActiveProfile();
            if (active is not null)
                profiles.Add(active);
        }

        if (profiles.Count == 0)
        {
            if (_persistentShown)
            {
                await _hud.HidePersistentAsync();
                _persistentShown = false;
                _persistentProfileId = "";
            }
            return;
        }

        var nowUtc = DateTime.UtcNow;

        if (_settings.AutoCycle && nowUtc >= _nextPersistentCycle)
        {
            if (_persistentShown)
                _profileIndex = (_profileIndex + 1) % profiles.Count;
            else if (_profileIndex >= profiles.Count)
                _profileIndex = 0;

            _nextPersistentCycle = nowUtc.AddSeconds(Math.Clamp(_settings.CycleSeconds, 3, 3600));
            _nextPersistentRefresh = DateTime.MinValue;
            _lastRenderedLocalSecond = -1;
        }
        else if (!_settings.AutoCycle)
        {
            int resolved = ResolveActiveProfileIndex(profiles);
            if (resolved != _profileIndex)
            {
                _profileIndex = resolved;
                _nextPersistentRefresh = DateTime.MinValue;
                _lastRenderedLocalSecond = -1;
            }
        }

        if (_profileIndex >= profiles.Count) _profileIndex = 0;
        var profile = profiles[_profileIndex];
        bool clockAccurate = HudProfileRenderer.NeedsSecondAccurateClock(profile);

        if (clockAccurate)
        {
            long localSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
            if (localSecond == _lastRenderedLocalSecond || _hud.IsHudBusy) return;
            _lastRenderedLocalSecond = localSecond;
        }
        else
        {
            if (nowUtc < _nextPersistentRefresh || _hud.IsHudBusy) return;
        }

        if (Interlocked.Exchange(ref _busy, 1) != 0) return;

        try
        {
            bool profileChanged = _persistentShown
                                  && _hud.IsVisible
                                  && !string.Equals(_persistentProfileId, profile.Id, StringComparison.OrdinalIgnoreCase);

            var renderProfile = _settings.AutoCycle ? ApplyCycleAnimationMode(profile) : profile;
            var data = ReadAndRefresh(renderProfile);

            if (_assistantActive) return;



            if (profileChanged)
            {
                await _hud.HideAnimatedAsync();
                await _hud.ShowPersistentAsync(data, sessionPinned: _userPinned, quick: true);
                if(_assistantActive||_disposed)return;
                _persistentShown = true;
            }
            else if (!_persistentShown || !_hud.IsVisible)
            {
                await _hud.ShowPersistentAsync(data, sessionPinned: _userPinned, quick: true);
                if(_assistantActive||_disposed)return;
                _persistentShown = true;
            }
            else
            {
                _hud.UpdatePersistent(data);
            }

            _persistentProfileId = profile.Id;
            _nextPersistentRefresh = clockAccurate ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(1);
            if (clockAccurate)
                _lastRenderedLocalSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
        }
        catch
        {
            _nextPersistentRefresh = DateTime.UtcNow.AddSeconds(2);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private IReadOnlyList<HudProfile> ResolveCycleProfiles()
    {
        var ids = _settings.CycleProfileIds ?? new List<string>();
        if (ids.Count == 0 || _settings.Profiles.Count == 0)
            return Array.Empty<HudProfile>();

        var byId = _settings.Profiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<HudProfile>(ids.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            if (byId.TryGetValue(id, out var profile))
                result.Add(profile);
        }
        return result;
    }

    private HudProfile ApplyCycleAnimationMode(HudProfile profile)
    {
        string mode = string.Equals(_settings.CycleAnimationMode, "Simple", StringComparison.OrdinalIgnoreCase)
            ? "Simple"
            : "Full";
        return profile with { AnimationMode = mode };
    }

    private HudProfile? ResolveActiveProfile()
    {
        var profiles = _settings.Profiles.ToList();
        if (profiles.Count == 0) return null;
        int index = ResolveActiveProfileIndex(profiles);
        return index >= 0 && index < profiles.Count ? profiles[index] : profiles[0];
    }

    private HudProfile FindBatteryProfile()
    {
        var existing = _settings.Profiles.FirstOrDefault(IsBatteryProfile);
        if (existing is not null) return existing;

        return CustomHudSettings.CreateDefaultProfiles().First(IsBatteryProfile);
    }

    private static bool IsBatteryProfile(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "system.battery", StringComparison.OrdinalIgnoreCase)
        || ((string.Equals(profile.Category, "系統", StringComparison.OrdinalIgnoreCase)
             || string.Equals(profile.Category, "System", StringComparison.OrdinalIgnoreCase))
            && (string.Equals(profile.Name, "電池", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.Name, "Battery", StringComparison.OrdinalIgnoreCase)));

    private int ResolveActiveProfileIndex()
    {
        var profiles = _settings.Profiles.ToList();
        return ResolveActiveProfileIndex(profiles);
    }

    private int ResolveActiveProfileIndex(IReadOnlyList<HudProfile> profiles)
    {
        if (profiles.Count == 0) return 0;
        if (!string.IsNullOrWhiteSpace(_settings.ActiveProfileId))
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i].Id, _settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return 0;
    }

    public void Dispose()
    {
        _disposed=true;++_displayEpoch;
        Stop();
        _hud.PinToggleRequested -= OnPinToggleRequested;
        if(_ownsData)_data.Dispose();
    }

    private HudRenderData ReadAndRefresh(HudProfile profile)
    {
        _displayProfileId=profile.Id;
        _=RefreshVisibleAsync(_settings,profile,_displayEpoch);
        return _data.Read(_settings,profile);
    }
    private async Task RefreshVisibleAsync(CustomHudSettings settings,HudProfile profile,int epoch)
    {
        try
        {
            await Task.Yield();
            await _data.RefreshAsync(settings,profile).ConfigureAwait(false);
            for(int attempt=0;attempt<40;attempt++)
            {
                bool retry=await Dispatcher.UIThread.InvokeAsync(()=>
                {
                    if(_disposed||_assistantActive||epoch!=_displayEpoch||!ReferenceEquals(settings,_settings)||_displayProfileId!=profile.Id)return false;
                    if(_hud.IsHudBusy)return true;
                    _hud.UpdateVisible(_data.Read(settings,profile));return false;
                });
                if(!retry)break;
                await Task.Delay(25).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException){}
        catch(ObjectDisposedException){}
    }
    private async Task<HudRenderData> RefreshDataAsync(HudProfile profile,CancellationToken ct)
    {
        var settings=_settings;
        await _data.RefreshAsync(settings,profile,ct).ConfigureAwait(false);
        return _data.Read(settings,profile);
    }
}
