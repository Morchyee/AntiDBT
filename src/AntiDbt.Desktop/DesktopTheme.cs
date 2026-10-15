using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace AntiDbt.Desktop;
internal static class DesktopTheme
{
    internal static bool IsDark(string theme)
    {
        if (theme != "System") return theme != "Light";
        try { return (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) != 1; }
        catch { return true; }
    }
    internal static void Apply(Window window, string theme, double backgroundOpacity = 0.80)
    {
        var dark = IsDark(theme);
        void Brush(string key, string color) => window.Resources[key] = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        
        // Calculate true alpha for Surface backdrop (0% = 0.0 to 100% = 1.0).
        // Minimum alpha of 1 (0.39% opacity, visually 100% transparent) prevents mouse hit-test click-through on Windows.
        double opacity = Math.Clamp(backgroundOpacity, 0.0, 1.0);
        byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255.0), 1, 255);
        Color surfaceColor = dark
            ? Color.FromArgb(alpha, 0x17, 0x1D, 0x29)
            : Color.FromArgb(alpha, 0xF2, 0xF5, 0xFA);
        window.Resources["Surface"] = new SolidColorBrush(surfaceColor);

        byte strokeAlpha = (byte)Math.Clamp((int)Math.Round(Math.Max(0.20, opacity) * 255.0), 30, 255);
        Color strokeColor = dark
            ? Color.FromArgb(strokeAlpha, 0x3B, 0x49, 0x60)
            : Color.FromArgb(strokeAlpha, 0xD7, 0xE0, 0xEC);
        window.Resources["Stroke"] = new SolidColorBrush(strokeColor);

        Brush("Card", dark ? "#232C3D" : "#FFFFFF");
        Brush("Input", dark ? "#151D2C" : "#F8FAFE");
        Brush("Ink", dark ? "#F2F6FF" : "#182337");
        Brush("Muted", dark ? "#ADBBD1" : "#53647E");
        Brush("Accent", dark ? "#83B5FF" : "#1D60BD");
        Brush("Hover", dark ? "#354563" : "#E4EDFA");
        var resources = (ResourceDictionary)XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource Stroke}"/><Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="12,7"/><Setter Property="Margin" Value="3"/><Setter Property="Cursor" Value="Hand"/>
    <Setter Property="MinHeight" Value="34"/><Setter Property="HorizontalContentAlignment" Value="Center"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
      <Border x:Name="frame" CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
      </Border>
      <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource Hover}"/></Trigger>
        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
        <Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.75"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="TextBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="BorderBrush" Value="{DynamicResource Stroke}"/><Setter Property="CaretBrush" Value="{DynamicResource Accent}"/><Setter Property="Padding" Value="8"/><Setter Property="Margin" Value="0,4,0,12"/><Setter Property="MinHeight" Value="34"/></Style>
  <Style TargetType="PasswordBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="BorderBrush" Value="{DynamicResource Stroke}"/><Setter Property="Padding" Value="8"/><Setter Property="Margin" Value="0,4,0,12"/><Setter Property="MinHeight" Value="34"/></Style>
  <Style TargetType="ComboBox"><Setter Property="Foreground" Value="#172238"/><Setter Property="Padding" Value="8"/><Setter Property="Margin" Value="0,4,0,12"/><Setter Property="MinHeight" Value="34"/></Style>
  <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Margin" Value="0,6,8,10"/></Style>
  <Style TargetType="ListBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="BorderBrush" Value="{DynamicResource Stroke}"/></Style>
  <Style TargetType="ListBoxItem"><Setter Property="Padding" Value="9,8"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="#172238"/><Setter Property="Background" Value="#BBD7FF"/></Trigger></Style.Triggers></Style>
  <Style TargetType="ToolTip"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="BorderBrush" Value="{DynamicResource Stroke}"/></Style>
  <Style x:Key="ScrollBarTrackButton" TargetType="RepeatButton">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="IsTabStop" Value="False"/>
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Border Background="{TemplateBinding Background}" Width="{TemplateBinding Width}" Height="{TemplateBinding Height}"/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="ScrollBarThumb" TargetType="Thumb">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="IsTabStop" Value="False"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Thumb">
          <Border x:Name="thumbBorder" Background="{DynamicResource Muted}" Opacity="0.38" CornerRadius="4" Margin="1"/>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="thumbBorder" Property="Background" Value="{DynamicResource Accent}"/>
              <Setter TargetName="thumbBorder" Property="Opacity" Value="0.85"/>
            </Trigger>
            <Trigger Property="IsDragging" Value="True">
              <Setter TargetName="thumbBorder" Property="Background" Value="{DynamicResource Accent}"/>
              <Setter TargetName="thumbBorder" Property="Opacity" Value="1.0"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="ScrollBar">
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Vertical">
        <Setter Property="Width" Value="8"/>
        <Setter Property="MinWidth" Value="8"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track" IsDirectionReversed="True">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageUpCommand" Style="{StaticResource ScrollBarTrackButton}"/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Style="{StaticResource ScrollBarThumb}"/>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageDownCommand" Style="{StaticResource ScrollBarTrackButton}"/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Height" Value="8"/>
        <Setter Property="MinHeight" Value="8"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track" IsDirectionReversed="False">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageLeftCommand" Style="{StaticResource ScrollBarTrackButton}"/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Style="{StaticResource ScrollBarThumb}"/>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageRightCommand" Style="{StaticResource ScrollBarTrackButton}"/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>
""");
        // Replacing the theme dictionary keeps dynamic resources live without accumulating styles.
        window.Resources.MergedDictionaries.Clear(); window.Resources.MergedDictionaries.Add(resources);
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); window.FontSize = 13;
        window.SetResourceReference(Control.ForegroundProperty, "Ink");
        if (window is not MainWindow)
            window.SetResourceReference(Control.BackgroundProperty, "Surface");
        else
            window.Background = Brushes.Transparent;
    }
    internal static Border Card(UIElement child, Thickness? padding = null)
    {
        var border = new Border { Child = child, Padding = padding ?? new Thickness(18), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16) };
        border.SetResourceReference(Border.BackgroundProperty, "Card"); border.SetResourceReference(Border.BorderBrushProperty, "Stroke"); return border;
    }
    internal static TextBlock Note(string text) { var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) }; note.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return note; }
}
