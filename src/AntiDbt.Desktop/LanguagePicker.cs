using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AntiDbt.Core;
using AntiDbt.Providers;
namespace AntiDbt.Desktop;
internal sealed class LanguagePicker : UserControl
{
    readonly Button trigger = new() { HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0), MinWidth = 130 };
    readonly TextBox search = new() { ToolTip = "按中文名称或语言标识搜索", Margin = new Thickness(0, 0, 0, 6) };
    readonly ListBox list = new() { MaxHeight = 310, BorderThickness = new Thickness(0) };
    readonly Popup popup;
    IReadOnlyList<LanguageChoice> choices = Array.Empty<LanguageChoice>();
    IReadOnlyList<string> recent = Array.Empty<string>();
    bool provisional;
    internal string Id { get; private set; } = "auto";
    internal event Action<string>? Selected;
    internal LanguagePicker()
    {
        Content = trigger; var panel = new StackPanel(); panel.Children.Add(search); panel.Children.Add(list);
        var card = DesktopTheme.Card(panel, new Thickness(12)); card.Width = 330; card.Margin = new Thickness(0);
        popup = new Popup { Child = card, PlacementTarget = trigger, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        trigger.Click += (_, _) => { popup.IsOpen = !popup.IsOpen; if (popup.IsOpen) { search.Text = ""; Refresh(); search.Focus(); } };
        search.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem item && item.Tag is LanguageChoice choice) { popup.IsOpen = false; Selected?.Invoke(choice.Id); } };
        search.KeyDown += (_, e) => { if (e.Key == Key.Down) { list.Focus(); if (list.Items.Count > 0) ((ListBoxItem)list.Items[0]).Focus(); e.Handled = true; } if (e.Key == Key.Escape) popup.IsOpen = false; };
        Unloaded += (_, _) => popup.IsOpen = false;
    }
    internal void Configure(string id, IReadOnlyList<LanguageChoice> available, IReadOnlyList<string> recentIds, bool allowProvisional = false)
    {
        Id = id; choices = available; recent = recentIds; provisional = allowProvisional;
        var current = choices.FirstOrDefault(x => x.Id == id);
        trigger.Content = (current?.Name ?? $"未识别：{id}") + "  ▾";
        trigger.ToolTip = current is { Enabled: false } ? current.Reason : "搜索语言；★ 为最近使用";
        Refresh();
    }
    void Refresh()
    {
        list.Items.Clear(); var query = search.Text.Trim();
        foreach (var c in choices.OrderBy(x => { var index = recent.ToList().IndexOf(x.Id); return index < 0 ? int.MaxValue : index; }).Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var label = new StackPanel(); label.Children.Add(new TextBlock { Text = (recent.Contains(c.Id) ? "★ " : "") + c.Name + "  ·  " + c.Id });
            if (!c.Enabled) label.Children.Add(DesktopTheme.Note(c.Reason + (provisional ? "（可暂选后显式测试）" : "")));
            var item = new ListBoxItem { Content = label, Tag = c, IsEnabled = c.Enabled || provisional, ToolTip = c.Reason };
            ToolTipService.SetShowOnDisabled(item, true); list.Items.Add(item);
        }
    }
    internal int TestSearch(string query) { search.Text = query; return list.Items.Count; }
    internal string? TestFirstId => (list.Items.Cast<ListBoxItem>().FirstOrDefault()?.Tag as LanguageChoice)?.Id;
    internal bool TestEnabled(string id) => choices.First(x => x.Id == id).Enabled;
}
