# antiDBT - スタンドアロンリアルタイム二言語字幕クライアント

<p align="center">
  <b>Windows 10/11 向けスタンドアロン・デスクトップリアルタイム二言語字幕ツール</b><br>
  <sub>オープンソースリアルタイム字幕クライアント · ローカルストリーミング音声認識と複数翻訳プロバイダーに対応</sub>
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

## 概要

antiDBT は、Windows 10 および 11 向けに設計された独立したリアルタイム二言語字幕フローティングオーバーレイクライアントです。Windows WASAPI システムオーディオループバックを利用して現在再生中の音声をキャプチャし、低遅延音声認識（ASR）および機械翻訳（MT）サービスを組み合わせて、画面上に低遅延で自然な二言語対照字幕を表示します。

外国語の動画視聴、技術カンファレンス、国際オンライン会議、ウェビナー、生配信などの視聴シナリオに適しています。

---

## 主な機能

### 1. 字幕ステートマシンとセマンティック順序制御（Phase 5.5.5）
- **動的タイムライン順序制御**：ネットワーク遅延や非同期処理によるパケット到着順の乱れを、音声タイムアンカーに基づく動的ソートで解決し、字幕のちらつきやジャンプを排除。
- **スティッキー翻訳コンテキスト（Sticky Translation）**：ストリーミング音声認識の修正中も前文の有効な訳文コンテキストを滑らかに保持し、新しい確定結果が届くまでの空白期間を防止。
- **グループリビジョン整合性**：プレフィックスの結合・分割・修正時に古い翻訳リクエストを確実に無効化し、表示領域と基底状態の整合性を保証。
- **高精度な文分割と段落統合**：無音区間検出と句読点アルゴリズムを組み合わせ、連続した音声を自然な段落に統合。

### 2. 信頼性と安定性の強化
- **停止時の末尾文完全ドレイン**：停止ボタン押下後、キューに入った音声チャンク、遅延確定テキスト、対応する翻訳を完全にレンダリングしてから安全に終了。
- **有界オーディオバッファと期限切れ破棄**：ネットワーク輻輳時に過期オーディオブロックを自動破棄し、遅延の累積を防止。
- **適応型レート制限バックオフ**：HTTP 429 制限を検知し、サーバーの `Retry-After` ヘッダーに従って再試行を待機。

### 3. 調整可能な半透明フローティングウィンドウ
- **0%～100% の背景不透明度調整**：設定画面のスライダーでリアルタイムに透明度を変更可能。
- **テキストと背景の完全分離**：背景を完全透明に設定した場合でも、文字は高コントラストで鮮明に表示。
- **視認性とクリック貫通防止**：背景の動画を透かして視認でき、かつマウスの誤クリック貫通を防止。

### 4. 自由なレイアウトとスムーズスクロール
- **高さ自動適応**：ウィンドウを縦に広げると字幕履歴領域が自動的に拡張され、幅に応じて自然に改行。
- **ピクセル単位の滑らかなスクロール**：ホイールやタッチパッドでのスムーズな操作に対応。
- **自動追従ロック**：過去の字幕を閲覧するために上にスクロールすると自動追従を一時停止。最下部に戻ると自動追従を再開。

### 5. モジュール式プロバイダーとローカル認証情報保護
- **音声認識（ASR）**：内蔵ローカルオフライン認識（Sherpa-ONNX Zipformer）、Groq Whisper、Volcengine ASR 2.0、Azure Speech SDK など。
- **機械翻訳（MT）**：DeepL API、OpenAI 互換エンドポイント（Groq、Ollama、DeepSeek、GPT など）、Azure Translator、Google 翻訳など。
- **Windows DPAPI によるローカル暗号化**：すべての API キーと設定は Windows DPAPI を通じてローカルの `data/settings.json` に暗号化保存され、クラウドへ送信されることはありません。

---

## ローカル英語音声認識（Sherpa-ONNX Zipformer）

正式完全版（`antiDBT-v0.5.5-win-x64-full.zip`）には、検証済みの標準ローカル英語認識モデル **`sherpa-onnx-streaming-zipformer-en-2023-06-21`**（INT8 量子化）が内蔵されています。

### 特徴と制限事項
- **メリット**：
  - 従来の 20M モデルと比較して認識精度が大幅に向上し、複雑な構文や口語表現も正確に文字起こし可能。
  - ミリ秒レベルのストリーミングプレビュー応答速度を維持。
  - CPU のみで動作し、メモリ使用量は約 260 MB 程度と安定。API キー不要、利用料金なし。
- **制限事項**：
  - 英語音声認識専用モデルです。日本語や中国語の音声認識には対応していません。
  - 背景雑音、複数人の同時発声、強い訛りがある場合、精度が低下することがあります。
  - 日本語への翻訳は独立した処理ステージであり、別途翻訳プロバイダー（DeepL、Groq、ローカル Ollama など）の設定が必要です。

### ローカル ASR とクラウド翻訳の違い
antiDBT では処理パイプラインを厳密に分離しています：
1. **ASR ステージ（音声 -> テキスト）**：ローカル Sherpa-ONNX を使用する場合、すべての処理がローカル CPU で完結し、通信や API キーは発生しません。
2. **MT ステージ（原文 -> 訳文）**：
   - 入力言語と出力言語をともに `en` に設定した場合、翻訳は呼び出されず、完全オフラインで動作します。
   - 日本語（`ja`）や中国語（`zh`）に翻訳する場合、認識テキストが HTTPS 経由で設定先の翻訳サービスに送信されます。

---

## 翻訳サービスの設定方法

メイン画面右上の歯車アイコン（⚙）をクリックして設定します：

### 1. Groq（低遅延 LLM 翻訳）
1. https://console.groq.com で API キーを取得。
2. 翻訳プロバイダーで `OpenAiCompatible` を選択。
3. エンドポイント：`https://api.groq.com/openai/v1/chat/completions`。
4. モデル名：`llama-3.3-70b-versatile` または `llama-3.1-8b-instant`。
5. API キーを入力し、ソース言語を `en`、ターゲット言語を `ja` に設定して保存。

### 2. DeepL（高精度機械翻訳）
1. https://www.deepl.com で API キーを取得（Free / Pro 両対応）。
2. 翻訳プロバイダーで `DeepL` を選択。
3. API キーを入力（キー末尾の `:fx` に応じてエンドポイントを自動判定）。
4. ソース言語を `en`、ターゲット言語を `ja` に設定して保存。

---

## システム要件

- **OS**：Windows 10 64-bit（バージョン 1903 / ビルド 18362 以降）または Windows 11 64-bit。
- **オーディオ**：WASAPI ループバックに対応したサウンドデバイス（共有モード Shared Mode で動作すること）。
- **実行環境**：配布パッケージには自己完結型の .NET 10 x64 ランタイムおよび Sherpa-ONNX ネイティブライブラリが同梱されています。**.NET SDK、Python、Go、CUDA、C++ ビルドツールのインストールは不要です。**

---

## ダウンロード、インストール、アップグレード

### 初回利用
1. [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases) から最新の正式版をダウンロード：
   - パッケージ：`antiDBT-v0.5.5-win-x64-full.zip`
   - ハッシュファイル：`antiDBT-v0.5.5-win-x64-full.sha256`
2. 任意のフォルダ（例：`D:\Tools\antiDBT\`）に解凍します。
3. 解凍先の `antiDBT` フォルダ内にある `antiDBT.exe` を実行します。

### アップグレード
1. 実行中の旧バージョン antiDBT を完全に終了します。
2. 新バージョンを新しいフォルダに解凍します。
3. 過去の設定や認証情報を引き継ぐ場合は、旧バージョンの `data/settings.json` を新バージョンの `data/` フォルダにコピーします（同一 PC・同一 Windows ユーザーのみ有効）。

---

## ソースコードのビルドとテスト

```powershell
# クローン
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT/prototype

# Release ビルド
dotnet build AntiDbt.sln -c Release

# 12 の統合自動検証スイートを実行
pwsh -File scripts/verify-all.ps1 -SkipRestore

# 正式配布パッケージをビルド
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.5"
```

---

## プライバシー、ライセンス、免責事項

- **プライバシー**：中央サーバー、利用状況トラッキング、バックグラウンド解析はありません。データはローカルで処理されるか、ユーザーが指定したエンドポイントとのみ通信します。
- **ライセンス**：antiDBT ソースコードは [MIT License](LICENSE) の下で公開されています。
- **サードパーティ通知**：Sherpa-ONNX（Apache-2.0）、ONNX Runtime（MIT）、NAudio（MIT）などのライセンス情報は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) をご参照ください。
- **免責事項**：本ソフトウェアは現状有姿（AS IS）で提供され、いかなる保証も伴いません。字幕および翻訳結果は参考用途としてご利用ください。
