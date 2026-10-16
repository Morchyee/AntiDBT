using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiDbt.Core;
using AntiDbt.Providers;
using AntiDbt.Windows;
namespace AntiDbt.Desktop;
public sealed class SettingsWindow : Window
{
    readonly AppSettings original;
    readonly CancellationTokenSource lifetime = new();
    readonly Dictionary<string, AsrConfiguration> asrProfiles;
    readonly Dictionary<string, TranslationConfiguration> mtProfiles;
    readonly ProviderEditor asr, mt;
    readonly LanguagePicker sourceLanguage = new(), targetLanguage = new();
    string sourceId, targetId;
    readonly ComboBox font = new(), devices = new(), theme = new(), backup = new() { DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" };
    readonly Slider opacity = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10, Margin = new Thickness(0, 0, 12, 0) };
    readonly TextBlock opacityLabel = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, MinWidth = 45 };
    readonly TextBox debounce = new(), timeout = new(), context = new();
    readonly CheckBox source = new() { Content = "显示识别原文" }, pin = new() { Content = "窗口置顶" }, fallback = new() { Content = "启用备用翻译（主服务失败时）" }, consent = new() { Content = "我确认备用服务可能产生额外费用" };
    readonly TextBlock error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    readonly ListBox navigation = new() { BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 18, 0) };
    readonly Dictionary<string, StackPanel> pages = new();
    readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public AppSettings Result { get; private set; }
    public SettingsWindow(AppSettings settings)
    {
        original = Result = LanguageSettings.Upgrade(settings); settings = original; sourceId = settings.SourceLanguage; targetId = settings.TargetLanguage;
        asrProfiles = new(settings.AsrProfiles); mtProfiles = new(settings.TranslationProfiles);
        asrProfiles[settings.Asr.Provider] = settings.Asr; mtProfiles[settings.Translation.Provider] = settings.Translation;
        Title = "antiDBT · 设置"; Width = 980; Height = 800; MinWidth = 680; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; DesktopTheme.Apply(this, settings.Theme);
        Closed += (_, _) => { lifetime.Cancel(); asr?.CancelRequests(); mt?.CancelRequests(); asr?.HideSecrets(); mt?.HideSecrets(); };
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 20) }; DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        title.Children.Add(new TextBlock { Text = "让字幕适合你的工作方式", FontSize = 25, FontWeight = FontWeights.SemiBold });
        title.Children.Add(DesktopTheme.Note("语言、识别与翻译服务 · 设置保存后，下次开始时生效"));
        var bottom = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); bottom.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(actions);
        var cancel = new Button { Content = "取消", IsCancel = true }; cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        var save = new Button { Content = "保存设置", IsDefault = true }; save.Click += (_, _) => Save(); actions.Children.Add(save);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(150) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); root.Children.Add(body);
        body.Children.Add(navigation); body.Children.Add(scroller); Grid.SetColumn(scroller, 1); ScrollRelay.Attach(scroller);
        var pageHost = new Grid(); scroller.Content = pageHost;
        foreach (var name in new[] { "常规", "语音识别", "翻译服务", "音频", "外观", "网络与代理", "高级", "关于" })
        {
            var page = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0) };
            pages[name] = page; pageHost.Children.Add(page); navigation.Items.Add(name);
            page.Children.Add(new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        }
        navigation.SelectionChanged += (_, _) => SelectPage(navigation.SelectedItem as string ?? "常规");
        asr = new ProviderEditor(this, true, settings.Asr.Provider); mt = new ProviderEditor(this, false, settings.Translation.Provider);
        pages["语音识别"].Children.Add(DesktopTheme.Card(asr.Panel)); pages["翻译服务"].Children.Add(DesktopTheme.Card(mt.Panel));
        var languages = Card(pages["常规"], "语言与窗口");
        Field(languages, "源语言 · 随识别服务能力筛选", sourceLanguage); Field(languages, "目标语言 · 同语言直通无需翻译调用", targetLanguage);
        sourceLanguage.Selected += id => { sourceId = id; SyncLanguages(); }; targetLanguage.Selected += id => { targetId = id; SyncLanguages(); };
        source.IsChecked = settings.ShowSource; pin.IsChecked = settings.Topmost; languages.Children.Add(source); languages.Children.Add(pin);
        var check = new Button { Content = "检查当前配置（不联网）", HorizontalAlignment = HorizontalAlignment.Left }; languages.Children.Add(check);
        check.Click += (_, _) => { try { var value = BuildResult(); error.Text = ProviderLanguages.ValidateSettings(value) ?? "配置完整 · 认证和真实请求尚未验证"; } catch (ArgumentException e) { error.Text = e.Message; } };
        Note(languages, "切换服务会保留当前语言；不支持的选项保持可见并说明原因，不会自动改为英语。");
        var backupCard = Card(pages["翻译服务"], "备用翻译");
        fallback.IsChecked = settings.FallbackEnabled; consent.IsChecked = settings.FallbackCostConsent;
        backupCard.Children.Add(fallback); backupCard.Children.Add(consent); backup.ItemsSource = ProviderDescriptors.Translation; backup.SelectedValue = settings.FallbackTranslationProviderId;
        Field(backupCard, "选择其他已配置的服务（先在上方切换服务完成配置）", backup);
        Note(backupCard, "默认关闭。只有启用、确认额外费用并配置不同的备用服务，才允许自动回退；运行前会检查语言和凭据。");
        var audio = Card(pages["音频"], "系统音频回环"); devices.DisplayMemberPath = "Name"; devices.SelectedValuePath = "Id";
        devices.Items.Add(new AudioDevice("", "跟随启动时的默认输出设备", true));
        try { foreach (var device in WasapiAudioSource.ListDevices()) devices.Items.Add(device); } catch { Note(audio, "无法枚举音频设备，可保留默认输出设备后重试。"); }
        devices.SelectedValue = settings.AudioDeviceId; if (devices.SelectedIndex < 0) devices.SelectedIndex = 0; Field(audio, "播放设备", devices);
        Note(audio, "点击主窗口“开始”后才会采集并向所选识别服务发送系统音频。连接检查不会发送录音。");
        var appearance = Card(pages["外观"], "主题与文字");
        foreach (var value in new[] { "System", "Dark", "Light" }) theme.Items.Add(value); theme.SelectedItem = settings.Theme; Field(appearance, "主题 · System 跟随系统 / Dark 深色 / Light 浅色", theme);
        theme.SelectionChanged += (_, _) => DesktopTheme.Apply(this, theme.SelectedItem as string ?? "System");
        foreach (var value in new[] { "small", "medium", "large" }) font.Items.Add(value); font.SelectedItem = settings.FontSize; Field(appearance, "字号 · small 小 / medium 中 / large 大", font);
        opacity.Value = Math.Round(Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0) * 100.0);
        opacityLabel.Text = $"{(int)opacity.Value}%";
        opacityLabel.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var opacityBox = new DockPanel { Margin = new Thickness(0, 4, 0, 12) };
        DockPanel.SetDock(opacityLabel, Dock.Right);
        opacityBox.Children.Add(opacityLabel);
        opacityBox.Children.Add(opacity);
        opacity.ValueChanged += (_, e) =>
        {
            var val = (int)Math.Round(e.NewValue);
            opacityLabel.Text = $"{val}%";
            if (Owner is MainWindow main)
                main.ApplyLiveOpacity(val / 100.0);
        };
        Field(appearance, "背景不透明度 · 0% 完全透明 / 50% 明显半透 / 80% 推荐默认 / 100% 完全不透", opacityBox);
        Note(appearance, "背景透明度支持桌面实时透视预览，文字与控制台保持高对比清晰度；主窗口支持拖动、缩放与自适应。");
        var network = Card(pages["网络与代理"], "当前使用系统网络设置");
        Note(network, "HTTP / SDK 请求使用 Windows 与 .NET 的系统代理设置。本版本没有独立代理地址、账号或代理连通性开关；请在 Windows 设置中管理代理。");
        Note(network, "修改自定义服务地址的主机、协议或端口会清除该服务凭据。修改模型或任何接口路径会清除该模型语言声明，需重新确认。");
        var advanced = Card(pages["高级"], "请求与上下文");
        Field(advanced, "增量翻译合并间隔（毫秒，0–2000）", debounce); debounce.Text = settings.Translation.DebounceMilliseconds.ToString();
        Field(advanced, "翻译请求超时（秒，1–120）", timeout); timeout.Text = settings.Translation.TimeoutSeconds.ToString();
        Field(advanced, "上下文句数（0–12，服务支持时使用）", context); context.Text = settings.Translation.ContextSentences.ToString();
        var about = Card(pages["关于"], "antiDBT · 实时双语字幕");
        Note(about, "凭据使用 Windows DPAPI 按当前用户加密保存。每个服务独立保留配置；服务账户 JSON 可直接粘贴。显示密钥是显式操作，隐藏或离开服务会清除明文视图。");
        Note(about, "配置完整、请求成功与翻译质量是不同状态。公开样例测试可能计费，只在你点击测试时执行。Seed LiveInterpret 2.0 尚未启用；没有自动云端测试。");
        SyncLanguages(); navigation.SelectedIndex = 0;
    }
    static StackPanel Card(Panel page, string title) { var content = new StackPanel(); Heading(content, title); page.Children.Add(DesktopTheme.Card(content)); return content; }
    void SelectPage(string name) { foreach (var pair in pages) pair.Value.Visibility = pair.Key == name ? Visibility.Visible : Visibility.Collapsed; scroller.ScrollToTop(); }
    void SyncLanguages()
    {
        asr.Snapshot(); mt.Snapshot();
        asrProfiles[asr.Id] = asrProfiles[asr.Id] with { Language = sourceId };
        sourceLanguage.Configure(sourceId, ProviderLanguages.AsrChoices(asrProfiles[asr.Id]), original.RecentLanguages);
        targetLanguage.Configure(targetId, ProviderLanguages.TargetChoices(mtProfiles[mt.Id]), original.RecentLanguages, mt.Id is "OpenAiCompatible" or "GroqLlm");
    }
    Dictionary<string, string> Read(string id, bool isAsr)
    {
        if (isAsr) { var c = asrProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultAsr(id); return Values(c.ApiKey, c.Endpoint, c.Model, c.Language, c.Options, c.Secrets); }
        var mt = mtProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultTranslation(id); return Values(mt.ApiKey, mt.Endpoint, mt.Model, "", mt.Options, mt.Secrets);
    }
    static Dictionary<string, string> Values(string key, string endpoint, string model, string language, Dictionary<string, string> options, Dictionary<string, string> secrets)
    {
        var values = new Dictionary<string, string>(options); foreach (var pair in secrets) values[pair.Key] = pair.Value;
        values["ApiKey"] = key; values["Endpoint"] = endpoint; values["Model"] = model; values["Language"] = language; return values;
    }
    void Write(string id, bool isAsr, Dictionary<string, string> v)
    {
        var d = ProviderDescriptors.Find(id, isAsr);
        if (isAsr)
        {
            var c = asrProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultAsr(id); var (options, secrets) = Dictionaries(d, v, c.Options, c.Secrets);
            asrProfiles[id] = c with { ApiKey = v.GetValueOrDefault("ApiKey", ""), Endpoint = v.GetValueOrDefault("Endpoint", ""), Model = v.GetValueOrDefault("Model", ""), Language = sourceId, Options = options, Secrets = secrets };
        }
        else
        {
            var c = mtProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultTranslation(id); var (options, secrets) = Dictionaries(d, v, c.Options, c.Secrets);
            mtProfiles[id] = c with { ApiKey = v.GetValueOrDefault("ApiKey", ""), Endpoint = v.GetValueOrDefault("Endpoint", ""), Model = v.GetValueOrDefault("Model", ""), Options = options, Secrets = secrets };
        }
    }
    static (Dictionary<string, string>, Dictionary<string, string>) Dictionaries(ProviderDescriptor d, Dictionary<string, string> values, Dictionary<string, string> oldOptions, Dictionary<string, string> oldSecrets)
    {
        var options = new Dictionary<string, string>(oldOptions); var secrets = values.ContainsKey("__clearSecrets") ? new Dictionary<string, string>() : new Dictionary<string, string>(oldSecrets);
        foreach (var f in d.Fields.Where(f => f.Key is not ("ApiKey" or "Endpoint" or "Model" or "Language"))) (f.Secret ? secrets : options)[f.Key] = values.GetValueOrDefault(f.Key, "");
        return (options, secrets);
    }
    AppSettings BuildResult()
    {
        asr.Snapshot(); mt.Snapshot();
        ValidateEndpoint(asr.Id, asrProfiles[asr.Id].Endpoint, asrProfiles[asr.Id].Options);
        ValidateEndpoint(mt.Id, mtProfiles[mt.Id].Endpoint, mtProfiles[mt.Id].Options);
        if (!int.TryParse(debounce.Text, out var delay) || delay < 0 || delay > 2000 || !int.TryParse(timeout.Text, out var seconds) || seconds < 1 || seconds > 120 || !int.TryParse(context.Text, out var count) || count < 0 || count > 12) throw new ArgumentException("请填写有效的合并间隔、超时和上下文句数。");
        var translation = mtProfiles[mt.Id] with { DebounceMilliseconds = delay, TimeoutSeconds = seconds, ContextSentences = count }; mtProfiles[mt.Id] = translation;
        var value = original with { Asr = asrProfiles[asr.Id], Translation = translation, AsrProfiles = new(asrProfiles), TranslationProfiles = new(mtProfiles), SourceLanguage = sourceId, TargetLanguage = targetId, Theme = theme.SelectedItem as string ?? "System", FallbackEnabled = fallback.IsChecked == true, FallbackCostConsent = consent.IsChecked == true, FallbackTranslationProviderId = backup.SelectedValue as string ?? "", FontSize = font.SelectedItem as string ?? "medium", ShowSource = source.IsChecked == true, Topmost = pin.IsChecked == true, AudioDeviceId = devices.SelectedValue as string ?? "", BackgroundOpacity = opacity.Value / 100.0 };
        return LanguageRegistry.Find(sourceId) is not null && LanguageRegistry.Find(targetId) is not null ? LanguageSettings.Select(value, sourceId, targetId) : value;
    }
    internal void TestSave() => Save(false);
    void Save(bool close = true) { try { Result = BuildResult(); if (close) DialogResult = true; } catch (ArgumentException e) { error.Text = e.Message; } }
    internal static void TestValidateEndpoint(string providerId, string text, Dictionary<string, string> options) => ValidateEndpoint(providerId, text, options);
    static void ValidateEndpoint(string providerId, string text, Dictionary<string, string> options)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var trimmed = text.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new ArgumentException("接口地址必须是有效的绝对 URL。");
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("接口地址不能包含用户名、查询参数或片段。");

        if (providerId == "OpenAiCompatible" && options.GetValueOrDefault("allowLocalHttp") == "true" && uri.Scheme == "http" && uri.IsLoopback)
            return;

        var expectedScheme = providerId == "VolcengineAsr2" ? "wss" : "https";
        if (uri.Scheme != expectedScheme || uri.IsLoopback)
            throw new ArgumentException($"接口地址须使用安全协议 {expectedScheme}:// 且不能为本机回环地址（如需本机 HTTP 请使用 OpenAiCompatible 并明确启用 allowLocalHttp）。");
    }
    static void Heading(Panel p, string text) => p.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
    static void Field(Panel p, string label, FrameworkElement control) { p.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); p.Children.Add(control); }
    static void Note(Panel p, string text) => p.Children.Add(DesktopTheme.Note(text));
    static void OpenLink(string url) { if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    internal ProviderEditor TestAsr => asr;
    internal ProviderEditor TestTranslation => mt;
    internal CancellationToken TestLifetimeToken => lifetime.Token;
    internal int TestNavigationCount => navigation.Items.Count;
    internal void TestPage(string name) => navigation.SelectedItem = name;
    internal ScrollViewer TestScroller => scroller;
    internal void TestLanguage(string source, string target) { sourceId = source; targetId = target; SyncLanguages(); }
    internal void TestTheme(string value) => theme.SelectedItem = value;
    internal Slider TestOpacitySlider => opacity;
    internal void TestOpacity(double percent) => opacity.Value = percent;
    internal sealed class ProviderEditor
    {
        readonly SettingsWindow owner;
        readonly bool isAsr;
        readonly ComboBox selector = new() { DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" };
        readonly StackPanel fields = new();
        readonly Dictionary<string, Control> controls = new();
        readonly Dictionary<string, TextBox> visibleSecrets = new();
        readonly Dictionary<string, Button> revealButtons = new();
        readonly TextBlock probeStatus = DesktopTheme.Note("尚未验证 · 不会自动联网");
        CancellationTokenSource? request;
        string current = "", credentialAuthority = "", declarationModel = "", declarationEndpoint = "";
        bool rendering;
        public StackPanel Panel { get; } = new();
        public string Id => current;
        public string Capability => ProviderDescriptors.Find(current, isAsr).Capability;
        public ProviderEditor(SettingsWindow owner, bool asr, string id)
        {
            this.owner = owner; isAsr = asr; selector.ItemsSource = asr ? ProviderDescriptors.Asr : ProviderDescriptors.Translation;
            Panel.Children.Add(selector); Panel.Children.Add(fields);
            selector.SelectionChanged += (_, _) => { Snapshot(); CancelRequests(); HideSecrets(); current = (string)selector.SelectedValue; Render(); if (owner.asr is not null && owner.mt is not null) owner.SyncLanguages(); };
            selector.SelectedValue = id;
        }
        public void Select(string id) => selector.SelectedValue = id;
        public void Snapshot()
        {
            if (current.Length == 0 || rendering) return;
            var values = controls.ToDictionary(x => x.Key, x => Get(x.Value)); var endpoint = values.GetValueOrDefault("Endpoint", ""); var authority = Authority(endpoint);
            if (!string.Equals(authority, credentialAuthority, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var field in ProviderDescriptors.Find(current, isAsr).Fields.Where(f => f.Secret)) { values[field.Key] = ""; Set(field.Key, ""); }
                values["ApiKey"] = ""; values["__clearSecrets"] = ""; credentialAuthority = authority; HideSecrets();
            }
            var model = values.GetValueOrDefault("Model", "");
            if (controls.ContainsKey("modelLanguages") && (model != declarationModel || endpoint != declarationEndpoint))
            {
                controls["modelLanguages"].SetCurrentValue(TextBox.TextProperty, ""); values["modelLanguages"] = "";
                probeStatus.Text = "模型或地址已更改 · 语言声明已清除，请重新测试并确认";
                declarationModel = model; declarationEndpoint = endpoint;
            }
            owner.Write(current, isAsr, values);
        }
        static string Authority(string value) => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "";
        public void Set(string key, string value) { var c = controls[key]; if (c is PasswordBox p) p.Password = value; else if (c is ComboBox b) b.Text = value; else ((TextBox)c).Text = value; }
        public string Get(string key) => Get(controls[key]);
        static string Get(Control c) => c is PasswordBox p ? p.Password : c is ComboBox b ? b.Text : ((TextBox)c).Text;
        public bool IsSecret(string key) => controls[key] is PasswordBox;
        public void ClearSecrets()
        {
            Snapshot(); var values = owner.Read(current, isAsr); values["ApiKey"] = "";
            foreach (var key in ProviderDescriptors.Find(current, isAsr).Fields.Where(f => f.Secret).Select(f => f.Key)) values[key] = "";
            values["__clearSecrets"] = ""; owner.Write(current, isAsr, values);
            foreach (var control in controls.Values.OfType<PasswordBox>()) control.Password = ""; HideSecrets(); probeStatus.Text = "凭据已删除 · 尚未验证";
        }
        internal void ToggleSecret(string key)
        {
            var view = visibleSecrets[key]; var control = controls[key];
            if (view.Visibility == Visibility.Visible) { view.Clear(); view.Visibility = Visibility.Collapsed; control.Visibility = Visibility.Visible; revealButtons[key].Content = "显示"; }
            else { view.Text = Get(key); view.Visibility = Visibility.Visible; control.Visibility = Visibility.Collapsed; revealButtons[key].Content = "隐藏"; }
        }
        internal void HideSecrets() { foreach (var pair in visibleSecrets) { pair.Value.Clear(); pair.Value.Visibility = Visibility.Collapsed; controls[pair.Key].Visibility = Visibility.Visible; revealButtons[pair.Key].Content = "显示"; } }
        internal string VisibleSecret(string key) => visibleSecrets[key].Text;
        internal void CancelRequests() { request?.Cancel(); request?.Dispose(); request = null; }
        void Changed() { if (rendering) return; CancelRequests(); probeStatus.Text = "配置已修改 · 尚未验证"; Snapshot(); }
        void Render()
        {
            rendering = true; fields.Children.Clear(); controls.Clear(); visibleSecrets.Clear(); revealButtons.Clear(); var d = ProviderDescriptors.Find(current, isAsr); var values = owner.Read(current, isAsr);
            Note(fields, d.Capability); probeStatus.Text = "尚未验证 · 配置检查不代表请求成功";
            if (!isAsr && d.Id == "OpenAiCompatible")
            {
                var preset = new ComboBox { ItemsSource = ServiceGuides.Presets, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
                Field(fields, "官方 API 预设（选择后清除旧密钥、模型和语言声明）", preset);
                preset.SelectionChanged += (_, _) => { if (preset.SelectedValue is string id) { Snapshot(); CancelRequests(); var currentConfig = owner.mtProfiles.GetValueOrDefault(current) ?? ProviderDescriptors.DefaultTranslation(current); owner.mtProfiles[current] = ServiceGuides.ApplyPreset(currentConfig, id); HideSecrets(); Render(); probeStatus.Text = "已切换预设 · 旧凭据、模型和语言声明已清除，请重新填写"; owner.SyncLanguages(); } };
            }
            foreach (var f in d.Fields.Where(f => f.Key != "Language"))
            {
                Control c = f.Secret ? new PasswordBox() : f.Choices is not null ? new ComboBox { IsEditable = true, ItemsSource = f.Choices } : new TextBox { AcceptsReturn = f.Key == "prompt", TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = f.Key == "prompt" ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, MaxHeight = f.Key == "prompt" ? 160 : double.PositiveInfinity };
                controls[f.Key] = c; Set(f.Key, values.GetValueOrDefault(f.Key, f.DefaultValue));
                Field(fields, f.Label + (f.Required ? " *" : ""), c);
                if (f.Secret)
                {
                    var view = new TextBox { IsReadOnly = true, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; visibleSecrets[f.Key] = view; fields.Children.Add(view);
                    var row = new WrapPanel { Margin = new Thickness(0, -8, 0, 8) }; fields.Children.Add(row);
                    var show = new Button { Content = "显示", ToolTip = "显式显示明文，隐藏时清空视图" }; revealButtons[f.Key] = show; show.Click += (_, _) => ToggleSecret(f.Key); row.Children.Add(show);
                    var reenter = new Button { Content = "重新输入" }; reenter.Click += (_, _) => { HideSecrets(); Set(f.Key, ""); c.Focus(); }; row.Children.Add(reenter);
                }
                if (c is TextBox text) text.TextChanged += (_, _) => Changed();
                else if (c is PasswordBox password) password.PasswordChanged += (_, _) => Changed();
                else if (c is ComboBox combo) combo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Changed()));
            }
            credentialAuthority = Authority(values.GetValueOrDefault("Endpoint", "")); declarationEndpoint = values.GetValueOrDefault("Endpoint", ""); declarationModel = values.GetValueOrDefault("Model", "");
            if (controls.ContainsKey("modelLanguages"))
            {
                Note(fields, "模型语言声明使用统一语言标识，逗号分隔。仅声明你已确认的语言；测试成功后仍需显式确认这一对语言。");
                var confirm = new Button { Content = "将当前源 / 目标加入已确认语言", HorizontalAlignment = HorizontalAlignment.Left };
                confirm.Click += (_, _) => { var ids = Get("modelLanguages").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Concat(new[] { owner.sourceId, owner.targetId }).Where(x => x != "auto").Distinct(); Set("modelLanguages", string.Join(',', ids)); Snapshot(); owner.SyncLanguages(); probeStatus.Text = "已按你的确认更新语言声明 · 不代表其他语言已通过"; }; fields.Children.Add(confirm);
            }
            if (controls.ContainsKey("Endpoint")) Note(fields, "服务主机 / 协议 / 端口改变会清除当前凭据；模型或路径改变会清除模型语言声明。其他服务配置保留。");
            var clear = new Button { Content = "删除当前服务全部凭据", HorizontalAlignment = HorizontalAlignment.Left }; clear.Click += (_, _) => ClearSecrets(); fields.Children.Add(clear);
            var guide = ServiceGuides.For(current); var links = new WrapPanel(); fields.Children.Add(links);
            void Link(string title, string url) { if (url.Length == 0) return; var b = new Button { Content = title }; b.Click += (_, _) => OpenLink(url); links.Children.Add(b); }
            Link("申请 / 控制台 ↗", guide.ApplicationUrl); Link("官方文档 ↗", guide.DocumentationUrl); Link("计费说明 ↗", guide.BillingUrl); Note(fields, guide.Note);
            Note(fields, "以下联网操作仅在点击后执行。使用公开样例，不会发送字幕历史；样例翻译和连接初始化可能按服务规则计费。");
            var probes = new WrapPanel(); fields.Children.Add(probes);
            AddProbe(probes, isAsr ? "连接检查（无音频）" : "连接检查（可能使用公开样例）", async token => isAsr ? await ProviderDiagnostics.TestAsrConnectionAsync(owner.asrProfiles[current], owner.sourceId, token) : await ProviderDiagnostics.TestConnectionAsync(owner.mtProfiles[current], owner.sourceId, owner.targetId, token));
            if (!isAsr)
            {
                AddProbe(probes, "公开样例翻译（可能计费）", token => ProviderDiagnostics.TestTranslationAsync(owner.mtProfiles[current], owner.sourceId, owner.targetId, token));
                if (d.Id is "GroqLlm" or "OpenAiCompatible") AddProbe(probes, "获取文本模型列表", async token => { var models = await ProviderDiagnostics.FetchModelsAsync(owner.mtProfiles[current], token); token.ThrowIfCancellationRequested(); if (controls["Model"] is ComboBox c) c.ItemsSource = models; else controls["Model"].ToolTip = string.Join("\n", models); return new(true, "模型目录请求成功；可手动填写模型 ID：" + string.Join("、", models.Take(12)) + "。语言与质量尚未验证。"); });
                if (d.Id == "DeepL")
                {
                    var cache = DeepLLanguageCache.Status(owner.mtProfiles.GetValueOrDefault(current) ?? ProviderDescriptors.DefaultTranslation(current));
                    Note(fields, cache is null ? "DeepL 语言目录：使用文档快照，可显式刷新一小时内存缓存。" : $"目录 {cache.Version} · {cache.FetchedAtUtc:O} · 目标 {cache.TargetCount}");
                    AddProbe(probes, "刷新 DeepL 语言目录", async token => { var status = await DeepLLanguageCache.RefreshAsync(owner.mtProfiles[current], token, true); token.ThrowIfCancellationRequested(); owner.SyncLanguages(); return new(true, $"语言目录已刷新 · {status.Version} · 有效至 {status.ExpiresAtUtc:O}"); });
                }
            }
            fields.Children.Add(probeStatus); rendering = false; Snapshot();
        }
        void AddProbe(Panel panel, string label, Func<CancellationToken, Task<ProviderProbeResult>> probe)
        {
            var button = new Button { Content = label }; panel.Children.Add(button);
            button.Click += async (_, _) =>
            {
                Snapshot(); CancelRequests(); var active = CancellationTokenSource.CreateLinkedTokenSource(owner.lifetime.Token); request = active;
                button.IsEnabled = false; probeStatus.Text = "正在请求… · 切换服务或关闭窗口会取消";
                try { var result = await probe(active.Token); if (!active.IsCancellationRequested && ReferenceEquals(request, active)) probeStatus.Text = (result.Success ? "请求成功 · " : "最后测试错误 · ") + result.Message + (result.Translation.Length > 0 ? "\n公开样例译文：" + result.Translation : ""); }
                catch (OperationCanceledException) when (active.IsCancellationRequested) { }
                catch { if (!active.IsCancellationRequested) probeStatus.Text = "最后测试错误 · 请求未完成，请检查配置与网络"; }
                finally { if (ReferenceEquals(request, active)) { request = null; active.Dispose(); } button.IsEnabled = true; }
            };
        }
    }
}

