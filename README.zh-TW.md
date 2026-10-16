# antiDBT · 獨立即時雙語字幕用戶端

<p align="center">
  <b>極簡 · 低延遲 · 高度自定義的 Windows 桌面即時雙語字幕工具</b><br>
  <sub>獨立開源復刻專案 · 不依賴任何特定廠商私有介面 · 支援多 Provider 自由組合</sub>
</p>

<p align="center">
  <a href="README.md"><b>简体中文</b></a> |
  <a href="README.zh-TW.md"><b>繁體中文</b></a> |
  <a href="README.en.md"><b>English</b></a> |
  <a href="README.ja.md"><b>日本語</b></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6.svg?logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4.svg?logo=dotnet" alt=".NET 9.0">
  <img src="https://img.shields.io/badge/Release-v0.5.3--beta-green.svg" alt="Release">
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License">
  <img src="https://img.shields.io/badge/CI-Passing-brightgreen.svg?logo=githubactions" alt="CI">
</p>

---

## 📖 專案簡介

**antiDBT** 是一個專為 Windows 10/11 設計與實現的獨立桌面即時雙語字幕浮動視窗用戶端。透過 Windows WASAPI 系統音訊回環擷取電腦正在播放的聲音，結合低延遲語音辨識（ASR）與機器翻譯（MT）服務，在螢幕上呈現純淨、低延遲、自然排版的雙語對照字幕。

本專案採用純淨室獨立開發路線，**不使用豆包官方的專有程式碼、非公開通訊協定或憑證**，專為生肉影片觀看、外語直播翻譯、跨國遠端會議、線上課程學習等高頻視聽場景打造。

---

## ✨ 核心特性

### 1. 🛡️ 可靠性與穩定性硬化 (Phase 5.3 專項演進)
- **停止尾句零遺失**：點擊「停止」時進入有界排空期（Bounded Drain），已入列的音訊片段與延遲 Final 轉寫及對應翻譯完整呈現後再安全關閉，徹底杜絕遺失最後一句話。
- **音訊時效性有界緩衝與超期丟棄**：引入名義容量為 50 個音訊區塊（每塊 6,400 位元組，在 16kHz/16-bit 單聲道下對應 200ms 音訊，佇列名義容量約 10 秒）的有界緩衝區與 2.0 秒音訊區塊過期清理閾值；發生網路瞬態擁塞導致積壓時，超期音訊區塊自動丟棄並記錄中斷統計，杜絕延遲無限滾雪球。
- **幽靈譯文清除與草稿相容校驗**：當 ASR 將原文修正為空或發生語意顛覆時，強制清空失效的歷史譯文；建立嚴格的前綴擴展與修訂版本隔離校驗，遲到舊結果無法污染當前字幕。
- **韓文詞間空格精準保護**：徹底修復多音節韓文片段在流式流轉中詞間空格被誤刪的問題，韓語聽譯語意清晰連貫。
- **連續穩定重試預算與限流退避**：單次發送音訊成功不再清空重試預算，只有建立連線後持續健康收發才重設預算；自動識別 HTTP 429 並嚴格遵循服務商回傳的 `Retry-After` 冷卻期，杜绝無效重試與多餘扣費。
- **細分狀態指示與靜音診斷**：
  - 🟢 聆聽中 / 🟡 連線中 / 🔴 異常 / ⚪ 就緒。
  - 翻譯服務異常但辨識正常時展示琥珀色指示（#FFB300），保持「停止」按鈕語意正常可用。
  - 音訊連續靜音超過 3.0s 明確診斷為「已連線但無音訊訊號」，絕不將設備靜音誤判為網路斷線。

### 2. 🎨 真正的可調半透明懸浮視窗 (Phase 5.2 核心特性)
- **0%～100% 背景不透明度自由調節**：在「設定 → 外觀」中透過滑桿隨時調整，免重啟無感知即時生效。
- **背景與文字徹底解耦**：原文、譯文、按鈕及圖示始終保持 100% 實體高對比度清晰渲染，告別全域模糊。
- **真實透視與防點擊穿透**：12px 現代平滑圓角，透過背景看清後方影片；即便調至 0% 透明度，底層滑鼠命中測試依然有效，絕不意外穿透誤觸後方視窗。

### 3. 📐 彈性視口與空間全高自我適應
- **縱向全高動態填滿**：視窗向下拉長時，歷史字幕區域全高自動填滿，大螢幕可瀏覽更多歷史上下文，消除無意義留白死區。
- **橫向自然折行重排**：橫向拖動縮放時，原文與譯文依據最新視口寬度自然折行，文字絕不溢出或裁切。
- **「↓ 回到底部」智慧錨定**：浮動按鈕精準錨定於字幕視口右下方（Overlay），不再隨視窗高度漂移至視窗中央。

### 4. 📜 絲滑像素級滾動與智慧跟隨
- **像素級滾動平滑阻尼**：支援滑鼠滾輪與筆記型電腦觸控板微小 Delta 絲滑位移，消除跳頁頓挫感。
- **智慧跟隨鎖**：向上滾動翻閱歷史時**自動暫停滾動跟隨**；回到最底或點擊「↓ 回到底部」時**自動恢復跟隨**。新字幕到達或視窗縮放時**絕不強拉底部**，保障回看體驗不受干擾。

### 5. ⚙️ 非侵入式設定面板與端點安全
- **無感設定體驗**：開啟或取消設定面板**不停止音訊擷取、不清空歷史字幕**；僅改變外觀參數原地生效；僅在修改核心擷取參數時執行單次受控平滑重啟。
- **本地大模型安全接入**：在顯式啟用本地回環允許時，支援直接接入本地部署的 Ollama、LocalAI、LM Studio 或 OpenAI 相容端點（`http://127.0.0.1` / `localhost`）；針對外部雲端端點嚴格要求 HTTPS 並拒絕攜帶敏感 userinfo。

### 6. 📊 細分延遲度量模型 (Telemetry)
- 內建輕量級執行緒安全延遲度量收集器，細分記錄 `QueueWaitMs`（佇列等待）、`AsrServiceMs`（語音辨識耗時）、`MtDebounceWaitMs`（去抖等待）、`MtServiceMs`（翻譯耗時）及 `UiApplyMs`（介面呈現耗時）。
- 一鍵脫敏匯出 JSON 診斷樣例，預設嚴禁包含任何金鑰、Token、請求簽名、原文文字、譯文或原始音訊。

### 7. 🔌 模組化 Provider 架構
- **語音辨識（ASR）**：支援 Groq Whisper（超低延遲）、火山語音 ASR 2.0、Azure Speech SDK、Google Cloud Speech V2 等。
- **機器翻譯（MT）**：支援 DeepL API、OpenAI 相容大模型（Ollama、DeepSeek、GPT-4o、Claude 等）、Groq LLM、火山 MT、Azure Translator、Google Basic、百度翻譯、阿里雲翻譯等。

### 8. 🔒 本地憑證安全
- 所有 API Key、Secret、密碼均經由 **Windows DPAPI（Data Protection API）** 採用當前使用者專屬加密金鑰儲存於本地 `data/settings.json`，絕不上雲、絕不向第三方洩漏。

---

## 🖥️ 執行環境需求

- **作業系統**：Windows 10 / Windows 11 (x64)
- **音訊架構**：支援 WASAPI 回環擷取的音效卡/音訊設備（系統預設喇叭/耳機）
- **便攜包執行階段**：發布包內建獨立自包含 .NET 9 執行階段，**終端使用者無需單獨安裝 .NET SDK 或執行環境**。.NET 10 LTS 處於獨立分支評估與適配階段。

---

## 🚀 快速上手

### 1. 下載與執行
1. 前往 [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases) 下載最新版本的 `antiDBT-v0.5.3-beta-win-x64.zip`。
2. 解壓縮封包到本機任意目錄（例如 `D:\Tools\antiDBT\`）。
3. 按兩下解壓縮目錄中的 `antiDBT.exe` 即可啟動。

### 2. 設定翻譯與語音辨識服務
1. 點擊視窗右上角的設定圖示 **⚙**。
2. **語音辨識**：選擇希望使用的 Provider（推薦 `GroqWhisper` 或 `VolcengineAsr2`），輸入 API Key。
3. **翻譯服務**：選擇翻譯 Provider（推薦 `DeepL` 或 `OpenAiCompatible` 並配合高速模型），填入 API Key 與端點。
4. **外觀設定**：可切換「深色 / 淺色 / 跟隨系統」主題，並拖曳**「背景不透明度」**滑桿調整懸浮視窗透明度（建議 70%～85%）。
5. 點擊「儲存設定」。

### 3. 開始使用
- 點擊工具列的 **「開始」** 按鈕。
- 電腦播放任何影片、直播、播客、會議或線上課程音訊，主視窗即可即時浮現雙語字幕。
- 想要體驗排版而暫不消耗 API 額度？在主視窗按右鍵選擇**「本地展示」**，將以本地合成事件測試排版與滾動。

---

## 🛠️ 原始碼建置與測試

本專案採用 C# 13 與 .NET 9 WPF 編寫，已配置 `global.json` 鎖定 SDK 9.0.319 以實現確定性建置。

### 相依環境
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (9.0.319 推薦)
- Windows 10/11 x64
- PowerShell 7 (`pwsh`)

### 建置指令
```powershell
# 複製儲存庫
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT

# 編譯整個處理解決方案 (Release)
dotnet build AntiDbt.sln -c Release

# 執行全量 12 項自動化驗證套件 (涵蓋單元測試、通訊協定契約、WPF UI自動化、效能基準)
pwsh -File scripts/verify-all.ps1 -SkipRestore

# 打包建置 self-contained 單一檔案自包含便攜包
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.3-beta"
```

---

## ⚠️ 當前已知限制與測試邊界說明

1. **雲端憑證依賴**：實際語音辨識與機器翻譯需要使用者自行在對應廠商平台申請 API Key（如 Groq、DeepL、火山引擎、OpenAI 等）。
2. **測試與真實服務邊界**：本機全量迴歸測試與 CI 煙測通過（100% PASS）驗證了核心狀態機、本機音訊回環、UI 契約及錯誤復原邏輯的正確性，**不等於各第三方雲端服務商的實際線上可用性、額度健康度或全天候長期不中斷運行保證**。實際線上體驗依賴於所選雲端服務的網路環境與廠商伺服器端狀態。
3. **WASAPI 獨佔音訊限制**：若播放軟體（如專業音訊 DAW 或特定播放器）啟用了 WASAPI 獨佔輸出模式（Exclusive Mode），系統回環錄音可能無法擷取，請切換為共用模式（Shared Mode）。
4. **防火牆設定**：首次執行如有 Windows Defender 防火牆網路提示，請點擊允許，以便向所設定的 API 發起網路請求。

---

## 🛡️ 隱私與資料安全說明

- **零資料回傳與遙測**：antiDBT 沒有設定任何專有的中心伺服器，不包含任何資料埋點、使用者追蹤或背景遙測。
- **純點對點請求**：擷取到的音訊片段和辨識文字僅直接發送至**使用者在設定中明確指定的第三方 API 端點**。
- **本機憑證加密**：所有敏感參數透過 Windows DPAPI 綁定當前登入使用者的 Windows 帳號加密儲存於本機，即使複製了 `settings.json` 也無法在其他機器或使用者下解密。

---

## ⚖️ 免責與權利聲明 (Disclaimer)

1. **獨立開源聲明**：**antiDBT** 是一個獨立開發的技術研究與輔助工具開源專案，並非北京字節跳動科技有限公司或「豆包（Doubao）」官方產品，與上述實體無任何隸屬、合作、關聯或背書關係。
2. **純淨實現**：本專案所有業務程式碼、介面佈局、資料流管線均為獨立從零編寫，未提取或包含任何閉源私有二進位庫、專有資源檔案或私有憑證。
3. **使用規範**：使用者使用本軟體呼叫第三方 API 時產生的網路流量、費用及對各服務商服務條款（ToS）的遵守，由使用者自行承擔。

---

## 📄 開源授權條款

本專案原始碼基於 [MIT License](LICENSE) 條款開源。第三方開源相依性與聲明請參閱 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
