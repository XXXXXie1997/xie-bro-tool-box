using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XieBox.Platform.UI;

namespace XieBox.HealthCheck;

public sealed record UsbDevice(string Id, string Name, string Category, string Status);

public sealed class UsbCheckView : StackPanel, IDisposable
{
    private readonly TextBlock status = ToolPage.Text("正在读取 USB 设备…", 12);
    private readonly StackPanel devices = new();
    private readonly TextBlock changes = ToolPage.Text("插入或拔出设备后，这里会显示变化。", 12);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource cancellation = new();
    private readonly Action<string> ready;
    private readonly Queue<string> history = new();
    private IReadOnlyList<UsbDevice>? previous;
    private bool reading, disposed;

    public UsbCheckView(Action<string> ready)
    {
        this.ready = ready;
        Children.Add(ToolPage.Text("连接一个 USB 设备，观察它是否出现在下方。每 2 秒更新一次；可换一个接口再次插拔。", 13));
        status.Margin = new Thickness(0, 10, 0, 8); Children.Add(status);
        Children.Add(new ScrollViewer { Content = devices, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        changes.Margin = new Thickness(0, 10, 0, 0); changes.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Children.Add(changes);
        var note = ToolPage.Text("包含内置 USB 设备、集线器及复合设备节点，数量不等于物理接口数。识别成功不代表传输、供电或设备全部功能正常。", 12);
        note.Margin = new Thickness(0, 10, 0, 0); note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Children.Add(note);
        timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { if (!disposed) { timer.Start(); await Refresh(); } };
        Unloaded += (_, _) => Dispose();
    }

    public static IReadOnlyList<UsbDevice> Read(CancellationToken token) => DeviceProbes.Query(@"root\cimv2",
        "SELECT Name,PNPDeviceID,PNPClass,Service,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Present = TRUE", token)
        .Where(r => Convert.ToString(r.GetValueOrDefault("PNPDeviceID"))?.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) == true)
        .Select(r => new UsbDevice(Convert.ToString(r["PNPDeviceID"])!,
            Convert.ToString(r.GetValueOrDefault("Name")) is { Length: > 0 } name ? name : "未命名 USB 设备",
            Convert.ToString(r.GetValueOrDefault("Service"))?.Contains("hub", StringComparison.OrdinalIgnoreCase) == true ? "集线器" : "USB 设备节点",
            Convert.ToString(r.GetValueOrDefault("ConfigManagerErrorCode")) switch { "0" => "已识别", "22" => "已禁用", "" or null => "状态未知", var code => "设备异常 · 代码 " + code }))
        .OrderBy(d => d.Name).ToArray();

    private async Task Refresh()
    {
        if (reading || disposed) return;
        reading = true;
        try
        {
            var current = await Task.Run(() => Read(cancellation.Token), cancellation.Token);
            if (disposed) return;
            if (previous is not null)
            {
                foreach (var added in current.Where(d => !previous.Any(p => p.Id == d.Id))) Record("已接入", added.Name);
                foreach (var removed in previous.Where(p => !current.Any(d => p.Id == d.Id))) Record("已移除", removed.Name);
            }
            status.Text = $"当前识别 {current.Count} 个 USB 节点 · 更新于 {DateTime.Now:HH:mm:ss}";
            if (previous is null || !previous.SequenceEqual(current))
            {
                devices.Children.Clear();
                foreach (var device in current)
                {
                    var text = ToolPage.Text(device.Name + "\n" + device.Category + " · " + device.Status, 12);
                    var card = new Border { Child = text, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
                    card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); devices.Children.Add(card);
                }
                if (current.Count == 0) devices.Children.Add(ToolPage.Text("未检测到已连接的 USB 设备，请插入设备后观察。", 13));
            }
            previous = current;
            if (current.Count > 0 || history.Count > 0)
                ready("应用内 USB 检测：" + string.Join("、", current.Select(d => d.Name + "（" + d.Status + "）")) + "\n" + string.Join("\n", history) + "\n仅核对系统识别与插拔变化，具体接口及实际功能由用户确认。");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException or System.IO.IOException)
        { if (!disposed) status.Text = "暂时无法读取 USB 设备，将自动重试。下方保留上次结果。"; }
        finally { reading = false; }
    }
    private void Record(string action, string name)
    {
        history.Enqueue($"{DateTime.Now:HH:mm:ss} · {action}：{name}");
        while (history.Count > 5) history.Dequeue();
        changes.Text = string.Join("\n", history);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); cancellation.Cancel();
    }
}
