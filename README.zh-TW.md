# antiDBT - 獨立即時雙語字幕客戶端

<p align="center">
  <b>專為 Windows 10/11 設計的獨立桌面即時雙語字幕工具</b><br>
  <sub>開源即時字幕客戶端 · 支援本機串流語音辨識與多 Provider 機器翻譯組合</sub>
</p>

<p align="center">
  <a href="README.md"><b>简体中文</b></a> |
  <a href="README.zh-TW.md"><b>繁體中文</b></a> |
  <a href="README.en.md"><b>English</b></a> |
  <a href="README.ja.md"><b>日本語</b></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6.svg?logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4.svg?logo=dotnet" alt=".NET 10.0">
  <img src="https://img.shields.io/badge/Release-v0.5.5-green.svg" alt="Release">
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License">
  <img src="https://img.shields.io/badge/CI-Passing-brightgreen.svg?logo=githubactions" alt="CI">
</p>

---

## 專案簡介

antiDBT 是一個專為 Windows 10/11 設計的獨立桌面即時雙語字幕客戶端。透過 Windows WASAPI 系統音訊回環擷取系統當前播放的聲音，結合低延遲語音辨識（ASR）與機器翻譯（MT）服務，在桌面呈現低延遲、排版自然的雙語對照字幕。

軟體適用於外語影片播放、技術演講、遠端會議、線上課程及外語直播等視聽場景。

---

## 核心特性

### 1. 字幕狀態機與語義排序演進（Phase 5.5.5）
- **動態時間線保序**：針對網路抖動或非同步請求導致的到達亂序問題，採用基於音訊時間錨點的動態排序機制，消除字幕跳變與閃爍。
- **粘性翻譯上下文（Sticky Translation）**：在語音辨識串流修正期間，平滑保留前序句子的有效譯文上下文，避免在新譯文到達前介面閃爍或出現譯文短暫空白。
- **分組修訂一致性隔離**：當語音辨識前綴發生語義合併、拆分或修正時，精確回收過期翻譯請求，確保展示視口與底層數據狀態嚴格同步。
- **智慧分句與段落銜接**：結合聲學靜音閾值與標點斷句演算法，對連續語音自然聚合成段，長停頓時平滑劃分段落。

### 2. 可靠性與穩定性強化
- **停止尾句完整排空**：點擊停止後進入有界排空階段（Bounded Drain），已入隊的音訊片段與對應的終態轉寫及翻譯完整呈現後再安全退出，避免截斷尾句。
- **有時效的有界音訊緩衝**：設置有界緩衝佇列與超時丟棄機制，在網路發生嚴重擁塞時主動丟棄超期音訊塊，避免延遲持續滾雪球。
- **自適應重試與退避**：精準辨識 HTTP 429 限流並遵循服務商返回的 `Retry-After` 標頭，避免無效重複請求與額度消耗。

### 3. 可調半透明懸浮窗
- **0%～100% 背景不透明度調節**：在設定中即時滑動調節背景透明度，立即生效。
- **背景與文字獨立渲染**：即便背景完全透明，字幕文字依然保持高對比實體渲染，清晰易讀。
- **透視與防穿透保護**：支援透過半透明背景看清後方影片畫面；視窗區域保持正常滑鼠事件響應，避免意外穿透點擊後方視窗。

### 4. 視口排版與平滑滾動
- **視口全高自適應**：拉長視窗時歷史字幕區域自動填充可用空間，根據寬度自然折行，文字不溢出、不裁切。
- **像素級平滑滾動**：支援滾輪與觸控板平滑位移。
- **智慧跟隨鎖**：向上滾動瀏覽歷史時自動暫停滾動跟隨；滾動到底部或點擊底部浮標時恢復自動跟隨，新字幕到達時不強行打斷使用者回看。

### 5. 模組化 Provider 架構與本機憑據安全
- **語音辨識（ASR）**：支援內建離線串流辨識（Sherpa-ONNX Zipformer）、Groq Whisper、火山引擎語音 ASR 2.0、Azure Speech SDK 等。
- **機器翻譯（MT）**：支援 DeepL API、OpenAI 相容端點（Groq、Ollama、DeepSeek、GPT 等）、火山引擎翻譯、Azure Translator、Google 翻譯等。
- **本機 DPAPI 加密存儲**：所有 API Key 與敏感配置均經由 Windows DPAPI 基於當前使用者帳戶加密存儲於本機 `data/settings.json`，不上傳雲端。

---

## 本機英文語音辨識（Sherpa-ONNX Zipformer）

正式完整版（`antiDBT-v0.5.5-win-x64-full.zip`）內建了官方驗證的預設本機英文辨識模型：**`sherpa-onnx-streaming-zipformer-en-2023-06-21`**（INT8 量化）。

### 優勢與局限
- **優勢**：
  - 辨識準確率相較舊版 20M 模型有顯著提升，能夠完整轉錄複雜句子與日常語彙。
  - 維持毫秒級串流初態預覽響應速度。
  - 完全在本機 CPU 上運行，記憶體佔用平穩（實際運行約為 260 MB 級別），無需配置 ASR API Key，無雲端呼叫費用。
- **局限**：
  - 本模型為英文專用串流辨識模型，僅支援英文輸入，不支援中文語音辨識。
  - 重度依賴音訊清晰度；複雜背景噪音、重疊人聲或強口音場景下準確率會有所下降。
  - 中文翻譯屬於獨立處理階段，仍需配合配置翻譯服務（如 Groq、DeepL 或本機 Ollama）。

### 本機 ASR 與雲端翻譯的區別
antiDBT 將處理流程嚴格劃分為獨立的兩個階段：
1. **ASR 階段（音訊 -> 文字）**：若選用本機 Sherpa-ONNX，音訊完全在本機 CPU 推理，零網路流量，零 API Key 依賴。
2. **MT 階段（原文 -> 譯文）**：
   - 若來源語言與目標語言均設為 `en`（直通模式），系統不呼叫任何翻譯服務，實現 100% 離線使用。
   - 若目標語言設為 `zh`，系統將辨識出的文字透過 HTTPS 請求發送至配置的翻譯服務。

### 官方模型來源與手動安裝
若使用未攜帶模型的輕量版本，可手動配置模型：
- **官方下載地址**：https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-21.tar.bz2
- **解壓路徑**：解壓後將資料夾置於 `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\`。
- **關鍵檔案清單**：
  - `encoder-epoch-99-avg-1.int8.onnx`
  - `decoder-epoch-99-avg-1.int8.onnx`
  - `joiner-epoch-99-avg-1.int8.onnx`
  - `tokens.txt`

---

## 常用翻譯服務配置

點擊主視窗右上角設定圖示（⚙）打開設定面板進行配置：

### 1. Groq（低延遲大模型翻譯）
1. 前往 https://console.groq.com 獲取 API Key。
2. 翻譯服務選擇 `OpenAiCompatible`。
3. 介面端點填寫：`https://api.groq.com/openai/v1/chat/completions`。
4. 模型名稱填寫：`llama-3.3-70b-versatile` 或 `llama-3.1-8b-instant`。
5. 填入 API Key，來源語言設為 `en`，目標語言設為 `zh`，儲存即可。

### 2. DeepL（專業機器翻譯）
1. 前往 https://www.deepl.com 註冊獲取 API Key（支援 DeepL API Free 與 Pro）。
2. 翻譯服務選擇 `DeepL`。
3. 填入 API Key（程式根據金鑰末尾 `:fx` 自動切換免費版與專業版端點）。
4. 來源語言設為 `en`，目標語言設為 `zh`，儲存即可。

### 3. 本機大模型（Ollama 等）
1. 啟動本機 Ollama 服務（例如運行 `ollama run qwen2.5:7b`）。
2. 在設定中開啟「允許本機回環」。
3. 翻譯服務選擇 `OpenAiCompatible`，端點填寫 `http://127.0.0.1:11434/v1/chat/completions`，填入對應模型名即可完全離線翻譯。

---

## 系統要求

- **作業系統**：Windows 10 64 位元（版本 1903 / 內部版本 18362 及以上）或 Windows 11 64 位元。
- **音訊架構**：支援 WASAPI 回環擷取的音效卡或音訊裝置（需工作在共享模式 Shared Mode）。
- **執行依賴**：發布包內建獨立自包含的 .NET 10 x64 執行階段與 Sherpa-ONNX 原生動態函式庫。**終端使用者無需安裝 .NET SDK、.NET Runtime、Python、Go、CUDA 或 C++ 編譯環境。**

---

## 下載、安裝與升級

### 首次下載與運行
1. 前往 [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases) 下載正式穩定版：
   - 完整包：`antiDBT-v0.5.5-win-x64-full.zip`
   - 校驗檔案：`antiDBT-v0.5.5-win-x64-full.sha256`
2. 使用系統內建解壓縮功能或 7-Zip 將壓縮包解壓至非系統受限目錄（例如 `D:\Tools\antiDBT\`）。
3. 進入解壓生成的 `antiDBT` 資料夾，按兩下 `antiDBT.exe` 啟動。
4. 首次啟動請進入設定確認音訊裝置與 Provider 配置，儲存後點擊「開始」即可。

### 軟體升級
1. 完全退出正在運行的舊版本 antiDBT。
2. 將新版本 ZIP 解壓至新目錄。
3. 若需繼承歷史配置與憑據，可將舊版本目錄下的 `data/settings.json` 複製到新版本的 `data/` 目錄中（前提為在同一台電腦的同一 Windows 使用者下操作，因 DPAPI 綁定使用者帳戶）。請勿在運行狀態下原地覆蓋檔案。

---

## 原始碼建置與測試

```powershell
# 複製儲存庫
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT/prototype

# Release 編譯
dotnet build AntiDbt.sln -c Release

# 執行全套 12 項自動化驗證套件
pwsh -File scripts/verify-all.ps1 -SkipRestore

# 建置正式分發安裝包
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.5"
```

---

## 驗證結論與運行邊界

- 全套 12 套測試（Core、Windows、CloudSpeech、Providers、Domestic、WPF Desktop Smoke、Diagnostics 等）共計 125 項單元及迴歸測試 100% 通過。
- 包含針對 Phase 5.5.5 狀態機一致性、時間線動態保序、粘性翻譯生命週期及高併發請求迴歸。
- **WASAPI 獨占模式限制**：若第三方播放器啟用了 WASAPI 獨占輸出（Exclusive Mode），系統回環錄音無法擷取該音訊串流，需將其切換為共享模式（Shared Mode）。
- **雲端服務穩定性**：用戶端通過了嚴格的契約校驗與錯誤容災測試，但第三方雲端 API 的可用性、網路連線狀況及呼叫額度由各服務商自身決定。

---

## 隱私、授權條款與免責聲明

- **隱私安全**：antiDBT 沒有中央伺服器，不收集任何使用者使用數據、埋點數據或遙測資訊。
- **憑據加密**：所有 API 金鑰均透過 Windows DPAPI 本機加密存儲。
- **開源授權條款**：antiDBT 原始碼採用 [MIT 授權條款](LICENSE) 發布。
- **第三方組件歸屬**：Sherpa-ONNX（Apache-2.0）、ONNX Runtime（MIT）、NAudio（MIT）、Microsoft Speech SDK、Google Cloud Speech 客戶端（Apache-2.0）。詳細說明見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- **免責聲明**：本軟體按「現狀」（AS IS）提供，不包含任何明示或暗示的商業保證。轉錄及翻譯結果僅供參考。
