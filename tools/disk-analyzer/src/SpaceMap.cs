using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace XieBox.DiskAnalyzer;

public sealed record SpaceTile(DiskNode? Node, Rect Bounds, double Bytes, int OtherCount = 0);

public static class SpaceMapLayout
{
    // Limit rendered blocks, keeping the remaining bytes in an explicit aggregate.
    public static IReadOnlyList<SpaceTile> Build(IReadOnlyList<DiskNode> nodes, Rect bounds, int limit = 180)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return [];
        var largest = new PriorityQueue<DiskNode, long>();
        double totalBytes = 0; var count = 0; limit = Math.Max(1, limit);
        foreach (var node in nodes)
        {
            if (node.Bytes <= 0) continue;
            count++; totalBytes += node.Bytes; largest.Enqueue(node, node.Bytes);
            if (largest.Count > limit) largest.Dequeue();
        }
        var items = largest.UnorderedItems.Select(i => i.Element).OrderByDescending(n => n.Bytes).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(n => new SpaceTile(n, default, n.Bytes)).ToList();
        if (count > items.Count) items.Add(new(null, default, Math.Max(0, totalBytes - items.Sum(i => i.Bytes)), count - items.Count));
        var result = new List<SpaceTile>();
        void Split(int start, int count, Rect area)
        {
            if (count == 0) return;
            if (count == 1) { result.Add(items[start] with { Bounds = area }); return; }
            double total = 0; for (var i = start; i < start + count; i++) total += items[i].Bytes;
            var half = 0d; var take = 0;
            while (take < count - 1 && (take == 0 || half < total / 2)) half += items[start + take++].Bytes;
            var ratio = half / total;
            if (area.Width >= area.Height)
            {
                var width = area.Width * ratio;
                Split(start, take, new(area.X, area.Y, width, area.Height));
                Split(start + take, count - take, new(area.X + width, area.Y, area.Width - width, area.Height));
            }
            else
            {
                var height = area.Height * ratio;
                Split(start, take, new(area.X, area.Y, area.Width, height));
                Split(start + take, count - take, new(area.X, area.Y + height, area.Width, area.Height - height));
            }
        }
        Split(0, items.Count, bounds); return result;
    }
}

public sealed class SpaceMap : FrameworkElement
{
    private static readonly DependencyProperty ThemeInkProperty = DependencyProperty.Register("ThemeInk", typeof(Brush), typeof(SpaceMap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(nameof(Root), typeof(DiskNode), typeof(SpaceMap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (_, _) => { }));
    public DiskNode? Root { get => (DiskNode?)GetValue(RootProperty); set => SetValue(RootProperty, value); }
    public event Action<DiskNode>? EnterFolder;
    private readonly List<SpaceTile> _hits = [];
    private readonly Dictionary<(DiskNode, Rect, int), IReadOnlyList<SpaceTile>> _layout = [];
    private DiskNode? _layoutRoot;
    private Size _layoutSize;
    private SpaceTile? _hover;
    public SpaceMap() { ClipToBounds = true; Focusable = true; ToolTip = ""; SetResourceReference(ThemeInkProperty, "InkBrush"); }
    private Brush Ink => TryFindResource("InkBrush") as Brush ?? Brushes.Black;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear();
        if (!ReferenceEquals(_layoutRoot, Root) || _layoutSize != RenderSize)
        { _layout.Clear(); _layoutRoot = Root; _layoutSize = RenderSize; _hover = null; }
        dc.DrawRectangle(TryFindResource("SurfaceBrush") as Brush ?? Brushes.WhiteSmoke, null, new Rect(RenderSize));
        if (Root is null || Root.Bytes == 0)
        { Label(dc, Root is null ? "扫描完成后显示空间图" : !Root.IsComplete ? "此目录尚未完整读取，不能判断为空。请切换列表查看，或重新分析。" : "此目录没有可显示的文件大小，请切换列表查看空目录或零字节文件。", new Rect(18, 18, Math.Max(0, ActualWidth - 36), 60), Ink, 14); return; }
        var dark = Ink is SolidColorBrush ink && ink.Color.R > 180;
        Color[] colors = dark ? [Color.FromRgb(47, 76, 103), Color.FromRgb(56, 86, 86), Color.FromRgb(78, 72, 101), Color.FromRgb(99, 81, 57)] : [Color.FromRgb(199, 216, 231), Color.FromRgb(204, 223, 219), Color.FromRgb(217, 210, 229), Color.FromRgb(230, 218, 198)];
        void Draw(DiskNode node, Rect area, int depth, int family)
        {
            var index = 0;
            var key = (node, area, depth);
            if (!_layout.TryGetValue(key, out var tiles)) _layout[key] = tiles = SpaceMapLayout.Build(node.Children, area, depth == 0 ? 100 : 24);
            foreach (var tile in tiles)
            {
                var r = tile.Bounds;
                if (r.Width < 2 || r.Height < 2) continue;
                r.Inflate(-1, -1); _hits.Add(tile);
                var color = colors[(depth == 0 ? index++ : family) % colors.Length];
                dc.DrawRoundedRectangle(new SolidColorBrush(color), new Pen(TryFindResource("LineBrush") as Brush ?? Brushes.Gray, 1), r, 3, 3);
                var text = tile.Node is null ? $"其他 {tile.OtherCount} 项 · {DiskEngine.FormatSize((long)tile.Bytes)}" : $"{(tile.Node.IsFolder ? "▸ " : "")}{tile.Node.Name} · {tile.Node.Size}";
                if (r.Width > 36 && r.Height > 20) Label(dc, text, new(r.X + 6, r.Y + 4, Math.Max(0, r.Width - 12), Math.Min(36, r.Height - 8)), Ink, 12);
                if (depth == 0 && tile.Node is { IsFolder: true } folder && r.Width > 110 && r.Height > 95)
                    Draw(folder, new(r.X + 5, r.Y + 30, r.Width - 10, r.Height - 35), 1, index - 1);
                if (ReferenceEquals(_hover?.Node, tile.Node) && tile.Node is not null)
                    dc.DrawRoundedRectangle(null, new Pen(TryFindResource("AccentInkBrush") as Brush ?? Brushes.SteelBlue, 2), r, 3, 3);
            }
        }
        Draw(Root, new Rect(RenderSize), 0, 0);
    }
    private void Label(DrawingContext dc, string text, Rect area, Brush brush, double size)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = area.Width, MaxTextHeight = area.Height, Trimming = TextTrimming.CharacterEllipsis };
        dc.PushClip(new RectangleGeometry(area)); dc.DrawText(formatted, area.TopLeft); dc.Pop();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var point = e.GetPosition(this);
        var hit = _hits.LastOrDefault(t => t.Bounds.Contains(point));
        if (Equals(_hover, hit)) return;
        _hover = hit; Cursor = hit?.Node?.IsFolder == true ? Cursors.Hand : Cursors.Arrow;
        ToolTip = hit is null ? null : hit.Node is null ? $"其他 {hit.OtherCount} 项，请切换列表查看。" : $"{hit.Node.Path}\n{hit.Node.Size} · 占当前目录 {(Root?.Bytes > 0 ? 100 * hit.Bytes / Root.Bytes : 0):0.0}%\n{(hit.Node.IsFolder ? "点击进入文件夹" : "文件，可在列表中打开位置")}";
        InvalidateVisual();
    }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _hover = null; InvalidateVisual(); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_hits.LastOrDefault(t => t.Bounds.Contains(e.GetPosition(this)))?.Node is { IsFolder: true } folder) EnterFolder?.Invoke(folder);
    }
}
