using System.Windows;
using System.Windows.Media;

namespace XieBox.Platform.UI;

// Shared 24-unit vector icons: the same artwork is used in cards, tabs and management.
public sealed class ModuleIcon : FrameworkElement
{
    public static readonly DependencyProperty ModuleIdProperty = DependencyProperty.Register(nameof(ModuleId), typeof(string), typeof(ModuleIcon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(ModuleIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public string ModuleId { get => (string)GetValue(ModuleIdProperty); set => SetValue(ModuleIdProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public ModuleIcon() { SetResourceReference(InkProperty, "AccentInkBrush"); IsHitTestVisible = false; }
    private static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["file-image"] = Shape("M3,3 L21,3 L21,21 L3,21 Z M3,17 L9,10 L14,16 L17,12 L21,17 M16,6 A2,2 0 1 0 16,10 A2,2 0 1 0 16,6"),
        ["file-video"] = Shape("M3,4 L21,4 L21,20 L3,20 Z M9,8 L16,12 L9,16 Z"),
        ["file-audio"] = Shape("M10,17 L10,5 L20,3 L20,15 M10,8 L20,6 M10,17 A3,2 0 1 0 10,18 M20,15 A3,2 0 1 0 20,16"),
        ["file-archive"] = Shape("M4,3 L20,3 L20,21 L4,21 Z M10,3 L10,6 L14,6 L14,9 L10,9 L10,12 L14,12 M10,15 L14,15 L14,18 L10,18 Z"),
        ["file-document"] = Shape("M4,2 L14,2 L20,8 L20,22 L4,22 Z M14,2 L14,8 L20,8 M8,12 L16,12 M8,16 L16,16"),
        ["hardware-board"] = Shape("M4,2 L20,2 L20,22 L4,22 Z M7,5 L13,5 L13,11 L7,11 Z M16,5 L17,5 L17,14 L16,14 Z M7,15 L13,15 M7,18 L17,18"),
        ["hardware-memory"] = Shape("M2,6 L22,6 L22,17 L2,17 Z M5,9 L9,9 L9,14 L5,14 Z M12,9 L16,9 L16,14 L12,14 Z M5,17 L5,20 M9,17 L9,20 M13,17 L13,20 M17,17 L17,20 M21,17 L21,20"),
        ["hardware-gpu"] = Shape("M2,5 L22,5 L22,17 L2,17 Z M3,17 L3,21 M7,17 L7,20 L17,20 L17,17 M13,7 A4,4 0 1 0 13,15 A4,4 0 1 0 13,7 M5,8 L7,8 M5,12 L7,12"),
        ["hardware-monitor"] = Shape("M2,3 L22,3 L22,17 L2,17 Z M12,17 L12,21 M7,21 L17,21"),
        ["hardware-battery"] = Shape("M3,6 L20,6 L20,18 L3,18 Z M20,10 L23,10 L23,14 L20,14 M7,10 L7,14 M11,10 L11,14 M15,10 L15,14"),
        ["hardware-devices"] = Shape("M4,3 L10,3 L10,9 L4,9 Z M14,3 L20,3 L20,9 L14,9 Z M7,9 L7,13 L17,13 L17,9 M12,13 L12,17 M8,17 L16,17 L16,22 L8,22 Z"),
        ["garbage-cleaner"] = Shape("M4,6 L20,6 M9,6 L9,3 L15,3 L15,6 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17"),
        ["file-scanner"] = Shape("M13,21 L4,21 L4,3 L14,3 L18,7 L18,10 M13,3 L13,8 L18,8 M18,13 A4,4 0 1 0 18,21 A4,4 0 1 0 18,13 M21,20 L23,23"),
        ["startup-manager"] = Shape("M12,3 L12,12 M6,6 A8,8 0 1 0 18,6"),
        ["memory-release"] = Shape("M6,6 L18,6 L18,18 L6,18 Z M9,9 L15,9 L15,15 L9,15 Z M9,2 L9,6 M15,2 L15,6 M9,18 L9,22 M15,18 L15,22 M2,9 L6,9 M2,15 L6,15 M18,9 L22,9 M18,15 L22,15"),
        ["disk-analyzer"] = Shape("M4,4 L20,4 L22,15 L22,20 L2,20 L2,15 Z M2,15 L22,15 M6,17.5 L6.2,17.5 M10,17.5 L10.2,17.5 M8,11 L8,8 M12,11 L12,6 M16,11 L16,9"),
        ["software-uninstaller"] = Shape("M3,4 L21,4 L21,20 L3,20 Z M3,8 L21,8 M8,14 L16,14"),
        ["batch-renamer"] = Shape("M3,4 L11,4 M7,4 L7,17 M4,17 L10,17 M14,6 L21,6 M14,10 L19,10 M13,19 L15,15 L20,10 L23,13 L18,18 Z"),
        ["file-organizer"] = Shape("M3,7 L3,4 L10,4 L13,7 L21,7 L21,20 L3,20 Z M3,10 L21,10 M8,14 L16,14 M8,17 L13,17"),
        ["network-diagnostics"] = Shape("M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M2,12 L22,12 M12,2 C5,7 5,17 12,22 M12,2 C19,7 19,17 12,22"),
        ["health-check"] = Shape("M3,4 L21,4 L21,17 L3,17 Z M8,21 L16,21 M12,17 L12,21 M6,11 L9,11 L11,7 L14,14 L16,10 L19,10"),
        ["hardware-check"] = Shape("M3,3 L21,3 L21,18 L3,18 Z M8,22 L16,22 M12,18 L12,22 M8,7 L16,7 L16,14 L8,14 Z M5,9 L8,9 M16,9 L19,9 M10,5 L10,7 M14,14 L14,16"),
        [""] = Shape("M4,4 L10,4 L10,10 L4,10 Z M14,4 L20,4 L20,10 L14,10 Z M4,14 L10,14 L10,20 L4,20 Z M14,14 L20,14 L20,20 L14,20 Z")
    };
    private static Geometry Shape(string data) { var geometry = Geometry.Parse(data); geometry.Freeze(); return geometry; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var scale = Math.Min(ActualWidth, ActualHeight) / 26;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Ink, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, Shapes.GetValueOrDefault(ModuleId ?? "", Shapes[""]));
        dc.Pop(); dc.Pop();
    }
}
