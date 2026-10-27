using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XieBox.Views;

public partial class SupportView : UserControl
{
    public SupportView()
    {
        InitializeComponent();
        WechatPanel.Children.Add(CodeCard("wechat.png", "微信打赏码", 196));
    }

    private static FrameworkElement CodeCard(string file, string name, double size)
    {
        var card = new StackPanel { Margin = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var frame = new Border { Width = size, Height = size, Padding = new Thickness(10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "SoftBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        System.Windows.Automation.AutomationProperties.SetName(frame, name);
        // Real codes are bundled at build time; missing assets stay explicit placeholders.
        try
        {
            var uri = new Uri($"pack://application:,,,/XieBroToolBox;component/Resources/Support/{file}");
            var resource = Application.GetResourceStream(uri);
            if (resource is not null)
            {
                using var stream = resource.Stream;
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                frame.Background = Brushes.White;
                frame.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, ToolTip = name };
            }
        }
        catch (Exception error) when (error is IOException or NotSupportedException or System.IO.FileFormatException) { }
        if (frame.Child is null)
        {
            var placeholder = new TextBlock { Text = "二维码待添加", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var mark = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,10 L 0,0 10,0 M 26,0 L 36,0 36,10 M 36,26 L 36,36 26,36 M 10,36 L 0,36 0,26"), Width = 36, Height = 36, StrokeThickness = 2, Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
            mark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentInkBrush");
            empty.Children.Add(mark); empty.Children.Add(placeholder); frame.Child = empty;
        }
        card.Children.Add(frame);
        return card;
    }
}

