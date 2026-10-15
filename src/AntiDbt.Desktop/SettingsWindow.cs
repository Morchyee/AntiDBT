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
    readonly ComboBox target = new(), font = new(), devices = new();
    readonly TextBox debounce = new(), timeout = new(), context = new();
    readonly CheckBox source = new() { Content = "显示原文" }, pin = new() { Content = "窗口置顶" };
    readonly TextBlock error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    public AppSettings Result { get; private set; }
    public SettingsWindow(AppSettings settings)
    {
        original = Result = settings;
        Closed += (_, _) => lifetime.Cancel();
        asrProfiles = new(settings.AsrProfiles);
        mtProfiles = new(settings.TranslationProfiles);
        asrProfiles[settings.Asr.Provider] = settings.Asr;
        mtProfiles[settings.Translation.Provider] = settings.Translation;
        Title = "antiDBT · 服务与字幕设置";
        Width = 620; Height = 730; MinWidth = 500; MinHeight = 400;
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 38)); Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bottom.Children.Add(actions);
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => DialogResult = false; actions.Children.Add(cancel);
        var save = new Button { Content = "保存设置" }; save.Click += (_, _) => Save(); actions.Children.Add(save);
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Heading(content, "语音识别");
        asr = new ProviderEditor(true, settings.Asr.Provider, ReadAsr, WriteAsr, lifetime.Token); content.Children.Add(asr.Panel);
        Heading(content, "翻译");
        mt = new ProviderEditor(false, settings.Translation.Provider, ReadMt, WriteMt, lifetime.Token); content.Children.Add(mt.Panel);
        Field(content, "增量翻译合并间隔（毫秒，0–2000）", debounce); debounce.Text = settings.Translation.DebounceMilliseconds.ToString();
        Field(content, "翻译请求超时（秒，1–120）", timeout); timeout.Text = settings.Translation.TimeoutSeconds.ToString();
        Field(content, "上下文句数（0–12，服务支持时使用）", context); context.Text = settings.Translation.ContextSentences.ToString();
        var check = new Button { Content = "检查当前配置（不联网）" }; content.Children.Add(check);
        check.Click += (_, _) =>
        {
            try
            {
                asr.Snapshot(); mt.Snapshot(); ValidateEndpoint(asrProfiles[asr.Id].Endpoint); ValidateEndpoint(mtProfiles[mt.Id].Endpoint);
                error.Text = ProviderDescriptors.Validate(asrProfiles[asr.Id]) ?? ProviderDescriptors.Validate(mtProfiles[mt.Id]) ?? "配置完整；认证和真实请求尚未验证";
            }
            catch (ArgumentException e) { error.Text = e.Message; }
        };
        Note(content, "Seed LiveInterpret 2.0：尚未启用，服务事件协议仍需验证。配置检查不会发送语音或翻译请求。");
        Heading(content, "音频与字幕");
        devices.DisplayMemberPath = "Name"; devices.SelectedValuePath = "Id";
        devices.Items.Add(new AudioDevice("", "跟随启动时的默认输出设备", true));
        try { foreach (var device in WasapiAudioSource.ListDevices()) devices.Items.Add(device); }
        catch { Note(content, "无法枚举音频设备，可保留默认输出设备后重试。"); }
        devices.SelectedValue = settings.AudioDeviceId; if (devices.SelectedIndex < 0) devices.SelectedIndex = 0;
        Field(content, "系统音频回环设备", devices);
        foreach (var value in new[] { "zh", "en" }) target.Items.Add(value);
        target.SelectedItem = settings.TargetLanguage; Field(content, "翻译目标语言（zh 中文 / en 英文）", target);
        foreach (var value in new[] { "small", "medium", "large" }) font.Items.Add(value);
        font.SelectedItem = settings.FontSize; Field(content, "字号（小 16 / 中 20 / 大 32）", font);
        source.IsChecked = settings.ShowSource; pin.IsChecked = settings.Topmost; content.Children.Add(source); content.Children.Add(pin);
        Note(content, "凭据使用 Windows DPAPI 按当前用户加密保存。每个服务独立保留配置；服务账户 JSON 请直接粘贴。点击主窗口开始后才会采集并发送音频。");
    }
    Dictionary<string, string> ReadAsr(string id)
    {
        var c = asrProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultAsr(id);
        return Values(c.ApiKey, c.Endpoint, c.Model, c.Language, c.Options, c.Secrets);
    }
    Dictionary<string, string> ReadMt(string id)
    {
        var c = mtProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultTranslation(id);
        return Values(c.ApiKey, c.Endpoint, c.Model, "", c.Options, c.Secrets);
    }
    static Dictionary<string, string> Values(string key, string endpoint, string model, string language, Dictionary<string, string> options, Dictionary<string, string> secrets)
    {
        var values = new Dictionary<string, string>(options); foreach (var pair in secrets) values[pair.Key] = pair.Value;
        values["ApiKey"] = key; values["Endpoint"] = endpoint; values["Model"] = model; values["Language"] = language; return values;
    }
    void WriteAsr(string id, Dictionary<string, string> v)
    {
        var c = asrProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultAsr(id);
        var (options, secrets) = Dictionaries(ProviderDescriptors.Find(id, true), v, c.Options, c.Secrets);
        asrProfiles[id] = c with { ApiKey = v.GetValueOrDefault("ApiKey", ""), Endpoint = v.GetValueOrDefault("Endpoint", ""), Model = v.GetValueOrDefault("Model", ""), Language = v.GetValueOrDefault("Language", ""), Options = options, Secrets = secrets };
    }
    void WriteMt(string id, Dictionary<string, string> v)
    {
        var c = mtProfiles.GetValueOrDefault(id) ?? ProviderDescriptors.DefaultTranslation(id);
        var (options, secrets) = Dictionaries(ProviderDescriptors.Find(id, false), v, c.Options, c.Secrets);
        mtProfiles[id] = c with { ApiKey = v.GetValueOrDefault("ApiKey", ""), Endpoint = v.GetValueOrDefault("Endpoint", ""), Model = v.GetValueOrDefault("Model", ""), Options = options, Secrets = secrets };
    }
    static (Dictionary<string, string>, Dictionary<string, string>) Dictionaries(ProviderDescriptor d, Dictionary<string, string> values, Dictionary<string, string> oldOptions, Dictionary<string, string> oldSecrets)
    {
        var options = new Dictionary<string, string>(oldOptions); var secrets = values.ContainsKey("__clearSecrets") ? new Dictionary<string, string>() : new Dictionary<string, string>(oldSecrets);
        foreach (var f in d.Fields.Where(f => f.Key is not ("ApiKey" or "Endpoint" or "Model" or "Language"))) (f.Secret ? secrets : options)[f.Key] = values.GetValueOrDefault(f.Key, "");
        return (options, secrets);
    }
    internal void TestSave() => Save(false);
    void Save(bool close = true)
    {
        try
        {
            asr.Snapshot(); mt.Snapshot();
            ValidateEndpoint(asrProfiles[asr.Id].Endpoint); ValidateEndpoint(mtProfiles[mt.Id].Endpoint);
            if (!int.TryParse(debounce.Text, out var delay) || delay < 0 || delay > 2000 || !int.TryParse(timeout.Text, out var seconds) || seconds < 1 || seconds > 120 || !int.TryParse(context.Text, out var count) || count < 0 || count > 12) throw new ArgumentException("请填写有效的合并间隔、超时和上下文句数。");
            var translation = mtProfiles[mt.Id] with { DebounceMilliseconds = delay, TimeoutSeconds = seconds, ContextSentences = count }; mtProfiles[mt.Id] = translation;
            Result = original with { Asr = asrProfiles[asr.Id], Translation = translation, AsrProfiles = new(asrProfiles), TranslationProfiles = new(mtProfiles), TargetLanguage = target.SelectedItem as string ?? "zh", FontSize = font.SelectedItem as string ?? "medium", ShowSource = source.IsChecked == true, Topmost = pin.IsChecked == true, AudioDeviceId = devices.SelectedValue as string ?? "" };
            if (close) DialogResult = true;
        }
        catch (ArgumentException e) { error.Text = e.Message; }
    }
    static void ValidateEndpoint(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "wss") || uri.IsLoopback || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new ArgumentException("接口地址须使用公开 HTTPS/WSS，且不能包含用户名、查询参数或片段。");
    }
    static void Heading(Panel p, string text) => p.Children.Add(new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 9) });
    static void Field(Panel p, string label, Control control) { p.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray }); p.Children.Add(control); }
    static void Note(Panel p, string text) => p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 10) });
    internal ProviderEditor TestAsr => asr;
    internal ProviderEditor TestTranslation => mt;
    internal CancellationToken TestLifetimeToken => lifetime.Token;
    internal sealed class ProviderEditor
    {
        readonly bool isAsr;
        readonly Func<string, Dictionary<string, string>> read;
        readonly Action<string, Dictionary<string, string>> write;
        readonly ComboBox selector = new() { DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" };
        readonly StackPanel fields = new();
        readonly Dictionary<string, Control> controls = new();
        string current = "", credentialAuthority = "";
        readonly CancellationToken lifetimeToken;
        public StackPanel Panel { get; } = new();
        public string Id => current;
        public string Capability => ProviderDescriptors.Find(current, isAsr).Capability;
        public ProviderEditor(bool asr, string id, Func<string, Dictionary<string, string>> read, Action<string, Dictionary<string, string>> write, CancellationToken token)
        {
            isAsr = asr; this.read = read; this.write = write; lifetimeToken = token;
            selector.ItemsSource = asr ? ProviderDescriptors.Asr : ProviderDescriptors.Translation;
            Panel.Children.Add(selector); Panel.Children.Add(fields);
            selector.SelectionChanged += (_, _) => { Snapshot(); current = (string)selector.SelectedValue; Render(); };
            selector.SelectedValue = id;
        }
        public void Select(string id) => selector.SelectedValue = id;
        public void Snapshot()
        {
            if (current.Length == 0) return;
            var values = controls.ToDictionary(x => x.Key, x => Get(x.Value));
            var authority = Authority(values.GetValueOrDefault("Endpoint", ""));
            if (!string.Equals(authority, credentialAuthority, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var field in ProviderDescriptors.Find(current, isAsr).Fields.Where(f => f.Secret)) { values[field.Key] = ""; Set(field.Key, ""); }
                values["ApiKey"] = ""; values["__clearSecrets"] = "";
                credentialAuthority = authority;
            }
            write(current, values);
        }
        static string Authority(string value) => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "";
        public void Set(string key, string value) { var c = controls[key]; if (c is PasswordBox p) p.Password = value; else if (c is ComboBox b) b.Text = value; else ((TextBox)c).Text = value; }
        public string Get(string key) => Get(controls[key]);
        static string Get(Control c) => c is PasswordBox p ? p.Password : c is ComboBox b ? b.Text : ((TextBox)c).Text;
        public bool IsSecret(string key) => controls[key] is PasswordBox;
        public void ClearSecrets()
        {
            Snapshot(); var values = read(current); values["ApiKey"] = "";
            foreach (var key in ProviderDescriptors.Find(current, isAsr).Fields.Where(f => f.Secret).Select(f => f.Key)) values[key] = "";
            values["__clearSecrets"] = ""; write(current, values);
            // Clear retained secret dictionary entries as well as rendered fields.
            foreach (var control in controls.Values.OfType<PasswordBox>()) control.Password = "";
        }
        void Render()
        {
            fields.Children.Clear(); controls.Clear(); var d = ProviderDescriptors.Find(current, isAsr); var values = read(current);
            Note(fields, d.Capability + "\n语言：" + string.Join("、", d.Languages));
            foreach (var f in d.Fields)
            {
                Control c = f.Secret ? new PasswordBox() : f.Choices is not null ? new ComboBox { IsEditable = true, ItemsSource = f.Choices } : new TextBox { AcceptsReturn = f.Key == "prompt", TextWrapping = TextWrapping.Wrap };
                controls[f.Key] = c; Set(f.Key, values.GetValueOrDefault(f.Key, f.DefaultValue)); Field(fields, f.Label + (f.Required ? " *" : ""), c);
            }
            credentialAuthority = Authority(values.GetValueOrDefault("Endpoint", ""));
            if (controls.GetValueOrDefault("Endpoint") is TextBox endpoint) endpoint.TextChanged += (_, _) => Snapshot();
            else if (controls.GetValueOrDefault("Endpoint") is ComboBox endpointChoice) endpointChoice.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => Snapshot()));
            if (controls.ContainsKey("Endpoint")) Note(fields, "接口地址的服务地址改变时将清除当前凭据，请为新地址重新填写；同一服务地址的路径修改保留凭据。");
            var clear = new Button { Content = "清除当前服务凭据" }; clear.Click += (_, _) => ClearSecrets(); fields.Children.Add(clear);
            if (d.Id == "GroqLlm")
            {
                var fetch = new Button { Content = "获取 Groq 文本模型列表（联网）" }; var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; fields.Children.Add(fetch); fields.Children.Add(status);
                fetch.Click += async (_, _) =>
                {
                    fetch.IsEnabled = false; selector.IsEnabled = false;
                    try
                    {
                        var models = await ProviderDescriptors.FetchGroqModelsAsync(Get("ApiKey"), lifetimeToken);
                        if (lifetimeToken.IsCancellationRequested) return;
                        var value = Get("Model"); var index = fields.Children.IndexOf(controls["Model"]);
                        var model = new ComboBox { IsEditable = true, ItemsSource = models, Text = value }; fields.Children.RemoveAt(index); fields.Children.Insert(index, model); controls["Model"] = model;
                        status.Text = "模型列表请求成功；候选项仍需确认文本能力。翻译认证和真实翻译尚未验证。";
                    }
                    catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
                    catch (ProviderException e) { if (!lifetimeToken.IsCancellationRequested) status.Text = e.Message; }
                    catch { if (!lifetimeToken.IsCancellationRequested) status.Text = "模型列表获取失败，请检查配置或网络后重试。"; }
                    finally { if (!lifetimeToken.IsCancellationRequested) { fetch.IsEnabled = true; selector.IsEnabled = true; } }
                };
            }
        }
    }
}
