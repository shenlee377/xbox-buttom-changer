GuideDeck 0.5.0 — Xbox Button Changer
2026-10-31
============================================================

目的與目前邊界
保留微軟原生 Guide → Game Bar；研究阻止 Steam 對同一次 Guide 觸發
聚焦、Big Picture 或遊戲 Overlay。此版優先管理 Steam 原生聚焦與 Guide 組合鍵開關，進階頁保留 SDL mapping 工具，
不是已完成的全系統 Guide 防火牆。沒有驅動、程序注入、記憶體修改、
全域鍵盤模擬或強制終止 Steam。不修改 Steam Input／Overlay 啟用開關。

實際效果尚需 Windows＋Steam＋實體手把測試。Steam 可能忽略 mapping、
走其他輸入 API 或重新寫回設定；不會因為 mapping 沒有 guide 欄位就
顯示「已成功攔截」。其他第三方啟動器尚未實作。

下載與安裝
https://github.com/shenlee377/xbox-buttom-changer/releases

建議安裝：下載 GuideDeck-Setup.exe。Windows 設定可解除安裝。
app「更新與安裝」直接從 GitHub Releases 檢查／下載，由使用者確認重啟。
安裝版預設 %LOCALAPPDATA%\GuideDeck.Desktop。解除安裝會清除本機
GuideDeck 資料（含備份／觀察）；需要還原 Steam 修改時請先在 app 操作。
自行匯出、舊版副本、另部署的 widget 與 Windows 記錄不在清理範圍。
舊版首次需安裝 Setup；下方腳本是歷史／選用方式，不具新 updater。
更多：docs/UPDATING-AND-UNINSTALL.txt。

先修正大螢幕啟動：
  在「Steam Guide」頁開啟 Steam → 設定 → 控制器（部分版本在進階設定）。
  關閉 Guide button focuses Steam 與 Enable Guide Button Chords for controllers。
  後者也會停用 Steam Guide 快捷操作，普通 Steam Input 與 Shift+Tab 不改。
  完整離開 Steam 後，可選目前帳號 localconfig.vdf 或安裝 config.vdf，
  檢查／預覽／備份並關閉已存在的已知 0/1 開關；缺少欄位是未知，
  不會自動插入猜測值。帳號與安裝設定可能分開儲存，需分別核對。
  Guide 備份是 *.guide-settings.json，只存修改欄位，不複製整份設定。
  Steam 執行中拒絕寫入；偵測設定寫回後撤銷預覽，局部還原保留其他變更。
  重啟後測試背景／前景／遊戲、單按／連按／長按與 Game Bar／Shift+Tab。
  0.3 的 mapping 修改不足以阻止所有大螢幕路徑，不要反覆套用它。
  依據與限制：docs/STEAM-GUIDE-RESEARCH.txt。

免安裝：下載 GuideDeck-windows-x64.zip，解壓縮後執行 GuideDeck.exe。
內含 .NET runtime，預設不需要管理員權限。

安裝腳本：下載並檢視 Install-GuideDeck.ps1，依自己的 PowerShell
執行原則執行：
  .\Install-GuideDeck.ps1 -Version v0.5.0
  # 選配：安裝後開啟
  .\Install-GuideDeck.ps1 -Version v0.5.0 -Launch

腳本會下載指定 Release 的 ZIP 與 SHA256SUMS.txt、驗證完整性，安裝至
%LOCALAPPDATA%\Programs\GuideDeck 並建立開始功能表捷徑。SHA-256 核對
的是 Release 檔案完整性，不等於已取得發行者數位簽章。本版未簽章，
若組織政策不允許執行，腳本不要求停用安全功能，可改用允許的部署方式。

離線安裝（需同版本 ZIP 與 checksum）：
  .\Install-GuideDeck.ps1 -ArchivePath .\GuideDeck-windows-x64.zip -ChecksumFile .\SHA256SUMS.txt

更新前先關閉 GuideDeck；腳本不會關閉遊戲或 Steam。不覆蓋不是由本
腳本建立的同名資料夾。安裝與更新不會修改 Steam 設定。

移除：執行安裝資料夾的 Uninstall-GuideDeck.ps1。備份、觀察與 Steam
設定保留；若要復原 Guide 修改，請先在程式中還原，再移除程式。

使用流程
1. 偵測 Steam 或選擇 Steam/config/config.vdf。
（以下 mapping 流程是進階實驗功能，不是主要大螢幕修正。）
2. 核對控制器 GUID／CRC，選擇一筆 mapping，閱讀前後差異。
3. 完整離開 Steam，確認實驗性修改，按「備份並套用」。
4. 重新啟動 Steam，在桌面／遊戲中完成驗收。
5. 需要還原時先離開 Steam，選擇 *.backup.json、閱讀還原預覽，再還原。

找不到 mapping：部分 Steam 版本可在「設定 → 控制器 → 測試／設定
裝置輸入」中複製自己的 SDL mapping。貼到工作台預覽後，使用該版本
提供的匯入方式貼回。選項名稱隨版本不同；不支援匯入時不要硬套。
不要使用別人的 GUID，也不要把結果設成全域 SDL 環境變數。

修改範圍
只支援已存在且唯一的：
  "InstallConfigStore" { "SDL_GamepadBind" "<一行或多行 mapping>" }
處理 UTF-8、BOM、LF／CRLF、// 註解與一般巢狀物件；保留其他位元組。
不猜測預設 mapping，不新增未知設定。未知格式、重複欄位、重複
GUID／CRC、非 Windows mapping、未知 Guide 格式會拒絕寫入。

預覽後設定變更會使預覽失效。程式開啟時每 5 秒檢查已選的設定，
偵測到變動後會撤銷過期預覽與未儲存觀察；程式關閉後不做背景監測。
Steam 執行中的寫入／還原會被拒絕。需讓 Steam 在整個修改期間保持
離開；Steam 不遵守本工具鎖檔，不能保證跨程序無競態。

備份與局部還原
0.2 的 schema v2 備份只保存選中 mapping 的修改前後字串及 hash，
不保存 Steam 設定檔的其他內容。資料只留在本機。
還原依 GUID／CRC 找到原 mapping，只有目前內容仍等於本工具套用的
內容時才還原該行；其他設定或其他控制器的後續變更會保留。
若該 mapping 已被重新編排按鍵或移除，會停止還原。已是原始狀態時
不重複寫入。舊版 v1 備份仍可讀取，但同樣只做局部還原。
v1 備份含完整 config.vdf，可能包含私人資料，請勿公開分享。

驗收與歷史
人工觀察分成桌面 Steam 未聚焦、桌面 Steam 聚焦、Steam 遊戲、其他
遊戲。不適用項目需寫明原因；核心項目不能跳過。
紀錄保存當時設定 hash、mapping identity/hash、OS 與填寫的環境。
狀態區分：僅供參考、設定變更需重測、有失敗、尚未測完、此情境
人工驗收通過。只對該次填寫的環境有效，不推論所有遊戲都通過。
改變環境、遊戲、測試情境或控制器會清除未儲存的舊觀察。
「紀錄」頁可查看最近 100 份紀錄並匯出選定 JSON，不含 Steam 原檔。

Game Bar 整合
UWP widget 與 WPF 桌面 companion 打包在同一 app package。
widget 可：
  - 開啟同套件的桌面管理器（可能切出遊戲）。
  - 顯示桌面管理器最近檢查摘要；超過 30 秒標示過期。
  - 使用共用的 mapping 轉換與驗收規則。
  - 將人工觀察存到共用 Reports，供桌面管理器查看／匯出。

widget 不直接讀寫 Steam 原始設定，因此它儲存的觀察不綁定當前設定，
桌面會顯示「未綁定設定，僅供參考」。不把摘要當成實際攔截證明。
widget 不保證在按 Guide 後自動取得焦點；不取代 Game Bar 首頁。
關閉 widget 不影響已寫入的 Steam mapping；無需常駐輸入代理。

儲存位置
獨立桌面版：%LOCALAPPDATA%\GuideDeck
同套件版：%LOCALAPPDATA%\Packages\<package family name>\LocalState\GuideDeck
內含 Backups、Reports、preferences.json、status-summary.json。
獨立版與套件版的資料分開，不自動搬移；widget 原始字串備份另留在
該套件 LocalState。沒有上傳遙測或帳號憑證。

建置／部署
.NET 8 SDK：
  dotnet run --project tests/GuideDeck.Tests -c Release
  .\scripts\build-windows.ps1

Game Bar 額外需要 Visual Studio 2022 UWP 工具與 Windows SDK 19041+
（腳本會選取已安裝的最新 SDK）：
  .\scripts\build-windows.ps1 -IncludeGameBar
腳本先發佈 companion 至 GameBar/Companion，再建置 UWP 專案。
在 Visual Studio 選 Debug/x64，Deploy 該 UWP 專案，然後在 Game Bar
的 Widgets 選單選 GuideDeck。此為開發部署，不是已簽章的商店安裝包。
依自己的開發部署政策操作，不需關閉 Secure Boot 或記憶體完整性。
桌面免安裝 ZIP 不包含 widget 的自動安裝。

GitHub Actions
core：Linux 執行核心測試。
windows：核心測試、桌面發佈、UWP companion 套件建置、桌面啟動／
單一實例測試、離線安裝／移除腳本測試。建置產物保留為 workflow artifact。
Windows CI 仍無法替代實體手把、遊戲與反作弊測試。

CLI
  dotnet run --project src/GuideDeck.Cli -- inspect samples/config.vdf
  dotnet run --project src/GuideDeck.Cli -- preview samples/config.vdf 0 plan.json
  dotnet run --project src/GuideDeck.Cli -- apply plan.json --experimental
  dotnet run --project src/GuideDeck.Cli -- restore <backup.json>
samples 是合成 fixture，不能套用到真手把。CLI apply／restore 僅在 Windows 開放。

實機驗收目標
保持 Steam Input、Steam Overlay、Windows 控制器開啟 Game Bar 啟用。
每個桌面／遊戲情境分別確認：
  Guide 只開 Game Bar；不聚焦 Steam／不進 Big Picture；不開 Steam Overlay。
  遊戲中 Shift+Tab 仍可開 Overlay。
  普通按鍵、搖桿、扳機、震動與需要的額外按鍵正常。
再測 Steam 重啟、手把重接與睡眠喚醒，最後確認還原能恢復原有行為。
未測的項目保持未測試；無法維持其他功能時，這個適配器尚未達標。

依據
SDL mapping 語法（官方）：https://wiki.libsdl.org/SDL3/SDL_AddGamepadMapping
SDL 官方資料庫說明 Steam SDL_GamepadBind 欄位：
https://github.com/libsdl-org/SDL/blob/main/src/joystick/SDL_gamepad_db.h
mapping 工具原始碼佐證，非 Valve 契約：
https://github.com/General-Arcade/sdl2-gamepad-tool/blob/master/src/core/SteamConfig.cpp
移除 Guide 的社群線索，非新版本保證：
https://gist.github.com/rosswd/c0c8b46d1ae5bb4f02207b6195392f93
Valve Overlay／快捷鍵：https://help.steampowered.com/en/faqs/view/3978-072C-18DF-FBF9
Game Bar：https://github.com/microsoft/XboxGameBarSamples
同套件桌面啟動：https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.fulltrustprocesslauncher

0.3 外觀與介面
左側導覽與分步卡片；主要套用／還原固定在設定頁底部。右上角可選
跟隨系統、淺色、深色，即時生效並儲存 appearance.json。高對比優先。
Game Bar 自動模式尊重宿主 RequestedTheme；同套件桌面與 widget 共用
偏好並在執行中更新。一般程式不修改 Windows 的主題登錄值。
CI 另檢查主題色對比、實際 WPF 深淺色渲染與即時系統設定變更。
詳細設計：docs/UI-DESIGN.txt。

圖標：GuideDeck-icons.zip 提供原創手把 SVG／1024 PNG／多尺寸 Windows ICO。
以中央亮色 Guide 鈕為辨識重點，深淺色使用同一圖標，素材授權沿用 MIT。
