using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntiDbt.Core;

namespace AntiDbt.Desktop;

internal static class SentenceDesktopSmoke
{
    internal static IEnumerable<TextBox> TextBoxes(DependencyObject root)
    {
        if (root is TextBox text) yield return text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in TextBoxes(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Max(1, Math.Ceiling(window.ActualWidth)),
            (int)Math.Max(1, Math.Ceiling(window.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    internal static async Task RunAsync(MainWindow window, string directory, Action<string, bool> check)
    {
        var observations = new List<object>();
        var frames = new List<object>();
        var matrixRecords = new List<object>();
        var dpi = VisualTreeHelper.GetDpi(window);
        Console.WriteLine($"Desktop layout: session={System.Diagnostics.Process.GetCurrentProcess().SessionId}, interactive={Environment.UserInteractive}, loaded={window.IsLoaded}, dpi={dpi.PixelsPerInchX}x{dpi.PixelsPerInchY}, screen={SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight}, trackMax={SystemParameters.MaximumWindowTrackWidth}x{SystemParameters.MaximumWindowTrackHeight}");

        // 1. Unified Subtitle Container Verification (P0 UI regression check)
        window.SetExpanded(false);
        long session = window.TestStore.BeginSession();
        window.TestSetFollowing(true);

        // Add initial sentence
        window.TestStore.ApplySource(session, new("s1", 1, "Welcome to antiDBT live transcription and translation system.", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
        window.TestStore.ApplyTranslation(session, "s1", 1, "欢迎使用 antiDBT 实时转写与翻译系统。");
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        var boxes = TextBoxes(window.TestHistory).ToArray();
        check("unified layout: no per-textbox MaxHeight caps", boxes.All(x => double.IsPositiveInfinity(x.MaxHeight)));
        check("unified layout: textboxes disable internal vertical scrollbars", boxes.All(x => x.VerticalScrollBarVisibility is ScrollBarVisibility.Disabled or ScrollBarVisibility.Hidden));
        check("unified layout: textboxes enable text wrapping", boxes.All(x => x.TextWrapping == TextWrapping.Wrap));

        var scroller = window.TestScroller;
        check("unified layout: single vertical scroll container exists", scroller is not null);
        if (scroller is not null)
        {
            check("unified layout: horizontal scrollbar is disabled", scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled);
            check("unified layout: no horizontal scrollable width", scroller.ScrollableWidth == 0);
        }

        // Record initial observation
        observations.Add(new { mode = "compact", stage = "init", width = window.ActualWidth, height = window.ActualHeight, rows = window.TestStore.Snapshot().Count });
        Capture(window, Path.Combine(directory, "matrix-compact-normal.png"));

        // Sample render frames during translation update
        EventHandler sampleFrame = (_, _) => frames.Add(new { expanded = false, width = window.ActualWidth, height = window.ActualHeight, rows = window.TestStore.Snapshot().Count });
        CompositionTarget.Rendering += sampleFrame;
        try
        {
            window.TestStore.ApplySource(session, new("s2", 1, "Although we faced unexpected technical challenges during development, the team managed to deliver on schedule.", true, TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(7), "en"));
            window.TestStore.ApplyTranslation(session, "s2", 1, "虽然我们在开发过程中遇到了意料之外的技术挑战，但团队依然按期完成了交付。");
            await Task.Delay(150);
        }
        finally { CompositionTarget.Rendering -= sampleFrame; }
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        check("unified layout: sampled actual render frames during subtitle update", frames.Count > 0);

        // 2. Window Size Matrix Tests
        // 2.1 Min Width & Min Height
        window.Width = 400; // Below MinWidth (520)
        window.Height = 50;  // Below MinHeight (88)
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        check("window resize matrix: width respects MinWidth (>= 520)", window.ActualWidth >= 520);
        check("window resize matrix: height respects MinHeight (>= 88)", window.ActualHeight >= 88);
        matrixRecords.Add(new { test = "min-bounds", actualWidth = window.ActualWidth, actualHeight = window.ActualHeight });
        Capture(window, Path.Combine(directory, "matrix-compact-min.png"));

        // Exercise both a constrained desktop and the window's usual size limit.
        double originalMaxWidth = window.MaxWidth;
        foreach (double maxWidth in new[] { Math.Min(1024, originalMaxWidth), originalMaxWidth }.Distinct())
        {
            window.MaxWidth = maxWidth;

            // 2.2 Normal Width & Compact
            window.Width = 680;
            window.SizeToContent = SizeToContent.Height;
            await Task.Delay(100);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            check("window resize matrix: normal width re-wraps text without horizontal overflow", scroller?.ScrollableWidth == 0);
            double normalWidth = window.ActualWidth;
            double normalHistoryWidth = window.TestHistory.ActualWidth;
            matrixRecords.Add(new { test = "normal-compact", maxWidth, actualWidth = normalWidth, actualHeight = window.ActualHeight });

            // 2.3 Wide Layout
            window.Width = 1200;
            await Task.Delay(100);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Console.WriteLine($"Resize matrix: requested=1200, max={maxWidth}, actual={window.ActualWidth}, history={window.TestHistory.ActualWidth}, normal={normalWidth}, normalHistory={normalHistoryWidth}");
            // WPF also clamps top-level windows to the desktop's native track limit.
            double widthGrowth = window.ActualWidth - normalWidth;
            double historyGrowth = window.TestHistory.ActualWidth - normalHistoryWidth;
            check("window resize matrix: wide layout expands subtitle area",
                widthGrowth > 0 && historyGrowth > 0 && Math.Abs(historyGrowth - widthGrowth) < 1);
            check("window resize matrix: wide layout has no horizontal scrollable width", scroller?.ScrollableWidth == 0);
            matrixRecords.Add(new { test = "wide-layout", maxWidth, actualWidth = window.ActualWidth, actualHeight = window.ActualHeight, historyWidth = window.TestHistory.ActualWidth });
            Capture(window, Path.Combine(directory, maxWidth == originalMaxWidth ? "matrix-wide.png" : "matrix-wide-constrained.png"));
        }
        window.MaxWidth = originalMaxWidth;

        // 2.4 Expanded Mode
        window.SetExpanded(true);
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        check("window resize matrix: expanded mode increases history capacity", window.TestHistory.MaxHeight >= 360);
        matrixRecords.Add(new { test = "expanded-mode", actualWidth = window.ActualWidth, actualHeight = window.ActualHeight });
        Capture(window, Path.Combine(directory, "matrix-expanded-normal.png"));

        // 2.5 Tall Layout (User Drag Resize)
        window.TestSetUserResized(true);
        window.Height = 650;
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        check("window resize matrix: tall layout stretches history height", window.TestHistory.ActualHeight > 400);
        matrixRecords.Add(new { test = "tall-layout", actualWidth = window.ActualWidth, actualHeight = window.ActualHeight, historyHeight = window.TestHistory.ActualHeight });
        Capture(window, Path.Combine(directory, "matrix-tall-expanded.png"));

        // 3. Scroll and Follow Behavior Verification
        // Add more sentences to fill height
        for (int i = 3; i <= 15; i++)
        {
            window.TestStore.ApplySource(session, new($"m{i}", 1, $"Matrix verification sentence number {i} for scrolling test.", true, TimeSpan.FromSeconds(i * 3), TimeSpan.FromSeconds(i * 3 + 2), "en"));
            window.TestStore.ApplyTranslation(session, $"m{i}", 1, $"用于滚动验证的矩阵测试第 {i} 句。");
        }
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        check("scroll test: history populated with multiple items", window.TestHistoryCount >= 4);

        // Scroll to top: pauses follow
        window.TestSetFollowing(false);
        scroller?.ScrollToTop();
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        double topOffset = scroller?.VerticalOffset ?? 0;
        check("scroll test: manual scroll up suspends follow", !window.TestFollowing);

        // Late translation does not jump scroll position when reading history
        window.TestStore.ApplyTranslation(session, "m15", 1, "用于滚动验证的矩阵测试第 15 句（最新修正）。");
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        check("scroll test: translation update preserves manual reading position", Math.Abs((scroller?.VerticalOffset ?? 0) - topOffset) < 5);

        // Resume follow
        window.TestSetFollowing(true);
        window.TestScrollBottom();
        await Task.Delay(100);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        check("scroll test: restoring follow scrolls back to bottom", window.TestFollowing);

        // Write test reports
        File.WriteAllText(Path.Combine(directory, "sentence-visibility.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "sentence-render-frames.json"), JsonSerializer.Serialize(frames, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, "window-matrix.json"), JsonSerializer.Serialize(matrixRecords, new JsonSerializerOptions { WriteIndented = true }));

        // Restore default state
        window.TestStore.BeginSession();
        window.SetExpanded(false);
        window.Width = 680;
        window.TestSetUserResized(false);
        window.SizeToContent = SizeToContent.Height;
    }
}
