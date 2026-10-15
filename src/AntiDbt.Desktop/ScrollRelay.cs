using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace AntiDbt.Desktop;
internal static class ScrollRelay
{
    internal static void Attach(ScrollViewer page) => page.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) => Relay(page, e)), true);
    static void Relay(ScrollViewer page, MouseWheelEventArgs e)
    {
        if (!page.IsEnabled || e.Delta == 0 || e.OriginalSource is not DependencyObject origin) return;
        var combo = Ancestor<ComboBox>(origin);
        if (combo?.IsDropDownOpen == true) return;
        var text = Ancestor<TextBox>(origin);
        var password = Ancestor<PasswordBox>(origin);
        // A popup lives in another visual tree. Never forward its wheel to this page.
        if (text is null && password is null && combo is null) return;
        if ((text is not null && !text.IsEnabled) || (password is not null && !password.IsEnabled)) return;
        var pixels = -e.Delta * 48d / 120;
        if (text?.AcceptsReturn == true)
        {
            var inner = MainWindow.FindScroller(text);
            if (inner is not null && (pixels > 0 ? inner.VerticalOffset < inner.ScrollableHeight - 0.01 : inner.VerticalOffset > 0.01))
            {
                inner.ScrollToVerticalOffset(Math.Clamp(inner.VerticalOffset + pixels, 0, inner.ScrollableHeight));
                e.Handled = true; return;
            }
        }
        page.ScrollToVerticalOffset(Math.Clamp(page.VerticalOffset + pixels, 0, page.ScrollableHeight));
        e.Handled = true;
    }
    internal static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T found) return found;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent;
        }
        return null;
    }
}
