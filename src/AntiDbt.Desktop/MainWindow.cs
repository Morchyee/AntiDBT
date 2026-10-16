using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AntiDbt.Core;
using AntiDbt.Providers;
using AntiDbt.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AntiDbt.Desktop;

public sealed class MainWindow : Window
{
    readonly SettingsStore settingsStore = new(Path.Combine(AppContext.BaseDirectory, "data"));
    readonly bool persistSettings;
    AppSettings settings = ProviderCatalog.DefaultSettings();
    readonly SubtitleStore store;
    readonly ObservableCollection<SubtitleParagraph> paragraphs = new();
    double historyFont;
    bool historyShowSource;
    CaptionPipeline? pipeline;
    bool busy, closing, allowClose, expanded, follow = true, renderQueued, userResized, pipelineCleanupFailed;
    long lastAudibleTicks;
    PipelineState currentPipelineState = PipelineState.Stopped;
    long lastDroppedChunks;

    readonly TextBox source = CaptionText(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)));
    readonly TextBox target = CaptionText(Brushes.White);
    static TextBox CaptionText(Brush brush) => new()
    {
        IsReadOnly = true,
        IsReadOnlyCaretVisible = false,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        Margin = new Thickness(0),
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    // Compact Top-Right Status Badge
    string statusMessage = "准备就绪 · 点击开始采集系统音频";
    readonly Border statusDot = new()
    {
        Width = 7,
        Height = 7,
        CornerRadius = new CornerRadius(3.5),
        Background = new SolidColorBrush(Color.FromRgb(158, 158, 158)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 5, 0)
    };
    readonly TextBlock statusText = new()
    {
        Text = "未开始",
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Brushes.Gray
    };
    readonly StackPanel statusBadge = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 6, 0),
        ToolTip = "准备就绪 · 点击开始采集系统音频"
    };

    readonly Button start = new() { Content = "开始" }, expand = new() { Content = "展开" };
    readonly Button sourceBtn = new() { Content = "原文" }, fontBtn = new() { Content = "字号" }, settingsBtn = new() { Content = "⚙" }, closeBtn = new() { Content = "×" };
    readonly TextBlock langTitle = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBlock sourceBoxLabel = new(), targetBoxLabel = new();
    readonly TextBlock providerNoticeText = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button swap = new() { Content = "⇄" };
    readonly Button bottom = new()
    {
        Content = "↓ 回到底部",
        Visibility = Visibility.Collapsed,
        Padding = new Thickness(10, 4, 10, 4),
        Cursor = Cursors.Hand,
        ToolTip = "有新的字幕，点击滚动回最新内容"
    };
    readonly ListBox history = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    readonly StackPanel compact = new() { Margin = new Thickness(4, 10, 4, 8) };
    readonly ScrollViewer sourceViewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly ScrollViewer targetViewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

    // Integrated Language Trigger & Popup
    readonly Button languageTrigger = new()
    {
        Content = "自动检测 → 中文 ▾",
        ToolTip = "选择字幕语言方向",
        Padding = new Thickness(8, 3, 8, 3),
        Margin = new Thickness(2, 0, 2, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly Popup languagePopup = new()
    {
        StaysOpen = false,
        AllowsTransparency = true,
        Placement = PlacementMode.Bottom
    };
    readonly LanguagePicker sourceLanguage = new(), targetLanguage = new();

    internal Func<AppSettings, SubtitleStore, CaptionPipeline>? TestPipelineFactory { get; set; }
    readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(bool testMode = false)
    {
        persistSettings = !testMode;
        settings = LanguageSettings.Upgrade(settings);
        UiText.CurrentLanguage = settings.UiLanguage;
        store = new(settings.MaxHistory > 0 ? settings.MaxHistory : 2000);
        Resources["BoolToVis"] = new BooleanToVisibilityConverter();
        DesktopTheme.Apply(this, settings.Theme, settings.BackgroundOpacity);

        Title = UiText.AppTitle;
        Width = 680;
        MinWidth = 520;
        MaxWidth = 2400;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 1600;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        Foreground = Brushes.White;

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8, 14, 10),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        Content = border;

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button && e.OriginalSource is not TextBox && e.OriginalSource is not ScrollViewer && e.OriginalSource is not ListBoxItem)
            {
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        };

        var grid = new Grid();
        border.Child = grid;
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); // Row 0: Top Toolbar
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); // Row 1: Subtitle Viewport Area (dynamically adapts and fills 100% available space)

        // Assemble statusBadge
        statusBadge.Children.Add(statusDot);
        statusBadge.Children.Add(statusText);

        // Top Toolbar
        var toolbar = new DockPanel { MinHeight = 32, LastChildFill = false };
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        // Right side: Close button & Status Indicator
        closeBtn.MinWidth = 28;
        closeBtn.Padding = new Thickness(6, 2, 6, 2);
        closeBtn.Margin = new Thickness(4, 0, 0, 0);
        closeBtn.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(closeBtn, Dock.Right);
        closeBtn.Click += (_, _) => Close();
        toolbar.Children.Add(closeBtn);

        DockPanel.SetDock(statusBadge, Dock.Right);
        toolbar.Children.Add(statusBadge);

        // Left side: Title
        var title = new TextBlock
        {
            Text = "antiDBT",
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        DockPanel.SetDock(title, Dock.Left);
        toolbar.Children.Add(title);

        static Border Divider() => new()
        {
            Width = 1,
            Height = 14,
            Background = Brushes.Gray,
            Opacity = 0.35,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };

        var div1 = Divider();
        DockPanel.SetDock(div1, Dock.Left);
        toolbar.Children.Add(div1);

        // Language Trigger Button in Toolbar
        DockPanel.SetDock(languageTrigger, Dock.Left);
        toolbar.Children.Add(languageTrigger);

        var div2 = Divider();
        DockPanel.SetDock(div2, Dock.Left);
        toolbar.Children.Add(div2);

        // Actions: Start, Expand, Source, FontSize, Settings
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(actions, Dock.Left);
        toolbar.Children.Add(actions);

        void Tool(Button b, Action action)
        {
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Padding = new Thickness(7, 3, 7, 3);
            b.Margin = new Thickness(2, 0, 2, 0);
            actions.Children.Add(b);
            b.Click += (_, _) => action();
        }
        Tool(start, async () => await ToggleCaptureAsync());
        Tool(expand, () => SetExpanded(!expanded));
        Tool(sourceBtn, async () => { settings = settings with { ShowSource = !settings.ShowSource }; Render(); await SaveSettingsAsync(); });
        Tool(fontBtn, async () => { settings = settings with { FontSize = settings.FontSize == "small" ? "medium" : settings.FontSize == "medium" ? "large" : "small" }; Render(); await SaveSettingsAsync(); });
        Tool(settingsBtn, async () => await OpenSettingsAsync());

        // Language Popup Card
        languagePopup.PlacementTarget = languageTrigger;
        var langPanel = new StackPanel { Width = 370 };
        langTitle.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        langPanel.Children.Add(langTitle);

        var langGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        langGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        langGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        langGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });

        var sourceBox = new StackPanel();
        sourceBoxLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        sourceBoxLabel.Margin = new Thickness(0, 0, 0, 4);
        sourceBox.Children.Add(sourceBoxLabel);
        sourceBox.Children.Add(sourceLanguage);

        var targetBox = new StackPanel();
        targetBoxLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        targetBoxLabel.Margin = new Thickness(0, 0, 0, 4);
        targetBox.Children.Add(targetBoxLabel);
        targetBox.Children.Add(targetLanguage);

        swap.VerticalAlignment = VerticalAlignment.Bottom;
        swap.Margin = new Thickness(8, 0, 8, 4);
        swap.Padding = new Thickness(8, 6, 8, 6);

        langGrid.Children.Add(sourceBox);
        langGrid.Children.Add(swap);
        langGrid.Children.Add(targetBox);
        Grid.SetColumn(swap, 1);
        Grid.SetColumn(targetBox, 2);
        langPanel.Children.Add(langGrid);

        providerNoticeText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        providerNoticeText.FontSize = 11.5;
        providerNoticeText.Margin = new Thickness(0, 4, 0, 0);
        langPanel.Children.Add(providerNoticeText);

        var langCard = DesktopTheme.Card(langPanel, new Thickness(14));
        langCard.Margin = new Thickness(0);
        languagePopup.Child = langCard;

        languageTrigger.Click += (_, _) =>
        {
            languagePopup.IsOpen = !languagePopup.IsOpen;
            if (languagePopup.IsOpen)
                RefreshLanguages();
        };
        sourceLanguage.Selected += async id =>
        {
            languagePopup.IsOpen = false;
            await ApplyLanguageAsync(id, settings.TargetLanguage);
        };
        targetLanguage.Selected += async id =>
        {
            languagePopup.IsOpen = false;
            await ApplyLanguageAsync(settings.SourceLanguage, id);
        };
        swap.Click += async (_, _) =>
        {
            await SwapLanguagesAsync();
        };

        grid.Children.Add(languagePopup);

        // Subtitle Viewports
        grid.Children.Add(compact);
        Grid.SetRow(compact, 1);
        sourceViewport.Content = source;
        targetViewport.Content = target;
        compact.Children.Add(sourceViewport);
        compact.Children.Add(targetViewport);

        // History Subtitle View (Used in both Compact and Expanded modes)
        history.MaxHeight = 76;
        history.Height = double.NaN;
        history.VerticalAlignment = VerticalAlignment.Stretch;
        history.HorizontalAlignment = HorizontalAlignment.Stretch;
        history.Margin = new Thickness(4, 4, 4, 4);
        ScrollViewer.SetHorizontalScrollBarVisibility(history, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(history, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(history, true);
        VirtualizingPanel.SetVirtualizationMode(history, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(history, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(history, true);

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0, 1, 0, 1)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.FocusVisualStyleProperty, null));
        var ct = new ControlTemplate(typeof(ListBoxItem));
        ct.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        itemStyle.Setters.Add(new Setter(ListBoxItem.TemplateProperty, ct));
        history.ItemContainerStyle = itemStyle;

        history.ItemsSource = paragraphs;

        history.PreviewMouseWheel += (_, e) =>
        {
            var scroller = FindScroller(history);
            if (scroller is null)
                return;

            var size = settings.FontSize == "small" ? 16 : settings.FontSize == "large" ? 32 : 20;
            var line = size == 16 ? 24 : size == 32 ? 44 : 28;
            double lineStep = Math.Max(26.0, line * 1.15); // ~30-34px per 120 delta (about 1 to 1.5 lines)
            double delta = -(e.Delta / 120.0) * lineStep;

            double targetOffset = Math.Clamp(scroller.VerticalOffset + delta, 0, scroller.ScrollableHeight);
            scroller.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        };

        history.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && Math.Abs(e.VerticalChange) > 0)
            {
                follow = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 12;
            }
            else if (follow && (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollBottom);
            }
            bottom.Visibility = !follow && paragraphs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }));
        grid.Children.Add(history);
        Grid.SetRow(history, 1);

        // Back to Bottom Button (Overlay pinned at bottom-right of subtitle viewport)
        bottom.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.VerticalAlignment = VerticalAlignment.Bottom;
        bottom.Margin = new Thickness(0, 0, 18, 12);
        Panel.SetZIndex(bottom, 10);
        bottom.Click += (_, _) => { follow = true; ScrollBottom(); };
        grid.Children.Add(bottom);
        Grid.SetRow(bottom, 1);

        // Context Menu
        RebuildContextMenu();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.FocusedElement is TextBox text && text.SelectionLength > 0)
                    return;
                CopyCaption();
                e.Handled = true;
            }
        };

        SizeChanged += (_, e) =>
        {
            if (e.HeightChanged || e.WidthChanged)
            {
                if (SizeToContent == SizeToContent.Manual)
                {
                    userResized = true;
                    history.MaxHeight = double.PositiveInfinity;
                    history.Height = double.NaN;
                }
            }
        };

        store.Changed += QueueRender;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
        monitor.Tick += (_, _) =>
        {
            if (pipeline is not null && !pipeline.Completion.IsCompleted && currentPipelineState == PipelineState.Listening)
            {
                UpdateStatusDisplay();
            }
        };
        monitor.Start();
        ApplyTheme();
        ApplyUiLanguage();
        RefreshLanguages();
        Render();
    }

    async Task InitializeAsync()
    {
        try
        {
            var loaded = persistSettings ? await settingsStore.LoadAsync() : new AppSettings();
            settings = LanguageSettings.Upgrade(string.IsNullOrEmpty(loaded.Asr.Provider) ? ProviderCatalog.DefaultSettings() : loaded);
            UiText.CurrentLanguage = settings.UiLanguage;
            ApplyTheme();
            ApplyUiLanguage();
            RefreshLanguages();
            Topmost = persistSettings && settings.Topmost;
            var work = SystemParameters.WorkArea;
            Left = Math.Clamp(settings.WindowLeft ?? work.Left + (work.Width - Width) / 2, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(settings.WindowTop ?? work.Bottom - 260, work.Top, Math.Max(work.Top, work.Bottom - 250));
            if (settingsStore.Warning is not null)
                SetStatus("设置无法读取或解密，已恢复默认值");
            Render();
        }
        catch { SetStatus("设置读取失败，请打开设置重新配置", PipelineState.Error); }
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
        catch
        {
            SetStatus("设置保存失败，请检查软件目录写入权限", PipelineState.Error);
            return false;
        }
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
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(statusMessage) || (!statusMessage.Contains("禁止") && !statusMessage.Contains("无法释放")))
                SetStatus(ex.Message.Length > 0 && ex is InvalidOperationException ? ex.Message : "操作失败，请检查音频资源或服务配置后重试", PipelineState.Error);
            start.Content = "重试";
        }
        finally
        {
            busy = false;
            start.IsEnabled = true;
        }
    }

    Task OpenSettingsAsync() => RunActionAsync(OpenSettingsCoreAsync);

    async Task OpenSettingsCoreAsync()
    {
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await ApplySettingsResultAsync(dialog.Result);
        }
        else
        {
            ApplyTheme();
        }
    }

    internal async Task ApplySettingsResultAsync(AppSettings newSettings)
    {
        var wasRunning = pipeline is not null && !pipeline.Completion.IsCompleted;
        var pipelineChanged = HasPipelineConfigChanged(settings, newSettings);
        if (pipelineChanged)
        {
            if (wasRunning)
            {
                await StopCaptureCoreAsync(StopReason.Reconfigure);
                settings = newSettings;
                UiText.CurrentLanguage = settings.UiLanguage;
                ApplyTheme();
                ApplyUiLanguage();
                RefreshLanguages();
                Topmost = settings.Topmost;
                Render();
                if (!await SaveSettingsAsync())
                {
                    SetStatus("设置保存失败，请检查写入权限", PipelineState.Error);
                    return;
                }
                if (ProviderLanguages.ValidateSettings(settings) is { } validation)
                {
                    SetStatus(validation, PipelineState.Error);
                    return;
                }
                await StartConfiguredPipelineAsync();
                SetStatus("设置已保存并生效", PipelineState.Listening);
            }
            else
            {
                settings = newSettings;
                UiText.CurrentLanguage = settings.UiLanguage;
                ApplyTheme();
                ApplyUiLanguage();
                RefreshLanguages();
                Topmost = settings.Topmost;
                Render();
                if (await SaveSettingsAsync())
                    SetStatus("设置已保存 · 点击开始");
            }
        }
        else
        {
            settings = newSettings;
            UiText.CurrentLanguage = settings.UiLanguage;
            ApplyTheme();
            ApplyUiLanguage();
            RefreshLanguages();
            Topmost = settings.Topmost;
            Render();
            if (!await SaveSettingsAsync())
            {
                SetStatus("设置保存失败，请检查写入权限", PipelineState.Error);
            }
        }
    }

    static bool HasPipelineConfigChanged(AppSettings a, AppSettings b)
    {
        if (a.AudioDeviceId != b.AudioDeviceId) return true;
        if (a.SourceLanguage != b.SourceLanguage) return true;
        if (a.TargetLanguage != b.TargetLanguage) return true;
        if (a.FallbackEnabled != b.FallbackEnabled) return true;
        if (a.FallbackCostConsent != b.FallbackCostConsent) return true;
        if (a.FallbackTranslationProviderId != b.FallbackTranslationProviderId) return true;

        if (a.Asr.Provider != b.Asr.Provider ||
            a.Asr.ApiKey != b.Asr.ApiKey ||
            a.Asr.Endpoint != b.Asr.Endpoint ||
            a.Asr.Model != b.Asr.Model ||
            a.Asr.Language != b.Asr.Language ||
            !DictionariesEqual(a.Asr.Options, b.Asr.Options) ||
            !DictionariesEqual(a.Asr.Secrets, b.Asr.Secrets))
            return true;

        if (a.Translation.Provider != b.Translation.Provider ||
            a.Translation.ApiKey != b.Translation.ApiKey ||
            a.Translation.Endpoint != b.Translation.Endpoint ||
            a.Translation.Model != b.Translation.Model ||
            a.Translation.DebounceMilliseconds != b.Translation.DebounceMilliseconds ||
            a.Translation.TimeoutSeconds != b.Translation.TimeoutSeconds ||
            a.Translation.ContextSentences != b.Translation.ContextSentences ||
            !DictionariesEqual(a.Translation.Options, b.Translation.Options) ||
            !DictionariesEqual(a.Translation.Secrets, b.Translation.Secrets))
            return true;

        return false;
    }

    static bool DictionariesEqual(Dictionary<string, string>? x, Dictionary<string, string>? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        if (x.Count != y.Count) return false;
        foreach (var (k, v) in x)
        {
            if (!y.TryGetValue(k, out var vy) || v != vy) return false;
        }
        return true;
    }

    internal void ApplyLiveOpacity(double opacity)
    {
        DesktopTheme.Apply(this, settings.Theme, opacity);
    }

    Task ToggleCaptureAsync() => RunActionAsync(async () =>
    {
        if (pipeline is not null && !pipeline.Completion.IsCompleted)
        {
            await StopCaptureCoreAsync(StopReason.UserStop);
            return;
        }
        if (ProviderLanguages.ValidateSettings(settings) is { } validation)
        {
            SetStatus(validation, PipelineState.Error);
            await OpenSettingsCoreAsync();
            return;
        }
        await StartConfiguredPipelineAsync();
    });

    async Task StartPipelineAsync(IAudioSource audio, IAsrProvider asr, ITranslationProvider translator)
    {
        if (pipeline is not null)
        {
            SetStatus("前次资源尚未释放，正在尝试清理…", PipelineState.Stopping);
            try
            {
                await pipeline.DisposeAsync();
                pipeline = null;
                pipelineCleanupFailed = false;
            }
            catch (Exception ex)
            {
                pipelineCleanupFailed = true;
                SetStatus($"前次音频或服务资源无法释放: {ex.Message}，禁止重复启动", PipelineState.Error);
                throw new InvalidOperationException("Previous pipeline failed to clean up.", ex);
            }
        }
        pipeline = new(audio, asr, translator, settings, store);
        await StartPipelineInstanceAsync(pipeline);
    }

    async Task StartPipelineInstanceAsync(CaptionPipeline current)
    {
        if (pipeline is not null && !ReferenceEquals(pipeline, current))
        {
            if (pipelineCleanupFailed || !pipeline.Completion.IsCompleted)
            {
                SetStatus("前次资源释放失败，禁止启动新管线", PipelineState.Error);
                throw new InvalidOperationException("Previous pipeline failed to clean up.");
            }
        }
        pipeline = current;
        current.StatusChanged += s => Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(current, pipeline))
                return;
            SetStatus(s.Message, s.State, s.DroppedChunks);
        });
        current.AudioLevelChanged += level =>
        {
            if (level > 50.0)
                Interlocked.Exchange(ref lastAudibleTicks, DateTimeOffset.UtcNow.Ticks);
        };
        Interlocked.Exchange(ref lastAudibleTicks, 0);
        await current.StartAsync();
    }

    async Task StopCaptureCoreAsync(StopReason reason = StopReason.UserStop)
    {
        Interlocked.Exchange(ref lastAudibleTicks, 0);
        if (pipeline is null)
            return;
        var previous = pipeline;
        try
        {
            if (reason == StopReason.Reconfigure)
            {
                store.BeginSession(false);
                await previous.StopAsync(StopReason.Reconfigure);
                await previous.DisposeAsync();
            }
            else
            {
                await previous.StopAsync(reason);
                await previous.DisposeAsync();
            }
            if (ReferenceEquals(previous, pipeline))
            {
                pipeline = null;
                pipelineCleanupFailed = false;
            }
            SetStatus("准备就绪 · 点击开始采集系统音频", PipelineState.Stopped);
        }
        catch (Exception ex)
        {
            pipelineCleanupFailed = true;
            SetStatus($"停止或清理异常: {ex.Message}，请重试停止或检查配置", PipelineState.Error);
            throw;
        }
    }

    async Task StartConfiguredPipelineAsync()
    {
        if (pipelineCleanupFailed && pipeline is not null)
        {
            SetStatus("前次资源尚未释放，正在尝试强制清理…", PipelineState.Stopping);
            try
            {
                await pipeline.DisposeAsync();
                pipeline = null;
                pipelineCleanupFailed = false;
            }
            catch (Exception ex)
            {
                SetStatus($"前次音频或服务资源无法释放: {ex.Message}，禁止重复启动", PipelineState.Error);
                throw new InvalidOperationException($"前次音频或服务资源无法释放: {ex.Message}，禁止重复启动", ex);
            }
        }

        if (TestPipelineFactory is { } factory)
            await StartPipelineInstanceAsync(factory(settings, store));
        else
            await StartPipelineAsync(new WasapiAudioSource(settings.AudioDeviceId), ProviderCatalog.CreateAsr(settings.Asr), ProviderCatalog.CreateTranslator(settings));
    }

    internal Task ApplyLanguageAsync(string sourceId, string targetId) => RunActionAsync(async () =>
    {
        var wasRunning = pipeline is not null && !pipeline.Completion.IsCompleted;
        SetStatus("正在切换语言并释放旧会话…", PipelineState.Connecting);
        await StopCaptureCoreAsync(StopReason.Reconfigure);
        settings = LanguageSettings.Select(settings, sourceId, targetId);
        store.BeginSession();
        RefreshLanguages();
        Render();
        if (!await SaveSettingsAsync())
            return;
        if (ProviderLanguages.ValidateSettings(settings) is { } invalid)
        {
            SetStatus(invalid, PipelineState.Error);
            return;
        }
        if (wasRunning)
            await StartConfiguredPipelineAsync();
        else
            SetStatus(LanguageRegistry.Same(settings.SourceLanguage, settings.TargetLanguage) ? "同语言直通 · 无需翻译服务调用" : "语言已更新 · 点击开始");
    });

    internal Task SwapLanguagesAsync()
    {
        if (settings.SourceLanguage == "auto")
        {
            SetStatus("自动检测不能作为目标语言，请先明确选择源语言。", PipelineState.Error);
            return Task.CompletedTask;
        }
        return ApplyLanguageAsync(settings.TargetLanguage, settings.SourceLanguage);
    }

    void RefreshLanguages()
    {
        sourceLanguage.Configure(settings.SourceLanguage, ProviderLanguages.AsrChoices(settings.Asr), settings.RecentLanguages);
        targetLanguage.Configure(settings.TargetLanguage, ProviderLanguages.TargetChoices(settings.Translation), settings.RecentLanguages, settings.Translation.Provider is "GroqLlm" or "OpenAiCompatible");
        var s = LanguageRegistry.Find(settings.SourceLanguage)?.Name ?? settings.SourceLanguage;
        var t = LanguageRegistry.Find(settings.TargetLanguage)?.Name ?? settings.TargetLanguage;
        languageTrigger.Content = $"{s} → {t}  ▾";
        languageTrigger.ToolTip = UiText.CurrentLanguage switch
        {
            "en" => $"Current: {s} → {t} (Click to change)",
            "ja" => $"現在の言語: {s} → {t}（クリックして変更）",
            _ => $"当前语言：{s} → {t}（点击更改）"
        };
    }

    void ApplyTheme()
    {
        DesktopTheme.Apply(this, settings.Theme, settings.BackgroundOpacity);
        source.SetResourceReference(Control.ForegroundProperty, "Muted");
        target.SetResourceReference(Control.ForegroundProperty, "Ink");
        history.SetResourceReference(Control.ForegroundProperty, "Ink");
        history.ItemTemplate = null;
    }

    void ApplyUiLanguage()
    {
        Title = UiText.AppTitle;
        expand.Content = expanded ? UiText.Collapse : UiText.Expand;
        expand.ToolTip = expanded ? UiText.CollapseTip : UiText.ExpandTip;
        sourceBtn.Content = UiText.SourceBtn;
        sourceBtn.ToolTip = UiText.SourceBtnTip;
        fontBtn.Content = UiText.FontBtn;
        fontBtn.ToolTip = UiText.FontBtnTip;
        settingsBtn.Content = "⚙";
        settingsBtn.ToolTip = UiText.SettingsBtnTip;
        closeBtn.ToolTip = UiText.CloseBtnTip;
        bottom.Content = UiText.BackToBottom;
        bottom.ToolTip = UiText.BackToBottomTip;
        swap.ToolTip = UiText.SwapLanguagesTip;
        langTitle.Text = UiText.LangTitle;
        sourceBoxLabel.Text = UiText.SourceBoxLabel;
        targetBoxLabel.Text = UiText.TargetBoxLabel;
        providerNoticeText.Text = UiText.ProviderNoticeText;
        RebuildContextMenu();
        UpdateStatusDisplay();
    }

    void RebuildContextMenu()
    {
        var menu = new ContextMenu();
        MenuItem demo = new() { Header = UiText.MenuDemo };
        demo.Click += async (_, _) => await StartDemoAsync();
        menu.Items.Add(demo);
        MenuItem top = new() { Header = UiText.MenuTopmost, IsCheckable = true, IsChecked = Topmost };
        top.Click += async (_, _) => { Topmost = top.IsChecked; settings = settings with { Topmost = Topmost }; await SaveSettingsAsync(); };
        menu.Items.Add(top);
        MenuItem copy = new() { Header = UiText.MenuCopy };
        copy.Click += (_, _) => CopyCaption();
        menu.Items.Add(copy);
        ContextMenu = menu;
    }

    public Task StartDemoAsync() => RunActionAsync(async () =>
    {
        await StopCaptureCoreAsync(StopReason.Reconfigure);
        await StartPipelineAsync(new DemoAudioSource(), new DemoAsrProvider(), new DemoTranslationProvider());
        SetStatus("本地演示 · 合成字幕，无音频采集或网络请求", PipelineState.Listening);
    });

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
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            lock (store)
                renderQueued = false;
            Render();
        });
    }

    public void SetExpanded(bool value)
    {
        expanded = value;
        userResized = false;
        expand.Content = value ? UiText.Collapse : UiText.Expand;
        expand.ToolTip = value ? UiText.CollapseTip : UiText.ExpandTip;
        SizeToContent = SizeToContent.Height;
        var size = settings.FontSize == "small" ? 16 : settings.FontSize == "large" ? 32 : 20;
        var line = size == 16 ? 24 : size == 32 ? 44 : 28;
        if (value)
        {
            history.MaxHeight = 360;
            history.Height = double.NaN;
        }
        else
        {
            history.MaxHeight = Math.Max(76, line * 3.4);
            history.Height = double.NaN;
        }
        Render();
    }

    void Render()
    {
        var size = settings.FontSize == "small" ? 16 : settings.FontSize == "large" ? 32 : 20;
        var line = size == 16 ? 24 : size == 32 ? 44 : 28;
        source.FontSize = size * 0.85;
        target.FontSize = size;
        target.FontWeight = FontWeights.SemiBold;
        TextBlock.SetLineHeight(source, line);
        TextBlock.SetLineHeight(target, line);
        sourceViewport.MaxHeight = targetViewport.MaxHeight = line * 2;
        sourceViewport.Visibility = settings.ShowSource ? Visibility.Visible : Visibility.Collapsed;

        if (!userResized)
        {
            if (expanded)
            {
                history.MaxHeight = 360;
                history.Height = double.NaN;
            }
            else
            {
                history.MaxHeight = Math.Max(76, line * 3.4);
                history.Height = double.NaN;
            }
        }
        else
        {
            history.MaxHeight = double.PositiveInfinity;
            history.Height = double.NaN;
        }

        var rows = store.Snapshot();
        var text = SubtitleProjection.Compact(rows);
        source.Text = text.Source.Length > 0 ? text.Source : UiText.PlaceholderSource;
        target.Text = text.Source.Length > 0 && text.Translation.Length > 0 ? text.Translation : (rows.Count > 0 ? "" : UiText.PlaceholderTarget);

        compact.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        history.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var updated = SubtitleProjection.Expanded(rows);
        for (int i = 0; i < updated.Count; i++)
        {
            if (i >= paragraphs.Count)
                paragraphs.Add(updated[i]);
            else if (paragraphs[i] != updated[i])
                paragraphs[i] = updated[i];
        }
        while (paragraphs.Count > updated.Count)
            paragraphs.RemoveAt(paragraphs.Count - 1);

        if (history.ItemTemplate is null || historyFont != size || historyShowSource != settings.ShowSource)
        {
            history.ItemTemplate = ParagraphTemplate(size, line, settings.ShowSource);
            historyFont = size;
            historyShowSource = settings.ShowSource;
        }

        if (follow)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollBottom);

        bottom.Visibility = !follow && paragraphs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (!expanded)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                sourceViewport.ScrollToBottom();
                targetViewport.ScrollToBottom();
            });

        UpdateStatusDisplay();
    }

    DataTemplate ParagraphTemplate(double size, double line, bool showSource)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.MarginProperty, new Thickness(2, 4, 2, 4));
        void Add(string property, double s, Brush brush)
        {
            var text = new FrameworkElementFactory(typeof(TextBox));
            text.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(property));
            text.SetValue(TextBox.IsReadOnlyProperty, true);
            text.SetValue(TextBox.IsReadOnlyCaretVisibleProperty, false);
            text.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            text.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
            text.SetValue(TextBox.PaddingProperty, new Thickness(0));
            text.SetValue(TextBox.MarginProperty, new Thickness(0));
            text.SetValue(TextBox.FontSizeProperty, s);
            text.SetValue(TextBlock.LineHeightProperty, line);
            text.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
            if (property == nameof(SubtitleParagraph.Translation))
                text.SetBinding(TextBox.OpacityProperty, new System.Windows.Data.Binding(nameof(SubtitleParagraph.TranslationOpacity)));
            text.SetResourceReference(TextBox.ForegroundProperty, property == nameof(SubtitleParagraph.Source) ? "Muted" : "Ink");
            panel.AppendChild(text);
        }
        if (showSource)
            Add(nameof(SubtitleParagraph.Source), size * 0.85, Brushes.Gray);
        Add(nameof(SubtitleParagraph.Translation), size, Brushes.White);

        var errorPanel = new FrameworkElementFactory(typeof(StackPanel));
        errorPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        errorPanel.SetValue(StackPanel.MarginProperty, new Thickness(0, 2, 0, 0));
        errorPanel.SetBinding(StackPanel.VisibilityProperty, new System.Windows.Data.Binding(nameof(SubtitleParagraph.HasError)) { Converter = (System.Windows.Data.IValueConverter)FindResource("BoolToVis") });

        var errText = new FrameworkElementFactory(typeof(TextBlock));
        errText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SubtitleParagraph.ErrorDisplay)));
        errText.SetValue(TextBlock.FontSizeProperty, 11.5);
        errText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(229, 115, 115)));
        errText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        errorPanel.AppendChild(errText);

        var retryBtn = new FrameworkElementFactory(typeof(Button));
        retryBtn.SetValue(Button.ContentProperty, UiText.Retry);
        retryBtn.SetValue(Button.FontSizeProperty, 11d);
        retryBtn.SetValue(Button.PaddingProperty, new Thickness(6, 1, 6, 1));
        retryBtn.SetValue(Button.MarginProperty, new Thickness(6, 0, 0, 0));
        retryBtn.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
        retryBtn.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is FrameworkElement fe && fe.DataContext is SubtitleParagraph p && p.CanRetry)
                RetryTranslation(p.SessionId, p.SegmentId, p.Revision);
        }));
        errorPanel.AppendChild(retryBtn);

        panel.AppendChild(errorPanel);
        return new() { VisualTree = panel };
    }

    internal bool RetryTranslation(long session, string segmentId, int revision)
    {
        if (pipeline is not null && !pipeline.Completion.IsCompleted)
            return pipeline.RetryTranslation(session, segmentId, revision);
        return false;
    }

    void ScrollBottom()
    {
        if (!follow)
            return;
        var scroller = FindScroller(history);
        if (scroller is not null)
            scroller.ScrollToBottom();
        else if (history.Items.Count > 0)
            history.ScrollIntoView(history.Items[history.Items.Count - 1]);
        bottom.Visibility = Visibility.Collapsed;
    }

    void SetStatus(string message, PipelineState? state = null, long dropped = 0)
    {
        statusMessage = message;
        if (state.HasValue)
            currentPipelineState = state.Value;
        else if (pipeline is null || pipeline.Completion.IsCompleted)
            currentPipelineState = PipelineState.Stopped;
        else
            currentPipelineState = PipelineState.Listening;

        lastDroppedChunks = dropped;
        UpdateStatusDisplay();
    }

    void UpdateStatusDisplay()
    {
        var hasTranslationError = paragraphs.Any(p => p.HasError);
        var effectiveState = currentPipelineState;

        var tip = statusMessage;
        if (lastDroppedChunks > 0)
            tip += $" · 已丢弃 {lastDroppedChunks} 个过期音频块";

        if (effectiveState == PipelineState.Listening)
        {
            if (hasTranslationError)
            {
                statusDot.Background = new SolidColorBrush(Color.FromRgb(255, 179, 0)); // Amber #FFB300
                statusText.Text = UiText.StatusTranslationError;
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                start.Content = UiText.Stop;
                tip += " · " + UiText.TipUnfinishedTranslationError;
            }
            else
            {
                statusDot.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green #4CAF50
                statusText.Text = UiText.StatusListening;
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                start.Content = UiText.Stop;
            }

            var lastAudible = Interlocked.Read(ref lastAudibleTicks);
            if (lastAudible == 0 || DateTimeOffset.UtcNow.Ticks - lastAudible > TimeSpan.FromSeconds(3).Ticks)
            {
                tip += " · " + UiText.TipNoAudioSignal;
            }
        }
        else
        {
            switch (effectiveState)
            {
                case PipelineState.Connecting:
                    statusDot.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow #FFC107
                    statusText.Text = UiText.StatusConnecting;
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                    start.Content = UiText.Stop;
                    break;
                case PipelineState.Retrying:
                    statusDot.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange #FF9800
                    statusText.Text = UiText.StatusRetrying;
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                    start.Content = UiText.Stop;
                    break;
                case PipelineState.Stopping:
                    statusDot.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                    statusText.Text = UiText.StatusStopping;
                    statusText.Foreground = Brushes.Gray;
                    start.Content = UiText.Stop;
                    break;
                case PipelineState.Error:
                    statusDot.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red #F44336
                    statusText.Text = UiText.StatusError;
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                    start.Content = UiText.Retry;
                    break;
                case PipelineState.Stopped:
                default:
                    statusDot.Background = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray #9E9E9E
                    statusText.Text = UiText.StatusIdle;
                    statusText.Foreground = Brushes.Gray;
                    start.Content = UiText.Start;
                    break;
            }
        }
        statusBadge.ToolTip = tip;
    }

    public async Task CloseAsync()
    {
        if (allowClose || closing)
            return;
        closing = true;
        monitor.Stop();
        IsEnabled = false;
        try
        {
            while (busy)
                await Task.Delay(20);
            await StopCaptureCoreAsync(StopReason.Shutdown);
            if (persistSettings)
            {
                settings = settings with
                {
                    WindowLeft = Left,
                    WindowTop = Top
                };
                await SaveSettingsAsync();
            }
        }
        catch { SetStatus("关闭时音频资源释放异常", PipelineState.Error); }
        finally
        {
            allowClose = true;
            if (!Dispatcher.HasShutdownStarted)
            {
                try { Close(); } catch { }
            }
        }
    }

    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
            return;
        e.Cancel = true;
        if (closing)
            return;
        await CloseAsync();
    }

    void CopyCaption()
    {
        try
        {
            var p = history.SelectedItem as SubtitleParagraph ?? SubtitleProjection.Compact(store.Snapshot());
            Clipboard.SetText(p.Source + Environment.NewLine + p.Translation);
        }
        catch { SetStatus("无法写入剪贴板，请重试", PipelineState.Error); }
    }

    internal AppSettings TestSettings => settings;
    internal void TestConfigure(AppSettings value) { settings = LanguageSettings.Upgrade(value); ApplyTheme(); RefreshLanguages(); Render(); }
    internal Task TestStartAsync() => RunActionAsync(StartConfiguredPipelineAsync);
    internal Task TestStopCaptureAsync(StopReason reason = StopReason.UserStop) => RunActionAsync(() => StopCaptureCoreAsync(reason));
    internal Task TestToggleCaptureAsync() => ToggleCaptureAsync();
    internal bool TestPipelineCleanupFailed => pipelineCleanupFailed;
    internal string TestStatus => statusMessage;
    internal bool TestRunning => pipeline is not null && !pipeline.Completion.IsCompleted;
    internal LanguagePicker TestSourcePicker => sourceLanguage;
    internal LanguagePicker TestTargetPicker => targetLanguage;
    internal SubtitleStore TestStore => store;
    internal string TestSource => source.Text;
    internal string TestTarget => target.Text;
    internal int TestHistoryCount => history.Items.Count;
    internal IReadOnlyList<SubtitleParagraph> TestVisibleParagraphs => paragraphs.ToArray();
    internal bool TestFollowing => follow;
    internal ScrollViewer? TestScroller => FindScroller(history);
    internal double TestCompactOffset => Math.Max(sourceViewport.VerticalOffset, FindScroller(history)?.VerticalOffset ?? 0);
    internal ListBox TestHistory => history;
    internal Button TestBottomButton => bottom;
    internal string TestStatusText => statusText.Text;
    internal string TestStatusTip => statusBadge.ToolTip as string ?? "";
    internal string TestStartButtonContent => start.Content as string ?? "";
    internal void TestOpenSettingsCancel() => ApplyTheme();
    internal Task TestApplySettingsResultAsync(AppSettings newSettings) => ApplySettingsResultAsync(newSettings);
    internal void TestSetAudibleTicks(long ticks) => Interlocked.Exchange(ref lastAudibleTicks, ticks);
    internal void TestUpdateStatusDisplay() => UpdateStatusDisplay();
    internal void TestSetFollowing(bool value) => follow = value;
    internal void TestScrollBottom() => ScrollBottom();
    internal void TestSetUserResized(bool value)
    {
        userResized = value;
        if (value)
        {
            SizeToContent = SizeToContent.Manual;
            history.MaxHeight = double.PositiveInfinity;
            history.Height = double.NaN;
        }
    }

    internal static ScrollViewer? FindScroller(DependencyObject element)
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

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    const int WM_NCHITTEST = 0x0084;
    const int WM_SIZING = 0x0214;
    const int HTLEFT = 10;
    const int HTRIGHT = 11;
    const int HTTOP = 12;
    const int HTTOPLEFT = 13;
    const int HTTOPRIGHT = 14;
    const int HTBOTTOM = 15;
    const int HTBOTTOMLEFT = 16;
    const int HTBOTTOMRIGHT = 17;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING)
        {
            userResized = true;
            if (SizeToContent != SizeToContent.Manual)
                SizeToContent = SizeToContent.Manual;
            history.MaxHeight = double.PositiveInfinity;
            history.Height = double.NaN;
        }
        else if (msg == WM_NCHITTEST)
        {
            if (GetWindowRect(hwnd, out RECT rect))
            {
                long val = lParam.ToInt64();
                short screenX = unchecked((short)(val & 0xFFFF));
                short screenY = unchecked((short)((val >> 16) & 0xFFFF));
                int x = screenX - rect.Left;
                int y = screenY - rect.Top;
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;

                const int border = 7;
                const int corner = 14;

                bool isLeft = x < border;
                bool isRight = x >= w - border;
                bool isTop = y < border;
                bool isBottom = y >= h - border;

                if (x < corner && y < corner) { handled = true; return (IntPtr)HTTOPLEFT; }
                if (x >= w - corner && y < corner) { handled = true; return (IntPtr)HTTOPRIGHT; }
                if (x < corner && y >= h - corner) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                if (x >= w - corner && y >= h - corner) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }

                if (isLeft) { handled = true; return (IntPtr)HTLEFT; }
                if (isRight) { handled = true; return (IntPtr)HTRIGHT; }
                if (isTop) { handled = true; return (IntPtr)HTTOP; }
                if (isBottom) { handled = true; return (IntPtr)HTBOTTOM; }
            }
        }
        return IntPtr.Zero;
    }
}
