#ifndef PayloadDir
  #error PayloadDir must point to the self-contained publish directory.
#endif
#ifndef AppVersion
  #define AppVersion "0.28.7"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef SetupAppId
  #define SetupAppId "357B1D2D-A547-4E97-A759-E1D814BF9023"
#endif
#ifndef AppMutexName
  #define AppMutexName "Local\EndfieldChargePlus.SingleInstance"
#endif

#ifndef AppGroupName
  #define AppGroupName "終末地 靈動島"
#endif
#ifndef InstallerMutex
  #define InstallerMutex "EndfieldDynamicIslandSetup"
#endif

[Setup]
AppId={{{#SetupAppId}}
AppName=終末地 靈動島
AppVersion={#AppVersion}
AppVerName=終末地 靈動島 {#AppVersion}
AppPublisher=GlacierGlimmer_冰川雪貓 / OverGreen996
AppPublisherURL=https://github.com/OverGreen996/Endfield-Dynamic-Island
AppSupportURL=https://github.com/OverGreen996/Endfield-Dynamic-Island/issues
AppUpdatesURL=https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest
DefaultDirName={localappdata}\Programs\EndfieldDynamicIsland
DefaultGroupName={#AppGroupName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
AppMutex={#AppMutexName}
SetupMutex={#InstallerMutex}
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\EndfieldChargePlus.exe
UninstallDisplayName=終末地 靈動島
SetupIconFile=..\src\EndfieldIsland\Assets\tray_bolt.ico
OutputDir={#OutputDir}
OutputBaseFilename=Endfield-Dynamic-Island-Setup-v{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern stellar includetitlebar hidebevels
WizardSizePercent=125,125
WizardBackColor=#191D1D
WizardBackImageFile=assets\background.png
WizardImageFile=assets\sidebar.png
WizardSmallImageFile=assets\header.png
DisableWelcomePage=no
DisableReadyPage=no
DisableDirPage=no
UsePreviousLanguage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
Uninstallable=yes
UninstallLogging=yes
VersionInfoDescription=終末地 靈動島 Windows 安裝程式

[Languages]
Name: "zhTW"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
zhTW.WelcomeLabel1=讓桌面，成為你的控制中心。
zhTW.WelcomeLabel2=安裝終末地 靈動島。%n%nAI 對話、音樂、通知與效能資訊，收進同一座島。%n%n既有設定、對話、記憶與用量會保留。AI 與搜尋內建於主程式，不需 Node 或另外啟動服務；搜尋使用 Exa、Tavily、Firecrawl，獨立保存金鑰設定與輪替紀錄。%n%n繼續前請從系統匣退出靈動島。
zhTW.FinishedHeadingLabel=桌面系統已就緒。
zhTW.FinishedLabel=終末地 靈動島已完成安裝。%n%nAlt+A：AI 助理　Alt+M：音樂島%n%nAI：到設定貼上自己的金鑰並確認免費方案。金鑰與用量資料會保留；搜尋：右鍵 → 搜尋 API 與輪替，填入自己的金鑰。
zhTW.ButtonNext=繼續(&N)  →
zhTW.ButtonInstall=開始安裝(&I)  →
zhTW.ButtonFinish=完成(&F)
en.WelcomeLabel1=Your desktop. One control center.
en.WelcomeLabel2=Install Endfield Dynamic Island.%n%nConversations, music, notifications and system telemetry, in one island.%n%nYour settings, conversations, memories and usage are preserved. AI and search run inside the app; no Node or separate service is required. Search uses independent Exa, Tavily and Firecrawl settings and rotation.%n%nExit the island from its tray menu before continuing.
en.FinishedHeadingLabel=Your desktop system is ready.
en.FinishedLabel=Endfield Dynamic Island is installed.%n%nAlt+A: AI assistant    Alt+M: Music%n%nAI: paste your own key in Settings and confirm the free tier. Your key and usage data are preserved; configure your own search keys under Search API & rotation.

[CustomMessages]
zhTW.DesktopShortcut=建立桌面捷徑
zhTW.LaunchIsland=開啟終末地 靈動島
zhTW.PhaseSelect=01 / 部署設定
zhTW.PhaseReady=02 / 確認部署
zhTW.PhaseInstall=03 / 寫入程式
zhTW.PhaseDone=04 / 桌面系統就緒
zhTW.PhaseWelcome=00 / 系統接入
en.DesktopShortcut=Create a desktop shortcut
en.LaunchIsland=Open Endfield Dynamic Island
en.PhaseSelect=01 / CONFIGURE
en.PhaseReady=02 / REVIEW
en.PhaseInstall=03 / DEPLOY
en.PhaseDone=04 / SYSTEM READY
en.PhaseWelcome=00 / CONNECT

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,Start.ps1"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\終末地 靈動島"; Filename: "{app}\EndfieldChargePlus.exe"; WorkingDir: "{app}"
Name: "{group}\解除安裝 終末地 靈動島"; Filename: "{uninstallexe}"
Name: "{userdesktop}\終末地 靈動島"; Filename: "{app}\EndfieldChargePlus.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\EndfieldChargePlus.exe"; Description: "{cm:LaunchIsland}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PhaseLabel, SystemLabel, ReadySummary: TNewStaticText;


procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Font.Size := 22;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.WelcomeLabel1.Height := ScaleY(80);
  WizardForm.WelcomeLabel2.Top := WizardForm.WelcomeLabel1.Top + ScaleY(96);
  WizardForm.WelcomeLabel2.Height := WizardForm.WelcomePage.Height - WizardForm.WelcomeLabel2.Top - ScaleY(24);
  WizardForm.FinishedHeadingLabel.Font.Size := 22;
  WizardForm.FinishedHeadingLabel.Height := ScaleY(72);
  WizardForm.FinishedLabel.Top := WizardForm.FinishedHeadingLabel.Top + ScaleY(86);
  WizardForm.FinishedLabel.Height := ScaleY(154);
  WizardForm.PageNameLabel.Font.Size := 15;
  WizardForm.PageNameLabel.Height := ScaleY(28);
  WizardForm.PageDescriptionLabel.Top := ScaleY(46);
  WizardForm.WizardSmallBitmapImage.Width := ScaleX(132);
  WizardForm.WizardSmallBitmapImage.Left := WizardForm.MainPanel.Width - ScaleX(147);
  PhaseLabel := TNewStaticText.Create(WizardForm);
  PhaseLabel.Parent := WizardForm;
  PhaseLabel.SetBounds(ScaleX(22), WizardForm.NextButton.Top - ScaleY(2), ScaleX(225), ScaleY(14));
  PhaseLabel.Font.Size := 9;
  PhaseLabel.Font.Color := $EBC813;
  SystemLabel := TNewStaticText.Create(WizardForm);
  SystemLabel.Parent := WizardForm;
  SystemLabel.SetBounds(ScaleX(22), PhaseLabel.Top + ScaleY(16), ScaleX(225), ScaleY(10));
  SystemLabel.Caption := 'ENDFIELD / WINDOWS X64 / {#AppVersion}';
  SystemLabel.Font.Size := 7;
  SystemLabel.Font.Color := $B5BBB4;
  { A plain summary stays readable on the industrial background instead of
    inheriting the style's large pale-blue memo surface. }
  ReadySummary := TNewStaticText.Create(WizardForm);
  ReadySummary.Parent := WizardForm.ReadyPage;
  ReadySummary.SetBounds(WizardForm.ReadyMemo.Left, WizardForm.ReadyMemo.Top,
    WizardForm.ReadyMemo.Width, WizardForm.ReadyMemo.Height);
  ReadySummary.AutoSize := False;
  ReadySummary.WordWrap := True;
  ReadySummary.Font.Size := 11;
  WizardForm.ReadyMemo.Visible := False;
end;

function ReadableSummary(Text: String): String;
var I, Column: Integer;
begin
  { Long custom paths are display-only; keep the installation path untouched. }
  Result := ''; Column := 0;
  for I := 1 to Length(Text) do begin
    Result := Result + Text[I];
    if Text[I] = #10 then Column := 0
    else if Text[I] <> #13 then begin
      Column := Column + 1;
      if (Column >= 64) or ((Column >= 45) and (Text[I] = '\')) then begin
        Result := Result + #13#10; Column := 0;
      end;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then PhaseLabel.Caption := CustomMessage('PhaseWelcome')
  else if CurPageID = wpReady then PhaseLabel.Caption := CustomMessage('PhaseReady')
  else if CurPageID = wpInstalling then PhaseLabel.Caption := CustomMessage('PhaseInstall')
  else if CurPageID = wpFinished then PhaseLabel.Caption := CustomMessage('PhaseDone')
  else PhaseLabel.Caption := CustomMessage('PhaseSelect');
  if CurPageID = wpReady then ReadySummary.Caption := ReadableSummary(WizardForm.ReadyMemo.Lines.Text);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var OldCommand: String;
begin
  { Preserve startup enablement when migrating from a previous location.
    Never create a startup preference for a user who has not enabled it. }
  if CurStep = ssPostInstall then begin
#ifndef VerificationBuild
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Endfield Charge Plus', OldCommand) then
      if (Pos('EndfieldChargePlus.exe', OldCommand) > 0) and (Pos('--autostart', OldCommand) > 0) then
        RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Endfield Charge Plus', '"' + ExpandConstant('{app}\EndfieldChargePlus.exe') + '" --autostart');
#endif
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  { Only remove the Run entry owned by this installation. User data is never deleted. }
#ifndef VerificationBuild
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Endfield Charge Plus', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\EndfieldChargePlus.exe') + '" --autostart') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Endfield Charge Plus');
#endif
end;
