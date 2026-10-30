GuideDeck 0.1 — Steam Guide mapping 實驗原型
============================================================

目標
保留 Windows 原生 Guide → Game Bar；研究只移除 Steam 控制器 mapping
裡的 Guide 綁定，保留普通按鍵與 Steam Input／Overlay 的啟用設定。

這是可以建置、預覽、寫入與還原的設定適配器原型，不是已驗證的
第三方全域 Guide 防火牆。沒有核心驅動、遊戲／Steam 注入、全域鍵盤
模擬或強制終止 Steam。沒有改動 Windows、Steam Input、Overlay 開關。

目前的限制
1. 尚未在實體 Windows＋Xbox 手把＋Steam 上驗證效果。
2. Steam 可能不走 SDL mapping 路徑、忽略此覆寫，或重新寫回綁定。
   Guide 是否真的不再呼叫第三方，必須完成實機驗收。
3. 只修改已存在、明確可辨識的 mapping，不從別人的 GUID／預設
   mapping 猜測，也不在找不到設定時聲稱成功。
4. 其他啟動器尚未實作；沒有跨程序單鍵過濾。
5. 桌面版與 Game Bar widget 的歷史／備份各自儲存，尚無即時 IPC。
6. 不保證反作弊或防毒相容性；目前方案不接觸遊戲程序。

快速開始：Windows 桌面版
------------------------------------------------------------
如果已取得 GuideDeck-windows-x64.zip：
  解壓縮後執行 GuideDeck.exe。此為未簽章的開發原型，並非安裝套件。
  不需要另行安裝 .NET runtime；無需管理員權限。
  如果 Windows 政策拒絕執行，不要求停用安全功能；可改用本機原始碼建置。

1. 偵測 Steam 或選擇 Steam/config/config.vdf。
2. 若找到支援的 mapping，選擇要測試的控制器並閱讀前後差異。
3. 若沒有 mapping，使用「mapping 工作台」；不要用 samples 的假 GUID。
4. 完整離開 Steam。確認實驗性修改後，點「備份並套用」。
   程式會拒絕在 steam／steamwebhelper 執行中寫入，也不會幫您關閉遊戲。
5. 重新啟動 Steam，完成下方實機驗收。
6. 需要還原時，先離開 Steam，再選取備份。若設定已被您或 Steam
   後續修改，程式不會覆蓋它；原始資料仍在備份 JSON 的 OriginalBase64。

找不到 mapping 時
------------------------------------------------------------
部分 Steam 版本可在「設定 → 控制器 → 測試／設定裝置輸入」中匯出／
複製 mapping。名稱隨版本不同；先保存自己控制器的原始 mapping。
將一行字串貼到工作台，按「預覽移除 Guide」。若產生結果，保留原始
字串，再依該 Steam 版本的匯入方式貼回。若客戶端沒有匯入功能，
這個途徑不能直接使用，不要把結果設成全域 SDL 環境變數。

直接寫檔只支援：
  "InstallConfigStore" { "SDL_GamepadBind" "<一行或多行 mapping>" }
允許一般巢狀物件、// 註解、UTF-8、UTF-8 BOM、LF／CRLF。
只在選中那行刪除一個 guide:bN 欄位；GUID、CRC、順序、名稱、額外
按鍵和其他 mapping 保留。其他所有檔案位元組保持原樣。
未知格式、重複 identity、跳脫 mapping 或任一不明 mapping 會拒絕寫入。
缺少 guide 欄位不代表 Steam 不會從其他 API 讀到 Guide。

安全與復原範圍
------------------------------------------------------------
預覽唯讀；套用重新計算差異並核對 SHA-256。備份先寫入並 flush，
修改採同資料夾暫存檔後替換，再讀回驗證。鎖檔防止本工具同時寫入。
寫入前再次檢查 Steam 與來源內容；但 Steam 並不遵守本工具的鎖檔，
因此使用者仍需讓 Steam 在整個操作期間保持離開。這不是跨程序交易保證。
還原只接受有效的 Guide 修改紀錄，且目前完整檔案 hash 必須仍等於
套用後 hash。設定有漂移時拒絕自動還原，避免抹掉後續偏好。

一般位置（不會上傳）
  桌面備份：%LOCALAPPDATA%\GuideDeck\Backups
  桌面觀察：%LOCALAPPDATA%\GuideDeck\Reports
  widget 資料：%LOCALAPPDATA%\Packages\<widget PFN>\LocalState
備份包含完整 config.vdf，可能包含私人資料；不應公開分享。
本工具不讀取 loginusers.vdf、帳號憑證或遊戲記憶體。
刪除程式不會自動還原設定，請先在 UI 還原需要的 mapping。

Game Bar widget
------------------------------------------------------------
src/GuideDeck.GameBar 是可由 Visual Studio 建置的 UWP widget 專案。
其介面包含：狀態、mapping 工作台、備份／複製原始 mapping、遊戲驗收。
與桌面版共用 Mapping.cs；不需要背景 Broker 就能處理剪貼簿工作流。
Game Bar 的設定圖示會切換至 mapping 頁。
原型停用釘選，避免在遊戲中持續顯示可操作的設定。

Windows 建置需求：
  Visual Studio 2022，UWP 開發工具，Windows SDK 10.0.19041.0，.NET 8 SDK。
  UWP 專案使用 Microsoft.Gaming.XboxGameBar 7.2.240903001。
  此專案尚未在 Windows 的 UWP XAML／AppX 工具鏈上完成建置與執行驗證。

部署：
  1. 在 Visual Studio 開啟 src/GuideDeck.GameBar/GuideDeck.GameBar.csproj。
  2. 選 Debug / x64，還原 NuGet，Build，再 Deploy。
     若開發部署需要系統開發設定，請依自己的 Windows 政策處理；
     不必關閉 Secure Boot 或記憶體完整性。
  3. 開啟 Game Bar 的 Widgets 選單，選 GuideDeck。
     Guide 不保證直接聚焦此 widget；它不是取代 Game Bar 首頁。
  4. 測試一般啟動、Game Bar 啟動、重複啟動、設定圖示、關閉與重新開啟。

桌面版採 WPF，以便先交付可建置的測試工具。Game Bar widget 採官方
UWP XAML 技術；核心沒有綁定 WPF，可另接 WinUI 3，不須重寫修改邏輯。

從原始碼建置與測試
------------------------------------------------------------
在 GuideDeck 目錄：
  dotnet run --project tests/GuideDeck.Tests -c Release
  dotnet build src/GuideDeck.Desktop -c Release
  dotnet run --project src/GuideDeck.Desktop

獨立 Windows x64 桌面版：
  powershell -File scripts/build-windows.ps1
包含 widget 的 Windows 建置檢查：
  powershell -File scripts/build-windows.ps1 -IncludeGameBar
（Game Bar 部分需要 Visual Studio MSBuild／UWP 工具；腳本不自動安裝或部署。）

CLI：
  dotnet run --project src/GuideDeck.Cli -- inspect samples/config.vdf
  dotnet run --project src/GuideDeck.Cli -- preview samples/config.vdf 0 plan.json
  dotnet run --project src/GuideDeck.Cli -- apply plan.json --experimental
  dotnet run --project src/GuideDeck.Cli -- restore <backup.json>
apply／restore CLI 僅 Windows 開放；samples 是合成測試資料，不能套用到手把。
preview 與 inspect 可在 Linux 執行。

實機驗收：每個環境個別記錄
------------------------------------------------------------
先確認原本會發生問題，記錄 OS build、Steam build、手把、連接方式。
保持 Steam Input 與 Steam Overlay 啟用，Windows 控制器開啟 Game Bar 啟用。
分別測：桌面 Steam 未聚焦、桌面 Steam 聚焦、Steam 遊戲、非 Steam 遊戲。
每種情境記錄：
  - Guide 是否只開 Game Bar。
  - Steam 是否未聚焦／未進 Big Picture。
  - Guide 是否未開 Steam Overlay。
  - 在遊戲內 Shift+Tab 是否仍能開 Overlay。
  - 普通按鍵、搖桿、扳機、震動、額外按鍵是否正常。
未測的項目維持「未測試」，不要填成成功。
完成後測 Steam 重啟、手把重接、睡眠喚醒；再測還原是否恢復原來行為。
UI 記錄的是人工觀察，沒有自動判斷其他程序是否吃掉 Guide。

專案結構
  src/GuideDeck.Core       mapping／VDF 解析、精確修改、交易與還原
  src/GuideDeck.Cli        檢查、產生預覽、套用、還原
  src/GuideDeck.Desktop    Windows 原型操作介面
  src/GuideDeck.GameBar    UWP widget 原型，與核心共用 mapping 轉換
  tests/GuideDeck.Tests    無外部測試套件的可執行回歸測試
  samples/config.vdf      合成 fixture

研究依據與證據強度
------------------------------------------------------------
SDL mapping 語法（官方）：
https://wiki.libsdl.org/SDL3/SDL_AddGamepadMapping
SDL 官方資料庫說明 Steam SDL_GamepadBind 欄位：
https://github.com/libsdl-org/SDL/blob/main/src/joystick/SDL_gamepad_db.h
另一個 mapping 工具的實作佐證（工具作者的原始碼，非 Valve 契約）：
https://github.com/General-Arcade/sdl2-gamepad-tool/blob/master/src/core/SteamConfig.cpp
Steam 移除 guide mapping 的社群實測線索（非官方、不能證明新版本有效）：
https://gist.github.com/rosswd/c0c8b46d1ae5bb4f02207b6195392f93
Valve 的 Overlay 與快捷鍵說明：
https://help.steampowered.com/en/faqs/view/3978-072C-18DF-FBF9
Game Bar widget 官方範例與接口：
https://github.com/microsoft/XboxGameBarSamples
https://learn.microsoft.com/en-us/xbox/game-bar/api/xgb-widget

目前設定 adapter 的 Steam 行為相容性：實驗性、未實機驗證。
程式測試通過與編譯成功，不等於已完成上述實機驗收。
