using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using EndfieldChargePlus.Music;

namespace EndfieldChargePlus;

public partial class App : Application
{
    private HudWindow? _hud;
    private CustomHudRuntime? _runtime;
    private WindowsTrayIcon? _tray;
    private TrayMenuWindow? _trayMenu;
    private SettingsWindow? _settingsWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private CancellationTokenSource? _activationListenerCts;
    private AssistantIslandWindow? _assistant;
    private bool _assistantOpening;
    private MusicIslandWindow? _music;
    private bool _musicOpening;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            AppLog.Info("Avalonia framework initialization completed; loading settings.");
            var settings = SettingsManager.Load();
            LocalizationManager.Initialize(settings.UiLanguage);
            StartupManager.Apply(settings.StartWithWindows);

            _hud = new HudWindow();
            _hud.ApplySettings(settings);

            _runtime = new CustomHudRuntime(_hud);
            _runtime.ApplySettings(settings);
            _runtime.Start();

            // Normal interactive launch opens Settings. Windows autostart stays silent:
            // only the tray icon and startup HUD presentation are shown.
            if (!Program.IsAutoStart)
            {
                _settingsWindow = CreateSettingsWindow(settings);
                desktop.MainWindow = _settingsWindow;
            }
            else
            {
                _settingsWindow = null;
                AppLog.Info("Autostart launch detected; Settings window will remain hidden.");
            }

            SetupTrayIcon();
            InitializeNotifications(settings);
            InitializePersonalAssistant();
            StartSecondInstanceActivationListener();
            _ = RunStartupUpdateCheckAsync();
            _ = InitializeAssistantAsync();
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeAssistantAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new Assistant.GeminiCredentialClient().InitializeAsync(timeout.Token);
            AppLog.Info("Native assistant initialized; no provider request made.");
        }
        catch { AppLog.Info("Assistant initialization requires attention; open AI settings."); }
    }

    private SettingsWindow CreateSettingsWindow(AppSettings settings)
    {
        var window = new SettingsWindow(settings, _hud!, _runtime!);
        window.AssistantRequested += OpenAssistant;
        window.MusicRequested += mode => OpenMusic(false, mode);
        ConnectNotificationSettings(window);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
                _settingsWindow = null;
        };
        return window;
    }

    private void OpenSettingsWindow()
    {
        if (_desktop is null || _hud is null || _runtime is null)
            return;

        if (_settingsWindow is { IsVisible: true })
        {
            if (_settingsWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                _settingsWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var settings = SettingsManager.Load();
        _settingsWindow = CreateSettingsWindow(settings);
        _desktop.MainWindow = _settingsWindow;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async void OpenAssistant()
    {
        if (_runtime is null || _assistantOpening || _musicOpening || _memoryOpening) return;
        if (_notificationOpening || _notificationIsland?.IsVisible == true) { _notificationReturn = NotificationReturn.Assistant; return; }
        if (_assistant is { IsVisible: true }) { _assistant.ShowInput(_runtime.EffectiveSettings); return; }
        _assistantOpening = true;
        AppLog.Info("Opening AI island from explicit user action.");
        try
        {
            if (_memoryPalace?.IsVisible == true) { await IslandTransition.FadeOutAsync(_memoryPalace); _memoryPalace.Hide(); }
            if (_music?.IsVisible == true) { await IslandTransition.FadeOutAsync(_music); _music.HideIsland(); }
            if (_assistant is null)
            {
                _assistant = new AssistantIslandWindow(_personalStore);
                _assistant.IslandHidden += RestoreHud;
                _assistant.MusicRequested += () => OpenMusic(false);
                _assistant.MemoryRequested += OpenMemoryPalace;
            }
            await _runtime.BeginAssistantAsync();
            IslandTransition.PrepareShow(_assistant);
            _assistant.ShowInput(_runtime.EffectiveSettings);
            await IslandTransition.FadeInAsync(_assistant);
            AppLog.Info($"AI island shown. Visible={_assistant.IsVisible}, Width={_assistant.Width}, Height={_assistant.Height}, Position={_assistant.Position}.");
        }
        catch (Exception ex) { _runtime.EndAssistant(); AppLog.Error("Unable to open AI island.", ex); }
        finally { _assistantOpening = false; }
    }

    private void RestoreHud()
    {
        if (!_assistantOpening && !_musicOpening && !_memoryOpening && !_notificationOpening && _memoryPalace?.IsVisible != true && _assistant?.IsVisible != true && _music?.IsVisible != true && _notificationIsland?.IsVisible != true)
        {
            if (_notificationReturn != NotificationReturn.None) { ResumeAfterNotification(); return; }
            _runtime?.EndAssistant();
            TryPresentNotification();
        }
    }
    private async void OpenMusic(bool openPlaylist, MusicMode? requestedMode = null)
    {
        if (_runtime is null || _assistantOpening || _musicOpening || _memoryOpening) return;
        if (_notificationOpening || _notificationIsland?.IsVisible == true) { _notificationReturn = NotificationReturn.Music; _notificationReturnMode = requestedMode; _notificationReturnOpenPlaylist = openPlaylist; return; }
        _musicOpening = true;
        try
        {
            if (_memoryPalace?.IsVisible == true) { await IslandTransition.FadeOutAsync(_memoryPalace); _memoryPalace.Hide(); }
            if (_assistant?.IsVisible == true) await _assistant.HideAnimatedAsync();
            if (_music is null)
            {
                _music = new MusicIslandWindow();
                _music.IslandHidden += RestoreHud;
                _music.SettingsRequested += () => { OpenSettingsWindow(); _settingsWindow?.SelectMusicTab(); };
            }
            await _runtime.BeginAssistantAsync();
            IslandTransition.PrepareShow(_music);
            _music.ShowMusic(_runtime.EffectiveSettings);
            await IslandTransition.FadeInAsync(_music);
            AppLog.Info($"Music island shown. Visible={_music.IsVisible}, Width={_music.Width}, Height={_music.Height}, Position={_music.Position}.");
            if(requestedMode==MusicMode.Browser)_music.FollowBrowser();
            else if(openPlaylist||requestedMode==MusicMode.Playlist)_music.OpenSavedPlaylist();
        }
        catch (Exception ex) { _runtime.EndAssistant(); AppLog.Error("Unable to open music island.", ex); }
        finally { _musicOpening = false; }
    }

    private void StartSecondInstanceActivationListener()
    {
        var activationEvent = Program.ActivationEvent;
        if (activationEvent is null) return;

        _activationListenerCts?.Cancel();
        _activationListenerCts?.Dispose();
        _activationListenerCts = new CancellationTokenSource();
        var token = _activationListenerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                bool signaled;
                try
                {
                    signaled = activationEvent.WaitOne(500);
                }
                catch
                {
                    break;
                }

                if (!signaled) continue;

                try
                {
                    await Dispatcher.UIThread.InvokeAsync(OpenSettingsWindow);
                }
                catch
                {
                    // Application may already be shutting down.
                }
            }
        }, token);
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        AppLog.Info("Startup update check started.");
        SettingsWindow.UpdateCheckResult result = await SettingsWindow.CheckForUpdatesAsync();

        AppLog.Info($"Startup update check finished: {result.StatusText}");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_settingsWindow is not null)
                _settingsWindow.ApplyUpdateCheckResult(result);

            if (result.HasUpdate)
            {
                string latest = result.LatestVersionText
                    .Replace("最新版本：", "", StringComparison.Ordinal)
                    .Replace("Latest: ", "", StringComparison.Ordinal);
                _tray?.ShowNotification(
                    LocalizationManager.Text("終末地 靈動島 有新版本", "Endfield Dynamic Island update available"),
                    LocalizationManager.Text($"發現 {latest}。可在“關於”頁面檢視更新狀態。", $"Found {latest}. See About for update details."));
            }
        });
    }

    private void SetupTrayIcon()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            _tray = new WindowsTrayIcon(
                ToggleTrayMenu,
                () =>
                {
                    CloseTrayMenu();
                    OpenSettingsWindow();
                },
                AppBrand.Name, OpenAssistant, () => OpenMusic(false));
            AppLog.Info($"Global hotkeys registered: Alt+A={_tray.AssistantHotkeyRegistered}, Alt+M={_tray.MusicHotkeyRegistered}.");
            if (!_tray.AssistantHotkeyRegistered)
            {
                AppLog.Warn("Alt+A is already registered by another app; use the AI tray/settings entry.");
                _tray.ShowNotification("AI 快捷鍵未註冊", "Alt+A 已被其他程式使用，可從設定或系統匣開啟 AI 靈動島。");
            }
            if (!_tray.MusicHotkeyRegistered)
            {
                AppLog.Warn("Alt+M could not be registered; use the music tray/settings entry.");
                _tray.ShowNotification("音樂快捷鍵未註冊", "Alt+M 無法註冊，可能已被其他程式使用，可從設定或系統匣開啟音樂靈動島。");
            }
        }
        catch (Exception ex)
        {
            // The main application remains usable even if Explorer/tray creation fails.
            AppLog.Error("Failed to create the Windows tray icon.", ex);
            _tray = null;
        }
    }

    private void ToggleTrayMenu()
    {
        if (_trayMenu is { IsVisible: true })
        {
            CloseTrayMenu();
            return;
        }

        var menu = new TrayMenuWindow();
        _trayMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu))
                _trayMenu = null;
        };

        menu.PreviewClicked += () =>
        {
            CloseTrayMenu();
            if (_runtime is not null)
                _ = _runtime.PreviewActiveAsync();
        };

        menu.AssistantClicked += () => { CloseTrayMenu(); OpenAssistant(); };
        menu.MusicClicked += () => { CloseTrayMenu(); OpenMusic(false); };

        menu.SettingsClicked += () =>
        {
            CloseTrayMenu();
            OpenSettingsWindow();
        };

        menu.ExitClicked += () =>
        {
            CloseTrayMenu();
            _desktop?.Shutdown();
        };

        menu.ShowAtTray();
    }

    private void CloseTrayMenu()
    {
        var menu = _trayMenu;
        _trayMenu = null;
        if (menu is not null)
        {
            try { menu.Close(); }
            catch { }
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        AppLog.Info("Desktop lifetime is exiting.");
        DisposeNotifications();
        DisposePersonalAssistant();
        _memoryPalace?.Close();
        _activationListenerCts?.Cancel();
        _activationListenerCts?.Dispose();
        _activationListenerCts = null;
        CloseTrayMenu();
        _tray?.Dispose();
        _tray = null;
        _music?.Close();
        _music = null;
        _assistant?.Close();
        _assistant = null;
        _runtime?.Dispose();
        _runtime = null;
        _hud?.Close();
        _hud = null;
    }
}
