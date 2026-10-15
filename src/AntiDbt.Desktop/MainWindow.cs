using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AntiDbt.Core;
using AntiDbt.Providers;
using AntiDbt.Windows;
namespace AntiDbt.Desktop;
public sealed class MainWindow : Window
{
    readonly SettingsStore settingsStore = new(Path.Combine(AppContext.BaseDirectory, "data"));
    readonly bool persistSettings;
    AppSettings settings = ProviderCatalog.DefaultSettings();
    readonly SubtitleStore store = new();
    readonly ObservableCollection<SubtitleParagraph> paragraphs = new();
    double historyFont; bool historyShowSource;
    CaptionPipeline? pipeline; bool busy, closing, allowClose, expanded, follow = true, renderQueued;
    readonly TextBox source = CaptionText(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)));
    readonly TextBox target = CaptionText(Brushes.White);
    static TextBox CaptionText(Brush brush) => new() { IsReadOnly = true, IsReadOnlyCaretVisible = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0), Foreground = brush, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBlock status = new() { Text = "准备就绪 · 点击开始采集系统音频", Foreground = Brushes.LightGray, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock metrics = new() { Foreground = Brushes.Gray, FontSize = 10 };
    readonly ProgressBar level = new() { Minimum = 0, Maximum = 1, Height = 3, Width = 42, Margin = new Thickness(6, 0, 4, 0), ToolTip = "系统音频电平" };
    readonly Button start = new() { Content = "开始" }, expand = new() { Content = "展开" }, bottom = new() { Content = "↓ 回到底部", Visibility = Visibility.Collapsed };
    readonly ListBox history = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    readonly StackPanel compact = new() { Margin = new Thickness(2, 14, 2, 12) };
    readonly ScrollViewer sourceViewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly ScrollViewer targetViewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow(bool testMode = false)
    {
        persistSettings = !testMode;
        Title = "antiDBT · 实时双语字幕";
        Width = 600;
        MinWidth = 420;
        MaxWidth = 1100;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Foreground = Brushes.White;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(30, 30, 34)), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 9, 14, 10), BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 64)), BorderThickness = new Thickness(1) };
        Content = border;
        var grid = new Grid();
        border.Child = grid;
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var toolbar = new DockPanel { LastChildFill = false, MinHeight = 24, Background = Brushes.Transparent };
        grid.Children.Add(toolbar);
        toolbar.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock || ReferenceEquals(e.OriginalSource, toolbar)) { try { DragMove(); } catch (InvalidOperationException) { } } };
        var title = new TextBlock { Text = "双语字幕", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray, Margin = new Thickness(4, 0, 14, 0) };
        toolbar.Children.Add(title);
        void Tool(Button b, Action action)
        {
            toolbar.Children.Add(b);
            b.Click += (_, _) => action();
        }
        Tool(start, async () => await ToggleCaptureAsync());
        Tool(expand, () => SetExpanded(!expanded));
        Tool(new()
        {
            Content = "原文",
            ToolTip = "显示 / 隐藏原文"
        }, async () => { settings = settings with { ShowSource = !settings.ShowSource }; Render(); await SaveSettingsAsync(); });
        Tool(new()
        {
            Content = "字号"
        }, async () => { settings = settings with { FontSize = settings.FontSize == "small" ? "medium" : settings.FontSize == "medium" ? "large" : "small" }; Render(); await SaveSettingsAsync(); });
        Tool(new()
        {
            Content = "中/英",
            ToolTip = "切换目标语言并停止当前会话"
        }, async () => await RunActionAsync(async () => { await StopCaptureCoreAsync(); settings = settings with { TargetLanguage = settings.TargetLanguage == "zh" ? "en" : "zh" }; if (await SaveSettingsAsync()) status.Text = "目标语言已切换，点击开始新会话"; }));
        Tool(new()
        {
            Content = "⚙",
            ToolTip = "API 与字幕设置"
        }, async () => await OpenSettingsAsync());
        var close = new Button { Content = "×", ToolTip = "停止采集并关闭" };
        DockPanel.SetDock(close, Dock.Right);
        Tool(close, Close);
        grid.Children.Add(compact);
        Grid.SetRow(compact, 1);
        sourceViewport.Content = source;
        targetViewport.Content = target;
        compact.Children.Add(sourceViewport);
        compact.Children.Add(targetViewport);
        history.Height = 330;
        ScrollViewer.SetHorizontalScrollBarVisibility(history, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(history, ScrollBarVisibility.Auto);
        history.ItemsSource = paragraphs;
        VirtualizingPanel.SetIsVirtualizing(history, true);
        VirtualizingPanel.SetVirtualizationMode(history, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(history, true);
        history.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.ExtentHeightChange == 0 && (Math.Abs(e.VerticalChange) > 0)) follow = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1; bottom.Visibility = expanded && !follow ? Visibility.Visible : Visibility.Collapsed; }));
        grid.Children.Add(history);
        Grid.SetRow(history, 1);
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Click += (_, _) => { follow = true; ScrollBottom(); };
        grid.Children.Add(bottom);
        Grid.SetRow(bottom, 2);
        var footer = new DockPanel();
        grid.Children.Add(footer);
        Grid.SetRow(footer, 3);
        DockPanel.SetDock(level, Dock.Right);
        footer.Children.Add(level);
        DockPanel.SetDock(metrics, Dock.Right);
        footer.Children.Add(metrics);
        footer.Children.Add(status);
        ContextMenu = new();
        MenuItem demo = new()
        {
            Header = "本地演示（不采集音频、不访问网络）"
        };
        demo.Click += async (_, _) => await StartDemoAsync();
        ContextMenu.Items.Add(demo);
        MenuItem top = new()
        {
            Header = "窗口置顶",
            IsCheckable = true,
            IsChecked = true
        };
        top.Click += async (_, _) => { Topmost = top.IsChecked; settings = settings with { Topmost = Topmost }; await SaveSettingsAsync(); };
        ContextMenu.Items.Add(top);
        MenuItem copy = new()
        {
            Header = "复制选中字幕 / 当前字幕"
        };
        copy.Click += (_, _) => CopyCaption();
        ContextMenu.Items.Add(copy);
        KeyDown += (_, e) => { if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (Keyboard.FocusedElement is TextBox text && text.SelectionLength > 0) return; CopyCaption(); e.Handled = true; } };
        store.Changed += QueueRender;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
        monitor.Tick += (_, _) => { if (pipeline is null) return; var a = pipeline.AsrLatency.Snapshot(); var t = pipeline.TranslationLatency.Snapshot(); metrics.Text = $"p95 ASR≈{(a.Count > 0 ? a.P95Milliseconds.ToString("0") + "ms" : "—")} · MT {(t.Count > 0 ? t.P95Milliseconds.ToString("0") + "ms" : "—")}"; };
        monitor.Start();
        Render();
    }
    async Task InitializeAsync()
    {
        try
        {
            var loaded = persistSettings ? await settingsStore.LoadAsync() : new AppSettings();
            settings = string.IsNullOrEmpty(loaded.Asr.Provider) ? ProviderCatalog.DefaultSettings() : loaded;
            Topmost = persistSettings && settings.Topmost;
            var work = SystemParameters.WorkArea;
            Left = Math.Clamp(settings.WindowLeft ?? work.Left + (work.Width - Width) / 2, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(settings.WindowTop ?? work.Bottom - 260, work.Top, Math.Max(work.Top, work.Bottom - 250));
            if (settingsStore.Warning is not null)
                status.Text = "设置无法读取或解密，已恢复默认值";
            Render();
        }
        catch { status.Text = "设置读取失败，请打开设置重新配置"; }
    }
    async Task<bool> SaveSettingsAsync()
    {
        if (!persistSettings)
            return true;
        try
        {
            await settingsStore.SaveAsync(settings);
            return true;
        }
        catch { status.Text = "设置保存失败，请检查软件目录写入权限"; return false; }
    }
    async Task RunActionAsync(Func<Task> action)
    {
        if (busy || closing)
            return;
        busy = true;
        start.IsEnabled = false;
        try
        {
            await action();
        }
        catch { status.Text = "操作失败，请检查音频资源或服务配置后重试"; start.Content = "重试"; }
        finally { busy = false; start.IsEnabled = true; }
    }
    Task OpenSettingsAsync() => RunActionAsync(OpenSettingsCoreAsync);
    async Task OpenSettingsCoreAsync()
    {
        await StopCaptureCoreAsync();
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            settings = dialog.Result;
            store.BeginSession();
            Topmost = settings.Topmost;
            Render();
            if (await SaveSettingsAsync())
                status.Text = "设置已保存 · 点击开始";
        }
    }
    Task ToggleCaptureAsync() => RunActionAsync(async () =>
    {
        if (pipeline is not null && !pipeline.Completion.IsCompleted)
        {
            await StopCaptureCoreAsync();
            return;
        }
        if (ProviderDescriptors.Validate(settings.Asr) is not null || ProviderDescriptors.Validate(settings.Translation) is not null)
        {
            await OpenSettingsCoreAsync();
            return;
        }
        await StartPipelineAsync(new WasapiAudioSource(settings.AudioDeviceId), ProviderCatalog.CreateAsr(settings.Asr), ProviderCatalog.CreateTranslator(settings.Translation));
    });
    async Task StartPipelineAsync(IAudioSource audio, IAsrProvider asr, ITranslationProvider translator)
    {
        if (pipeline is not null)
            await pipeline.DisposeAsync();
        pipeline = new(audio, asr, translator, settings, store);
        var current = pipeline;
        current.StatusChanged += s => Dispatcher.BeginInvoke(() => { if (!ReferenceEquals(current, pipeline)) return; status.Text = s.Message + (s.DroppedChunks > 0 ? $" · 已丢弃 {s.DroppedChunks} 个过期音频块" : ""); start.Content = s.State is PipelineState.Stopped or PipelineState.Error ? "重试" : "停止"; });
        current.AudioLevelChanged += rms => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(current, pipeline)) level.Value = Math.Min(1, rms * 5); });
        await current.StartAsync();
        start.Content = "停止";
    }
    async Task StopCaptureCoreAsync()
    {
        if (pipeline is null)
            return;
        var previous = pipeline;
        await previous.DisposeAsync();
        if (ReferenceEquals(previous, pipeline))
            pipeline = null;
        level.Value = 0;
        start.Content = "开始";
    }
    public Task StartDemoAsync() => RunActionAsync(async () => { await StopCaptureCoreAsync(); await StartPipelineAsync(new DemoAudioSource(), new DemoAsrProvider(), new DemoTranslationProvider()); status.Text = "本地演示 · 合成字幕，无音频采集或网络请求"; });
    void QueueRender()
    {
        if (Dispatcher.HasShutdownStarted)
            return;
        lock (store)
        {
            if (renderQueued)
                return;
            renderQueued = true;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { lock (store) renderQueued = false; Render(); });
    }
    public void SetExpanded(bool value)
    {
        expanded = value;
        expand.Content = value ? "收起" : "展开";
        compact.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        history.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }
    void Render()
    {
        var size = settings.FontSize == "small" ? 16 : settings.FontSize == "large" ? 32 : 20;
        var line = size == 16 ? 24 : size == 32 ? 44 : 28;
        source.FontSize = target.FontSize = size;
        TextBlock.SetLineHeight(source, line);
        TextBlock.SetLineHeight(target, line);
        sourceViewport.MaxHeight = targetViewport.MaxHeight = line * 2;
        sourceViewport.Visibility = settings.ShowSource ? Visibility.Visible : Visibility.Collapsed;
        var rows = store.Snapshot();
        var text = SubtitleProjection.Compact(rows);
        source.Text = text.Source.Length > 0 ? text.Source : "系统播放的语音将实时显示在这里";
        target.Text = text.Translation.Length > 0 ? text.Translation + (text.TranslationPending ? "  · 待更新" : "") : rows.Count > 0 ? "等待译文…" : "译文将在下方显示";
        if (expanded)
        {
            var updated = SubtitleProjection.Expanded(rows);
            for (int i = 0; i < updated.Count; i++)
            {
                if (i >= paragraphs.Count)
                    paragraphs.Add(updated[i]);
                else if (paragraphs[i] != updated[i])
                    paragraphs[i] = updated[i];
            } while (paragraphs.Count > updated.Count)
                paragraphs.RemoveAt(paragraphs.Count - 1);
            if (history.ItemTemplate is null || historyFont != size || historyShowSource != settings.ShowSource)
            {
                history.ItemTemplate = ParagraphTemplate(size, line, settings.ShowSource);
                historyFont = size;
                historyShowSource = settings.ShowSource;
            }
            if (follow)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollBottom);
        }
        bottom.Visibility = expanded && !follow ? Visibility.Visible : Visibility.Collapsed;
        if (!expanded)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { sourceViewport.ScrollToBottom(); targetViewport.ScrollToBottom(); });
    }
    static DataTemplate ParagraphTemplate(double size, double line, bool showSource)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(4, 10, 4, 12));
        void Add(string property, Brush brush)
        {
            var text = new FrameworkElementFactory(typeof(TextBox));
            text.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(property));
            text.SetValue(TextBox.IsReadOnlyProperty, true);
            text.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            text.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
            text.SetValue(TextBox.PaddingProperty, new Thickness(0));
            text.SetValue(TextBox.MarginProperty, new Thickness(0));
            text.SetValue(TextBox.FontSizeProperty, size);
            text.SetValue(TextBlock.LineHeightProperty, line);
            text.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBox.ForegroundProperty, brush);
            panel.AppendChild(text);
        }
        if (showSource)
            Add(nameof(SubtitleParagraph.Source), new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)));
        Add(nameof(SubtitleParagraph.Translation), Brushes.White);
        var pending = new FrameworkElementFactory(typeof(TextBlock));
        pending.SetValue(TextBlock.TextProperty, "译文待更新");
        pending.SetValue(TextBlock.FontSizeProperty, 11d);
        pending.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
        pending.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(SubtitleParagraph.TranslationPending)) { Converter = new BooleanToVisibilityConverter() });
        panel.AppendChild(pending);
        return new()
        {
            VisualTree = panel
        };
    }
    void ScrollBottom()
    {
        if (history.Items.Count > 0)
            history.ScrollIntoView(history.Items[history.Items.Count - 1]);
        bottom.Visibility = Visibility.Collapsed;
    }
    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
            return;
        e.Cancel = true;
        if (closing)
            return;
        closing = true;
        monitor.Stop();
        IsEnabled = false;
        try
        {
            while (busy)
                await Task.Delay(20);
            await StopCaptureCoreAsync();
            settings = settings with
            {
                WindowLeft = Left,
                WindowTop = Top
            };
            await SaveSettingsAsync();
        }
        catch { status.Text = "关闭时音频资源释放异常"; }
        finally { allowClose = true; Close(); }
    }
    void CopyCaption()
    {
        try
        {
            var p = history.SelectedItem as SubtitleParagraph ?? SubtitleProjection.Compact(store.Snapshot());
            Clipboard.SetText(p.Source + Environment.NewLine + p.Translation);
        }
        catch { status.Text = "无法写入剪贴板，请重试"; }
    }
    internal SubtitleStore TestStore => store;
    internal string TestSource => source.Text;
    internal string TestTarget => target.Text;
    internal int TestHistoryCount => history.Items.Count;
    internal bool TestFollowing => follow;
    internal ScrollViewer? TestScroller => FindScroller(history);
    internal double TestCompactOffset => sourceViewport.VerticalOffset;
    static ScrollViewer? FindScroller(DependencyObject element)
    {
        if (element is ScrollViewer viewer)
            return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var found = FindScroller(VisualTreeHelper.GetChild(element, i));
            if (found is not null)
                return found;
        }
        return null;
    }
}
