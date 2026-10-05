# 設定視窗設計 / Settings window design

v0.26.0，2026-10-05。設計限於設定窗；不重新設計 HUD、音樂島、AI 島或通知島。

## 美術轉譯

閱讀使用者提供的《終末地》通用美術參考包 V2：總覽、ART_DIRECTION_GENERAL、TYPOGRAPHY_COLOR_FRAME_MICROCOPY、G01 角色資料畫面及 W02 官方角色頁。取用疏密、對齐、線重、中性色與訊號色的關係；不打包參考畫面、角色、官網字型或整套版面。配合 ui-ux-pro-max 的介面審查與工業／Swiss Style 規則。

- 構圖：深色側欄與銀灰設定工作區形成主從；六個真實模組使用 01–06 索引。
- 字階：章節名稱 30 px、設定分組 16 px、一般欄位 12–15 px；較淡的大編號是重複章節提示，資訊仍由側欄與標題傳達。
- 線重：設定面板與分隔 1 px，選取導覽 3 px；不把每個角都變形或加入無功能裝飾。
- 色彩：石墨灰、銀灰及紙白作基底，青藍導覽與深藍綠欄位提示，黃色集中於主要操作。
- 內容：AI 連線與用量優先於頭像；音樂的清單控制與瀏覽器跟隨分成不同設定區。
- 視窗：56 px 整合標頭，原生可縮放邊框保留；最小化／最大化／還原／關閉有雙語操作名稱，標頭支援拖曳及雙擊。

不新增後端、模型、字型下載、大型套件或常駐效果。SettingsTheme.axaml 僅載入設定窗，顏色不傳入其他島。HudCustomizerView 是設定窗內的編輯器，本版僅修改它的 XAML 外觀，採樣與預覽邏輯不變。

## 驗證範圍

SettingsChromeApplication 用獨立程序、預設設定及不寫入偏好的 callback 執行。六分頁逐一驗證中英、1120／940／720 邏輯像素寬、設定欄位邊界、工具列與視窗控制。另輸出 150%／200% RenderTargetBitmap；這是縮放渲染驗證，不代表所有實體混合 DPI 顯示器都經過手測。420／640／940／1120 工具列離線量測由既有 AllUiLayoutProbe 保留。

UI 控制建立於既有 Avalonia 11.2.1；開關與滑桿的樣式依 [官方 ToggleSwitch](https://github.com/AvaloniaUI/Avalonia/blob/11.2.1/src/Avalonia.Themes.Fluent/Controls/ToggleSwitch.xaml) 與 [Slider](https://github.com/AvaloniaUI/Avalonia/blob/11.2.1/src/Avalonia.Themes.Fluent/Controls/Slider.xaml) 的模板節點設定。只覆寫設定窗的視覺狀態，仍保留原生互動。

本輪先後修正：窄窗按鈕越界、布局回呼重入、中英文字較長造成工具列分行、英文更新按鈕截字，以及使用者不接受的裝飾格紋。最終執行結果在 VALIDATION.md；失敗階段不算通過結果。
