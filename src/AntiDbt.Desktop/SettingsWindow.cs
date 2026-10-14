using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiDbt.Core;
using AntiDbt.Windows;
namespace AntiDbt.Desktop;
public sealed class SettingsWindow : Window
{
    readonly AppSettings original;
    readonly TextBox asrEndpoint = new(), asrResource = new(), asrApp = new(), asrModel = new(), mtEndpoint = new(), mtResource = new(), mtApp = new();
    readonly PasswordBox asrKey = new(), asrAccess = new(), mtKey = new(), mtAccess = new();
    readonly ComboBox language = new() { IsEditable = true }, target = new(), font = new(), devices = new();
    readonly TextBox debounce = new(), timeout = new();
    readonly CheckBox source = new() { Content = "显示原文" }, pin = new() { Content = "窗口置顶" };
    readonly TextBlock error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    public AppSettings Result
    {
        get; private set;
    }
    public SettingsWindow(AppSettings settings)
    {
        original = settings;
        Result = settings;
        Title = "antiDBT · 服务与字幕设置";
        Width = 620;
        Height = 730;
        MinWidth = 500;
        MinHeight = 400;
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 38));
        Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        bottom.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(actions);
        var cancel = new Button { Content = "取消" };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel);
        var save = new Button { Content = "保存设置" };
        save.Click += (_, _) => Save();
        actions.Children.Add(save);
        var content = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Heading(string s) => content.Children.Add(new TextBlock { Text = s, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 9) });
        void Field(string label, Control control)
        {
            content.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray });
            content.Children.Add(control);
        }
        void Note(string s) => content.Children.Add(new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 10) });
        Heading("语音识别 · 火山引擎豆包流式 ASR 2.0");
        Note("配置后点击主窗口“开始”才会采集并发送系统播放音频。API Key 与旧版 APP ID / Access Token 二选一；填写 API Key 时优先使用它。");
        Field("公开 WebSocket 地址", asrEndpoint);
        asrEndpoint.Text = settings.Asr.Endpoint;
        Field("ASR 模型名", asrModel);
        asrModel.Text = settings.Asr.Model;
        Field("资源 ID", asrResource);
        asrResource.Text = settings.Asr.Options.GetValueOrDefault("resourceId", "");
        Field("API Key（加密保存）", asrKey);
        asrKey.Password = settings.Asr.ApiKey;
        Field("旧版 APP ID（可选）", asrApp);
        asrApp.Text = settings.Asr.Options.GetValueOrDefault("appId", "");
        Field("旧版 Access Token（可选，加密保存）", asrAccess);
        asrAccess.Password = settings.Asr.Secrets.GetValueOrDefault("accessKey", "");
        foreach (var s in new[] { "", "zh-CN", "en-US" })
            language.Items.Add(s);
        language.Text = settings.Asr.Language;
        Field("识别语言（留空为服务默认 / 自动，支持情况取决于模型）", language);
        Heading("翻译 · 豆包机器翻译 API");
        Field("公开 HTTPS 地址", mtEndpoint);
        mtEndpoint.Text = settings.Translation.Endpoint;
        Field("资源 ID", mtResource);
        mtResource.Text = settings.Translation.Options.GetValueOrDefault("resourceId", "");
        Field("API Key（加密保存）", mtKey);
        mtKey.Password = settings.Translation.ApiKey;
        Field("旧版 APP ID（可选）", mtApp);
        mtApp.Text = settings.Translation.Options.GetValueOrDefault("appId", "");
        Field("旧版 Access Token（可选，加密保存）", mtAccess);
        mtAccess.Password = settings.Translation.Secrets.GetValueOrDefault("accessKey", "");
        Field("增量翻译合并间隔（毫秒，0–2000）", debounce);
        debounce.Text = settings.Translation.DebounceMilliseconds.ToString();
        Field("翻译请求超时（秒，1–120）", timeout);
        timeout.Text = settings.Translation.TimeoutSeconds.ToString();
        Note("Seed LiveInterpret 2.0：尚未启用。国内服务的原文、译文与增量事件协议还需验证。机器翻译公开接口没有上下文参数，当前适配器仅发送当前片段。");
        Heading("音频与字幕");
        devices.DisplayMemberPath = "Name";
        devices.SelectedValuePath = "Id";
        devices.Items.Add(new AudioDevice("", "跟随启动时的默认输出设备", true));
        try
        {
            foreach (var device in WasapiAudioSource.ListDevices())
                devices.Items.Add(device);
        }
        catch { Note("无法枚举音频设备，可保留默认输出设备后重试。"); }
        devices.SelectedValue = settings.AudioDeviceId;
        if (devices.SelectedIndex < 0)
            devices.SelectedIndex = 0;
        Field("系统音频回环设备", devices);
        target.Items.Add("zh");
        target.Items.Add("en");
        target.SelectedItem = settings.TargetLanguage;
        Field("翻译目标语言（zh 中文 / en 英文）", target);
        foreach (var size in new[] { "small", "medium", "large" })
            font.Items.Add(size);
        font.SelectedItem = settings.FontSize;
        Field("字号（小 16 / 中 20 / 大 32）", font);
        source.IsChecked = settings.ShowSource;
        pin.IsChecked = settings.Topmost;
        content.Children.Add(source);
        content.Children.Add(pin);
        Note("密钥使用 Windows DPAPI 按当前用户加密，保存在软件目录 data/settings.json。不会写入测试报告。设置保存后停止当前会话，再由你点击开始。");
    }
    void Save()
    {
        try
        {
            ValidateEndpoint(asrEndpoint.Text, true);
            ValidateEndpoint(mtEndpoint.Text, false);
            if (!int.TryParse(debounce.Text, out var delay) || delay < 0 || delay > 2000 || !int.TryParse(timeout.Text, out var seconds) || seconds < 1 || seconds > 120)
                throw new ArgumentException("请填写有效的合并间隔和超时。");
            var aOptions = new Dictionary<string, string>(original.Asr.Options) { ["resourceId"] = asrResource.Text.Trim(), ["appId"] = asrApp.Text.Trim() };
            var tOptions = new Dictionary<string, string>(original.Translation.Options) { ["resourceId"] = mtResource.Text.Trim(), ["appId"] = mtApp.Text.Trim() };
            Result = original with
            {
                Asr = original.Asr with
                {
                    Endpoint = asrEndpoint.Text.Trim(),
                    Model = asrModel.Text.Trim(),
                    Language = language.Text.Trim(),
                    ApiKey = asrKey.Password.Trim(),
                    Options = aOptions,
                    Secrets = new()
                    {
                        ["accessKey"] = asrAccess.Password.Trim()
                    }
                },
                Translation = original.Translation with
                {
                    Endpoint = mtEndpoint.Text.Trim(),
                    ApiKey = mtKey.Password.Trim(),
                    Options = tOptions,
                    Secrets = new()
                    {
                        ["accessKey"] = mtAccess.Password.Trim()
                    },
                    DebounceMilliseconds = delay,
                    TimeoutSeconds = seconds
                },
                TargetLanguage = target.SelectedItem as string ?? "zh",
                FontSize = font.SelectedItem as string ?? "medium",
                ShowSource = source.IsChecked == true,
                Topmost = pin.IsChecked == true,
                AudioDeviceId = devices.SelectedValue as string ?? ""
            };
            DialogResult = true;
        }
        catch (ArgumentException e) { error.Text = e.Message; }
    }
    static void ValidateEndpoint(string text, bool socket)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != (socket ? "wss" : "https") || !string.IsNullOrEmpty(uri.UserInfo) || !uri.Host.Equals("openspeech.bytedance.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("当前适配器请填写 openspeech.bytedance.com 的公开安全接口地址。");
    }
}
