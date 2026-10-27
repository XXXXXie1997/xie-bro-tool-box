using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace XieBox.NetworkDiagnostics;

public enum LinkState { Pending, Good, Failed, Uncertain }
public sealed record NetworkLinks(LinkState Local, LinkState Internet);

public sealed class NetworkMap : Grid
{
    public static NetworkLinks Evaluate(IReadOnlyList<NetworkCheck> rows, bool complete)
    {
        // A successful HTTPS exchange proves the route works even if ICMP is blocked.
        if (rows.Any(r => (r.Name == "HTTPS 连接" || r.Name == "对照网站") && r.Result == "已收到响应"))
            return new(LinkState.Good, LinkState.Good);
        bool connected = rows.Any(r => r.Result == "已连接");
        bool gateway = rows.Any(r => r.Name.StartsWith("网关") && r.Result == "可达");
        if (!complete) return new(gateway ? LinkState.Good : LinkState.Pending, LinkState.Pending);
        if (!connected) return new(LinkState.Failed, LinkState.Pending);
        return new(gateway ? LinkState.Good : LinkState.Uncertain, gateway ? LinkState.Failed : LinkState.Uncertain);
    }

    public NetworkMap(NetworkModel model)
    {
        Height = 150;
        MaxWidth = 600;
        for (int i = 0; i < 5; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i % 2 == 0 ? 104 : 1, i % 2 == 0 ? GridUnitType.Pixel : GridUnitType.Star) });
        Add(Node("电脑", "M4,5 L44,5 44,32 4,32 Z M24,32 L24,41 M14,41 L34,41"), 0);
        Add(Node("路由器", "M4,25 L44,25 44,40 4,40 Z M10,25 L10,8 M38,25 L38,8 M12,32 L14,32 M20,32 L22,32"), 2);
        Add(Node("互联网", "M24,3 A21,21 0 1 1 23.99,3 M3,24 L45,24 M24,3 C8,16 8,32 24,45 M24,3 C40,16 40,32 24,45"), 4);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 43, 0, 0) };
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 43, 0, 0) };
        Add(left, 1); Add(right, 3);
        void Update()
        {
            var state = Evaluate(model.Entries, model.Complete);
            DrawLink(left, state.Local, model.IsBusy); DrawLink(right, state.Internet, model.IsBusy);
        }
        model.PropertyChanged += (_, e) => { if (e.PropertyName is "Entries" or "IsBusy") Update(); };
        Update();
    }
    private void Add(UIElement element, int column) { SetColumn(element, column); Children.Add(element); }
    private static StackPanel Node(string name, string geometry)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var drawing = new Canvas { Width = 48, Height = 48 };
        var icon = new Path { Data = Geometry.Parse(geometry), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        icon.SetResourceReference(Path.StrokeProperty, "AccentInkBrush"); drawing.Children.Add(icon);
        var iconBase = new Border { Width = 80, Height = 80, CornerRadius = new CornerRadius(20), Child = new Viewbox { Width = 36, Height = 36, Child = drawing } };
        iconBase.SetResourceReference(Border.BackgroundProperty, "SoftBrush"); panel.Children.Add(iconBase);
        panel.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), FontSize = 14, FontWeight = FontWeights.SemiBold });
        return panel;
    }
    private static void DrawLink(StackPanel panel, LinkState state, bool busy)
    {
        panel.Children.Clear();
        var key = state switch { LinkState.Good => "SuccessBrush", LinkState.Failed => "ErrorBrush", LinkState.Uncertain => "WarningBrush", _ => "PendingBrush" };
        var arrow = new Path { Width = 96, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 12), Data = Geometry.Parse("M2,5 L94,5 89,1 M94,17 L2,17 7,21"), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        arrow.SetResourceReference(Path.StrokeProperty, key); panel.Children.Add(arrow);
        var label = new TextBlock { Text = state switch { LinkState.Good => "连接正常", LinkState.Failed => "连接异常", LinkState.Uncertain => "需要进一步排查", _ => busy ? "检查中…" : "未检查" }, TextAlignment = TextAlignment.Center, FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, key); panel.Children.Add(label);
    }
}
