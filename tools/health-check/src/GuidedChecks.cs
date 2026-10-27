using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XieBox.Platform.UI;

namespace XieBox.HealthCheck;

public sealed record GuidedCheck(string Title, string Description, string Kind);

public static class GuidedChecks
{
    public static IReadOnlyList<GuidedCheck> Items { get; } = [
        new("屏幕显示", "查看纯色画面，观察亮暗点、异常色块与闪烁。", "screen"),
        new("扬声器与耳机", "播放左右声道测试音，确认声音与声道方向。", "audio"),
        new("键盘与鼠标", "测试键盘按键、鼠标左右键和滚轮。", "input"),
        new("麦克风", "选择麦克风，录一句话并回放，确认音量和清晰度。", "microphone"),
        new("摄像头", "查看摄像头画面是否清晰、有无遮挡。", "camera"),
        new("USB 接口", "插拔 USB 设备，查看设备列表是否更新。", "ports")
    ];

    public static void Show(Window owner, GuidedCheck check, HealthModel model)
    {
        if (model.IsBusy || !model.CanVerify(check.Kind)) return;
        var body = new Grid { Margin = new Thickness(24) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var description = ToolPage.Text(check.Description, 13);
        description.Margin = new Thickness(0, 0, 0, 16); body.Children.Add(description);
        var area = new Grid(); Grid.SetRow(area, 1); body.Children.Add(area);
        var note = new TextBox { MinHeight = 38, Margin = new Thickness(0, 16, 0, 12), ToolTip = "可选：记录设备名称、已验证接口或发现的问题" };
        System.Windows.Automation.AutomationProperties.SetName(note, "验证备注（可选）");
        var noteHost = new Grid(); noteHost.Children.Add(note); note.Margin = new Thickness(0, 8, 0, 0);
        var notes = new Expander { Header = "添加备注（可选）", Content = noteHost, Margin = new Thickness(0, 16, 0, 12) };
        Grid.SetRow(notes, 2); body.Children.Add(notes);
        var placeholder = ToolPage.Text("在此处写下备注（可选），如问题描述", 12);
        placeholder.Margin = new Thickness(13, 8, 13, 0); placeholder.IsHitTestVisible = false;
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); noteHost.Children.Add(placeholder);
        note.TextChanged += (_, _) => placeholder.Visibility = string.IsNullOrEmpty(note.Text) ? Visibility.Visible : Visibility.Collapsed;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetRow(actions, 3); body.Children.Add(actions);
        var window = new DialogWindow { Owner = owner, Title = check.Title + " · 功能验证", Width = Math.Min(620, SystemParameters.WorkArea.Width - 40) }.FitContent();
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = window.MaxHeight - 40 };
        IDisposable? media = null;
        string nextStep = "";
        var evidence = "";
        var passed = MakeButton("正常", () => Finish(true), true); passed.IsEnabled = false;
        actions.Children.Add(MakeButton("跳过", () => Finish(null)));
        actions.Children.Add(MakeButton("发现问题", () => Finish(false)));
        actions.Children.Add(passed);
        void Ready(string detail) { evidence = detail; passed.IsEnabled = true; }
        void MediaEvidence(VerificationEvidence update)
        {
            evidence = update.Detail; nextStep = update.NextStep; passed.IsEnabled = update.Ready;
            if (update.Failed) model.RecordManualState(check.Title, HealthState.Incomplete, update.Detail, update.NextStep);
        }
        void Finish(bool? success)
        {
            var detail = success is null ? "本次跳过，未作健康判断。" : success == true ? "用户确认所测试功能正常。" : "用户报告功能异常，具体原因尚未确定。";
            model.RecordManualState(check.Title, success is null ? HealthState.Skipped : success.Value ? HealthState.Normal : HealthState.Issue, detail + (evidence.Length > 0 ? "\n" + evidence : "") + (string.IsNullOrWhiteSpace(note.Text) ? "" : "\n备注：" + note.Text.Trim()), success == false ? nextStep : "");
            window.Close();
        }
        if (check.Kind == "screen")
        {
            var stack = new StackPanel(); area.Children.Add(stack);
            stack.Children.Add(ToolPage.Text("点击颜色查看全屏画面，按 Esc 或单击返回。请逐个检查每台显示器。", 12));
            var colors = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) }; stack.Children.Add(colors);
            foreach (var (label, color) in new[] { ("白色", Colors.White), ("黑色", Colors.Black), ("红色", Colors.Red), ("绿色", Colors.Lime), ("蓝色", Colors.Blue) })
                colors.Children.Add(MakeButton(label, () =>
                {
                    var screen = new Window { Owner = window, Background = new SolidColorBrush(color), WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 600, Height = 400, ShowInTaskbar = false };
                    screen.Loaded += (_, _) => { screen.WindowState = WindowState.Maximized; screen.Focus(); };
                    screen.KeyDown += (_, e) => { if (e.Key == Key.Escape) screen.Close(); };
                    screen.MouseDown += (_, _) => screen.Close();
                    screen.ShowDialog(); Ready("查看过纯色测试画面；由用户观察显示效果。");
                }));
        }
        else if (check.Kind is "audio" or "microphone")
        {
            var audio = new AudioCheckView(check.Kind == "microphone", MediaEvidence);
            media = audio; area.Children.Add(audio);
        }
        else if (check.Kind == "camera")
        {
            var camera = new CameraCheckView(MediaEvidence);
            media = camera; area.Children.Add(camera);
        }
        else if (check.Kind == "input")
        {
            var log = ToolPage.Text("点击此区域后按键。Tab、Esc、Windows 等系统键可能被系统处理，不计入自动判断。", 15);
            var pad = new Border { Padding = new Thickness(20), MinHeight = 140, Focusable = true, Child = log, CornerRadius = new CornerRadius(8) };
            pad.SetResourceReference(Border.BackgroundProperty, "SoftBrush"); area.Children.Add(pad);
            var keys = new HashSet<Key>(); int left = 0, right = 0, wheel = 0;
            void Update()
            {
                evidence = $"收到 {keys.Count} 种按键；左键 {left} 次、右键 {right} 次、滚轮 {wheel} 次。";
                log.Text = evidence + "\n\n已收到：" + string.Join("、", keys.OrderBy(k => k)) + "\n\n仅证明这些输入有响应，不代表全部按键已验证。"; passed.IsEnabled = true;
            }
            pad.PreviewKeyDown += (_, e) => { keys.Add(e.Key == Key.System ? e.SystemKey : e.Key); Update(); if (e.Key != Key.Tab && e.Key != Key.Escape) e.Handled = true; };
            pad.MouseDown += (_, e) => { pad.Focus(); if (e.ChangedButton == MouseButton.Left) left++; if (e.ChangedButton == MouseButton.Right) right++; Update(); e.Handled = true; };
            pad.MouseWheel += (_, e) => { wheel++; Update(); e.Handled = true; };
        }
        else if (check.Kind == "ports")
        {
            var usb = new UsbCheckView(Ready); area.Children.Add(usb);
            window.Closed += (_, _) => usb.Dispose();
        }
        try { window.ShowDialog(); } finally { media?.Dispose(); }
    }

    private static Button MakeButton(string title, Action click, bool primary = false)
    {
        var button = new Button { Content = title, Margin = new Thickness(6), Padding = new Thickness(12, 8, 12, 8) };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        button.Click += (_, _) => click(); return button;
    }
}
