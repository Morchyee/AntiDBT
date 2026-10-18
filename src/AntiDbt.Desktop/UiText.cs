namespace AntiDbt.Desktop;

public static class UiText
{
    private static string _currentLanguage = "zh-Hans";

    public static string CurrentLanguage
    {
        get => _currentLanguage;
        set => _currentLanguage = NormalizeLanguage(value);
    }

    public static string NormalizeLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "zh-Hans";
        lang = lang.Trim().ToLowerInvariant();
        if (lang.StartsWith("en")) return "en";
        if (lang.StartsWith("ja")) return "ja";
        return "zh-Hans";
    }

    public static readonly (string Code, string DisplayName)[] SupportedLanguages =
    [
        ("zh-Hans", "简体中文"),
        ("en", "English"),
        ("ja", "日本語")
    ];

    private static string Pick(string zh, string en, string ja)
    {
        return _currentLanguage switch
        {
            "en" => en,
            "ja" => ja,
            _ => zh
        };
    }

    // Main Window
    public static string AppTitle => Pick("antiDBT · 实时双语字幕", "antiDBT · Realtime Bilingual Subtitles", "antiDBT · リアルタイム二言語字幕");
    public static string Start => Pick("开始", "Start", "開始");
    public static string Stop => Pick("停止", "Stop", "停止");
    public static string Stopping => Pick("停止中…", "Stopping...", "停止中…");
    public static string SourceTextToggle => Pick("原文", "Source", "原文");
    public static string FontSizeToggle => Pick("字号", "Font", "文字");
    public static string CloseTooltip => Pick("停止采集并关闭", "Stop capture and close", "キャプチャを停止して終了");
    public static string StartTooltip => Pick("开始实时转写", "Start realtime transcription", "リアルタイム書き起こしを開始");
    public static string StopTooltip => Pick("停止转写并保持历史", "Stop transcription and keep history", "書き起こしを停止し履歴を保持");
    public static string StoppingTooltip => Pick("正在排空尾句并停止采集...", "Draining trailing audio and stopping capture...", "音声をフラッシュして停止処理中…");
    public static string SourceToggleTooltip => Pick("显示 / 隐藏原文", "Show / Hide source text", "原文の表示 / 非表示");
    public static string FontSizeToggleTooltip => Pick("切换字号（小 / 中 / 大）", "Toggle font size (Small / Medium / Large)", "フォントサイズ切替（小 / 中 / 大）");
    public static string SettingsTooltip => Pick("API 与字幕设置", "API & Subtitle Settings", "API・字幕設定");
    public static string ExpandTooltip => Pick("展开历史字幕", "Expand subtitle history", "字幕履歴を展開");
    public static string CollapseTooltip => Pick("收起历史字幕", "Collapse subtitle history", "字幕履歴を折りたたむ");

    // Status Badge
    public static string StatusIdle => Pick("未开始", "Idle", "待機中");
    public static string StatusConnecting => Pick("连接中", "Connecting", "接続中");
    public static string StatusListening => Pick("聆听中", "Listening", "リスニング中");
    public static string StatusRetrying => Pick("重试中", "Retrying", "再試行中");
    public static string StatusStopping => Pick("停止中", "Stopping", "停止中");
    public static string StatusError => Pick("连接异常", "Error", "接続異常");
    public static string StatusTranslationError => Pick("翻译异常", "MT Error", "翻訳異常");

    public static string StatusIdleTooltip => Pick("未开始实时转写", "Realtime transcription not started", "リアルタイム書き起こしは未開始です");
    public static string StatusConnectingTooltip => Pick("正在建立实时音频流连接...", "Connecting to audio stream...", "ストリーミング接続中…");
    public static string StatusListeningTooltip => Pick("正在聆听并转写系统音频", "Listening and transcribing system audio", "システム音声を認識・翻訳中");
    public static string StatusListeningSilentTooltip => Pick("已连接但无音频信号（静音中）", "Connected but no audio signal (silent)", "接続済みですが音声信号がありません（無音）");
    public static string StatusRetryingTooltip => Pick("连接异常，正在尝试自动恢复...", "Connection issue, attempting automatic recovery...", "接続異常、自動復旧を試行中…");
    public static string StatusTranslationErrorTooltip => Pick("ASR 语音识别正常，但机器翻译服务发生异常", "Speech recognition is normal, but machine translation failed", "音声認識は正常ですが翻訳サービスで異常が発生しています");

    // Language Picker Popup
    public static string LangPickerTitle => Pick("双语字幕语言设置", "Bilingual Subtitle Languages", "字幕の言語設定");
    public static string SourceLangLabel => Pick("识别源语言", "Speech Source Language", "認識元言語");
    public static string TargetLangLabel => Pick("翻译目标语言", "Translation Target Language", "翻訳先言語");
    public static string SwapLangTooltip => Pick("交换源语言与目标语言", "Swap source and target languages", "元言語と先言語を入れ替え");
    public static string LangProviderNotice => Pick("提示：源语言受 ASR 识别能力约束，目标语言由翻译服务决定。", "Note: Source language is bounded by ASR; target language is determined by translation service.", "ヒント：元言語は ASR の能力に依存し、先言語は翻訳サービスによって決まります。");

    // Subtitle Viewport & Context Menu
    public static string ScrollToBottom => Pick("↓ 回到底部", "↓ Bottom", "↓ 最下部へ");
    public static string ScrollToBottomTooltip => Pick("恢复自动跟随最新字幕", "Resume auto-following latest subtitles", "最新字幕への自動追従を再開");
    public static string RetrySegment => Pick("↻ 重试", "↻ Retry", "↻ 再試行");
    public static string RetrySegmentTooltip => Pick("重新请求此段译文", "Retry translation for this segment", "このセグメントの翻訳を再試行");
    public static string MenuLocalDemo => Pick("本地演示", "Local Demo", "ローカルデモ");
    public static string MenuCopyParagraph => Pick("复制当前段落", "Copy Selected Paragraph", "この段落をコピー");
    public static string MenuCopyAll => Pick("复制全部记录", "Copy All History", "すべての履歴をコピー");
    public static string MenuClearHistory => Pick("清空屏幕记录", "Clear Subtitle History", "画面の履歴を消去");
    public static string MenuTopmost => Pick("置顶显示", "Always on Top", "常に最前面に表示");
    public static string MenuClickThrough => Pick("鼠标穿透 (Ctrl+Alt+T)", "Click-Through Overlay (Ctrl+Alt+T)", "マウスクリック透過 (Ctrl+Alt+T)");
    public static string MenuExportSubtitles => Pick("导出字幕记录...", "Export Subtitles...", "字幕をエクスポート...");
    public static string ExportSrt => Pick("导出为 SRT 字幕 (*.srt)", "Export as SRT (*.srt)", "SRT 字幕としてエクスポート (*.srt)");
    public static string ExportVtt => Pick("导出为 VTT 字幕 (*.vtt)", "Export as VTT (*.vtt)", "VTT 字幕としてエクスポート (*.vtt)");
    public static string ExportTxt => Pick("导出为 纯文本 TXT (*.txt)", "Export as Plain Text (*.txt)", "テキストとしてエクスポート (*.txt)");
    public static string ExportNoSubtitles => Pick("暂无字幕记录可导出", "No subtitles available to export", "エクスポート可能な字幕がありません");
    public static string ExportSuccess => Pick("已成功导出字幕到: {0}", "Subtitles exported successfully to: {0}", "字幕を保存しました: {0}");
    public static string ClickThroughEnabled => Pick("鼠标穿透已开启 · 按 Ctrl+Alt+T 可恢复交互", "Click-through enabled · Press Ctrl+Alt+T to restore", "マウスクリック透過が有効 · Ctrl+Alt+T で復帰");
    public static string ClickThroughDisabled => Pick("鼠标穿透已关闭 · 恢复正常窗口交互", "Click-through disabled · Restored window interaction", "マウスクリック透過が無効 · 通常モードに復帰");
    public static string MenuExit => Pick("退出 antiDBT", "Exit antiDBT", "antiDBT を終了");

    // Settings Window Navigation
    public static string NavGeneral => Pick("常规", "General", "一般");
    public static string NavAsr => Pick("语音识别", "Speech Recognition", "音声認識");
    public static string NavMt => Pick("翻译服务", "Translation", "翻訳サービス");
    public static string NavAudio => Pick("音频", "Audio", "オーディオ");
    public static string NavAppearance => Pick("外观", "Appearance", "外観");
    public static string NavNetwork => Pick("网络与代理", "Network", "ネットワーク");
    public static string NavAdvanced => Pick("高级", "Advanced", "詳細設定");
    public static string NavAbout => Pick("关于", "About", "情報");

    // Settings Window General Page
    public static string CardLanguageAndWindow => Pick("语言与窗口", "Languages & Window", "言語とウィンドウ");
    public static string FieldSourceLanguage => Pick("源语言 · 随识别服务能力筛选", "Source Language (filtered by ASR capabilities)", "元言語（ASRの対応言語により絞り込み）");
    public static string FieldTargetLanguage => Pick("目标语言 · 同语言直通无需翻译调用", "Target Language (direct passthrough when identical)", "先言語（同一言語時は翻訳スキップ）");
    public static string CheckboxShowSource => Pick("显示原文", "Show Source Text", "原文を表示");
    public static string CheckboxTopmost => Pick("窗口置顶", "Always on Top", "最前面に固定");
    public static string BtnCheckConfig => Pick("检查当前配置（不联网）", "Validate Config (Offline)", "設定を検証（オフライン）");
    public static string ConfigValidMsg => Pick("配置完整 · 认证和真实请求尚未验证", "Configuration valid · Credentials not verified online", "設定は正常です · オンライン検証は未実施");
    public static string NoteLanguagePreserve => Pick("切换服务会保留当前语言；不支持的选项保持可见并说明原因，不会自动改为英语。", "Switching providers preserves active languages; unsupported choices remain visible with explanatory notes.", "サービスを切り替えても現在の言語は保持されます。非対応の項目も理由付きで表示されます。");

    // Settings Window Translation Fallback Page
    public static string CardBackupTranslation => Pick("备用翻译", "Backup Translation", "予備の翻訳");
    public static string CheckboxFallbackEnable => Pick("启用备用翻译服务", "Enable Fallback Translation", "予備の翻訳サービスを有効化");
    public static string CheckboxFallbackConsent => Pick("同意备用服务的调用与额外费用", "Consent to fallback service usage & costs", "予備サービスの呼び出しと追加費用に同意");
    public static string FieldBackupProvider => Pick("选择其他已配置的服务（先在上方切换服务完成配置）", "Select pre-configured provider", "設定済みの別のサービスを選択");
    public static string NoteBackupFallback => Pick("默认关闭。只有启用、确认额外费用并配置不同的备用服务，才允许自动回退；运行前会检查语言和凭据。", "Disabled by default. Requires explicit consent and a different pre-configured service.", "デフォルト無効。明示的な同意と別の設定済みサービスが必要です。");

    // Settings Window Audio Page
    public static string CardSystemAudio => Pick("系统音频回环", "System Audio Loopback", "システム音声ループバック");
    public static string AudioDefaultDevice => Pick("跟随启动时的默认输出设备", "Follow system default output device", "起動時のデフォルト出力デバイスに追従");
    public static string FieldAudioPlaybackDevice => Pick("播放设备", "Playback Device", "再生デバイス");
    public static string AudioCannotEnumerate => Pick("无法枚举音频设备，可保留默认输出设备后重试。", "Unable to enumerate audio devices. Keep default and retry.", "オーディオデバイスを取得できません。デフォルトのまま再試行してください。");
    public static string NoteAudioCapture => Pick("点击主窗口“开始”后才会采集并向所选识别服务发送系统音频。连接检查不会发送录音。", "Audio capture only starts when clicking 'Start'. Connection tests do not send audio.", "メイン画面で「開始」を押すまで音声キャプチャは開始されません。接続テストで音声は送信されません。");

    // Settings Window Appearance Page
    public static string CardThemeAndFont => Pick("主题与文字", "Theme & Typography", "テーマと文字");
    public static string FieldUiLanguage => Pick("界面语言 · 中文 / English / 日本語", "UI Language · English / 简体中文 / 日本語", "表示言語 · 日本語 / English / 简体中文");
    public static string FieldTheme => Pick("主题 · System 跟随系统 / Dark 深色 / Light 浅色", "Theme · System / Dark / Light", "テーマ · System / Dark / Light");
    public static string FieldFontSize => Pick("字号 · small 小 / medium 中 / large 大", "Font Size · Small / Medium / Large", "フォントサイズ · Small / Medium / Large");
    public static string FieldOpacity => Pick("背景不透明度 · 0% 完全透明 / 50% 明显半透 / 80% 推荐默认 / 100% 完全不透", "Background Opacity · 0% Transparent / 80% Default / 100% Solid", "背景の不透明度 · 0% 完全透明 / 80% 推奨 / 100% 不透明");
    public static string NoteOpacity => Pick("背景透明度支持桌面实时透视预览，文字与控制台保持高对比清晰度；主窗口支持拖动、缩放与自适应。", "Realtime background opacity preview. Text remains crisp and solid with click-through protection.", "背景透明度のリアルタイムプレビュー対応。文字は鮮明なコントラストを維持し、クリック貫通を防ぎます。");

    // Settings Window Network Page
    public static string CardNetworkProxy => Pick("当前使用系统网络设置", "Using System Network Settings", "システムネットワーク設定を使用中");
    public static string NoteNetworkProxy1 => Pick("HTTP / SDK 请求使用 Windows 与 .NET 的系统代理设置。本版本没有独立代理地址、账号或代理连通性开关；请在 Windows 设置中管理代理。", "HTTP/SDK requests inherit Windows and .NET proxy settings.", "HTTP / SDK リクエストは Windows および .NET のプロキシ設定を引き継ぎます。");
    public static string NoteNetworkProxy2 => Pick("修改自定义服务地址的主机、协议或端口会清除该服务凭据。修改模型或任何接口路径会清除该模型语言声明，需重新确认。", "Changing custom endpoint host or protocol resets secrets for security.", "カスタムエンドポイントのホスト等を変更すると、セキュリティのため認証情報がリセットされます。");

    // Settings Window Advanced Page
    public static string CardAdvanced => Pick("请求与上下文", "Requests & Context", "リクエストと文脈");
    public static string FieldDebounce => Pick("增量翻译合并间隔（毫秒，0–2000）", "Incremental debounce delay (ms, 0–2000)", "増分翻訳マージ間隔（ミリ秒、0〜2000）");
    public static string FieldTimeout => Pick("翻译请求超时（秒，1–120）", "Translation timeout (seconds, 1–120)", "翻訳リクエストタイムアウト（秒、1〜120）");
    public static string FieldContext => Pick("上下文句数（0–12，服务支持时使用）", "Context sentences (0–12)", "文脈文数（0〜12、対応サービスのみ）");

    // Settings Window About Page
    public static string CardAbout => Pick("antiDBT · 实时双语字幕", "antiDBT · Realtime Bilingual Subtitles", "antiDBT · リアルタイム二言語字幕");
    public static string NoteAbout1 => Pick("凭据使用 Windows DPAPI 按当前用户加密保存。每个服务独立保留配置；服务账户 JSON 可直接粘贴。显示密钥是显式操作，隐藏或离开服务会清除明文视图。", "Credentials are encrypted per-user via Windows DPAPI. Secrets never leave your machine.", "認証情報は Windows DPAPI でユーザーごとに安全に暗号化されます。");
    public static string NoteAbout2 => Pick("配置完整、请求成功与翻译质量是不同状态。公开样例测试可能计费，只在你点击测试时执行。Seed LiveInterpret 2.0 尚未启用；没有自动云端测试。", "Validation, connectivity, and translation quality are distinct states. Tests only run on click.", "設定検証、接続、翻訳品質はそれぞれ独立した状態です。テストは明示的にクリックした時のみ実行されます。");

    // Settings Window Buttons & Diagnostics
    public static string BtnSaveSettings => Pick("保存设置", "Save Settings", "設定を保存");
    public static string BtnCancel => Pick("取消", "Cancel", "キャンセル");
    public static string BtnTestConnectionNoAudio => Pick("连接检查（无音频）", "Test Connection (No Audio)", "接続テスト（音声なし）");
    public static string BtnTestConnectionSample => Pick("连接检查（可能使用公开样例）", "Test Connection (Sample)", "接続テスト（サンプル）");
    public static string BtnTestSampleTranslation => Pick("公开样例翻译（可能计费）", "Test Sample Translation", "サンプル翻訳テスト");
    public static string BtnFetchModels => Pick("获取文本模型列表", "Fetch Models List", "モデル一覧を取得");
    public static string BtnRefreshDeepL => Pick("刷新 DeepL 语言目录", "Refresh DeepL Languages", "DeepL 言語一覧を更新");
    public static string RequestingPrompt => Pick("正在请求… · 切换服务或关闭窗口会取消", "Requesting... · Switching provider cancels", "リクエスト中… · 切り替えでキャンセル");
    public static string RequestSuccessPrompt => Pick("请求成功 · ", "Request successful · ", "リクエスト成功 · ");
    public static string RequestErrorPrompt => Pick("最后测试错误 · ", "Last test error · ", "テストエラー · ");

    // Additional MainWindow elements
    public static string Expand => Pick("展开", "Expand", "展開");
    public static string Collapse => Pick("收起", "Collapse", "折りたたむ");
    public static string SourceBtn => Pick("原文", "Source", "原文");
    public static string FontBtn => Pick("字号", "Font", "文字");
    public static string SettingsBtn => Pick("设置", "Settings", "設定");
    public static string CloseBtnTip => Pick("停止采集并关闭", "Stop capture and close", "キャプチャを停止して終了");
    public static string SourceBtnTip => Pick("显示 / 隐藏原文", "Show / Hide source text", "原文の表示 / 非表示");
    public static string FontBtnTip => Pick("切换字号（小 / 中 / 大）", "Toggle font size (Small / Medium / Large)", "フォントサイズ切替（小 / 中 / 大）");
    public static string SettingsBtnTip => Pick("API 与字幕设置", "API & Subtitle Settings", "API・字幕設定");
    public static string ExpandTip => Pick("展开历史字幕", "Expand subtitle history", "字幕履歴を展開");
    public static string CollapseTip => Pick("收起历史字幕", "Collapse subtitle history", "字幕履歴を折りたたむ");
    public static string SwapLanguagesTip => Pick("交换源语言与目标语言", "Swap source and target languages", "元言語と先言語を入れ替え");
    public static string LangTitle => Pick("双语字幕语言设置", "Bilingual Subtitle Languages", "字幕の言語設定");
    public static string SourceBoxLabel => Pick("识别源语言", "Speech Source Language", "認識元言語");
    public static string TargetBoxLabel => Pick("翻译目标语言", "Translation Target Language", "翻訳先言語");
    public static string ProviderNoticeText => Pick("提示：源语言受 ASR 识别能力约束，目标语言由翻译服务决定。", "Note: Source language is bounded by ASR; target language is determined by translation service.", "ヒント：元言語は ASR の能力に依存し、先言語は翻訳サービスによって決まります。");
    public static string BackToBottom => Pick("↓ 回到底部", "↓ Bottom", "↓ 最下部へ");
    public static string BackToBottomTip => Pick("有新的字幕，点击滚动回最新内容", "New subtitles available, click to scroll to bottom", "新しい字幕があります。クリックで最下部へスクロール");
    public static string MenuDemo => Pick("本地演示（不采集音频、不访问网络）", "Local Demo (No audio capture, no network)", "ローカルデモ（音声入力なし・通信なし）");
    public static string MenuCopy => Pick("复制选中字幕 / 当前字幕", "Copy Selected / Current Subtitle", "字幕をコピー");
    public static string Retry => Pick("重试", "Retry", "再試行");
    public static string PlaceholderSource => Pick("系统播放的语音将实时显示在这里", "System audio transcription will appear here", "再生中の音声書き起こしがここに表示されます");
    public static string PlaceholderTarget => Pick("译文将在下方显示", "Translation will appear below", "翻訳文がここに表示されます");
    public static string TipUnfinishedTranslationError => Pick("存在未完成翻译错误，可在列表中点击重试", "Translation error exists, click retry in list", "未完了の翻訳エラーがあります。リスト内の再試行をクリックしてください");
    public static string TipNoAudioSignal => Pick("已连接但无音频信号（请确认系统正在播放声音）", "Connected but no audio signal (please ensure system is playing audio)", "接続中ですが音声信号がありません（再生中の音声を確認してください）");
}

