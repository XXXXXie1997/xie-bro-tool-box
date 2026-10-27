using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DirectShowLib;
using XieBox.Platform.UI;

namespace XieBox.HealthCheck;

public sealed class CameraCheckView : StackPanel, IDisposable
{
    private readonly ComboBox devices = new() { Margin = new Thickness(0, 4, 0, 10), DisplayMemberPath = "Name" };
    private readonly TextBlock status = ToolPage.Text("选择摄像头后点击打开预览。", 12);
    private readonly CameraSurface surface = new() { Height = 240, Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Collapsed };
    private readonly Action<VerificationEvidence> report;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private DsDevice[] cameras = [];
    private IFilterGraph2? graph;
    private ICaptureGraphBuilder2? builder;
    private IBaseFilter? camera;
    private bool disposed;

    public CameraCheckView(Action<VerificationEvidence> report)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBox.HealthCheck;component/MediaStyles.xaml", UriKind.Relative) });
        this.report = report;
        Children.Add(ToolPage.Text("摄像头", 12, true)); Children.Add(devices);
        Children.Add(ToolPage.Text("请检查画面是否清晰、是否被遮挡。仅显示实时画面，不拍照、不录像、不上传。", 12));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; Children.Add(actions);
        actions.Children.Add(ToolPage.Button("打开预览", Start));
        actions.Children.Add(ToolPage.Button("停止预览", () => { Stop(); status.Text = "预览已停止，摄像头已释放。"; }));
        actions.Children.Add(ToolPage.Button("刷新设备", Refresh));
        Children.Add(surface); Children.Add(status);
        devices.SelectionChanged += (_, _) => { Stop(); report(new("已切换摄像头，请重新打开预览。", false)); };
        surface.SizeChanged += (_, _) => Resize();
        timer.Tick += (_, _) => CheckEvents();
        Loaded += (_, _) => { if (!disposed) Refresh(); };
        Unloaded += (_, _) => Dispose();
    }

    private void Refresh()
    {
        Stop(); devices.ItemsSource = null;
        foreach (var device in cameras) device.Dispose(); cameras = [];
        report(new("摄像头列表已更新，请重新验证。", false));
        try
        {
            cameras = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            devices.ItemsSource = cameras; devices.SelectedIndex = cameras.Length > 0 ? 0 : -1;
            status.Text = cameras.Length == 0 ? "未检测到摄像头。外接设备请连接后刷新；没有摄像头可跳过。" : "选择摄像头后点击打开预览。";
            report(new(status.Text, false, "连接摄像头后刷新列表，再打开预览。"));
        }
        catch (Exception e) when (MediaFailure.Expected(e)) { Fail(e); }
    }

    private void Start()
    {
        if (disposed || devices.SelectedItem is not DsDevice selected) return;
        if (MediaPermission.NeedsConsent(false))
        {
            status.Text = MediaPermission.Request(Window.GetWindow(this), false);
            report(new(status.Text, false, Failed: true));
            return;
        }
        Stop(); report(new("正在打开摄像头…", false));
        try
        {
            surface.Visibility = Visibility.Visible; surface.UpdateLayout();
            if (surface.Handle == IntPtr.Zero) throw new InvalidOperationException("预览窗口尚未就绪。");
            graph = (IFilterGraph2)new FilterGraph();
            builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
            DsError.ThrowExceptionForHR(builder.SetFiltergraph(graph));
            DsError.ThrowExceptionForHR(graph.AddSourceFilterForMoniker(selected.Mon, null, selected.Name, out camera));
            DsError.ThrowExceptionForHR(builder.RenderStream(PinCategory.Preview, MediaType.Video, camera, null, null));
            var video = (IVideoWindow)graph;
            DsError.ThrowExceptionForHR(video.put_Owner(surface.Handle));
            DsError.ThrowExceptionForHR(video.put_WindowStyle(DirectShowLib.WindowStyle.Child | DirectShowLib.WindowStyle.ClipChildren | DirectShowLib.WindowStyle.ClipSiblings));
            Resize(); DsError.ThrowExceptionForHR(video.put_Visible(OABool.True));
            DsError.ThrowExceptionForHR(((IMediaControl)graph).Run());
            timer.Start();
            status.Text = "实时预览已打开，请确认画面是否正常。黑屏或卡住时请记录为未完成或发现问题。";
            report(new($"摄像头：{selected.Name}；已打开应用内实时预览，画面质量由用户确认。", true,
                "检查镜头遮挡与摄像头权限，关闭其他视频软件，或切换摄像头后重试。"));
        }
        catch (Exception e) when (MediaFailure.Expected(e)) { Stop(); Fail(e); }
    }

    private void CheckEvents()
    {
        if (graph is not IMediaEvent events) return;
        while (events.GetEvent(out var code, out var first, out var second, 0) == 0)
        {
            events.FreeEventParams(code, first, second);
            if (code is EventCode.ErrorAbort or EventCode.DeviceLost)
            {
                Stop(); status.Text = "摄像头已断开或预览中断，请刷新设备后重试。";
                report(new(status.Text, false, "重新连接摄像头，关闭占用它的软件后再打开预览。", Failed: true)); return;
            }
        }
    }

    private void Resize()
    {
        if (graph is not IVideoWindow video || surface.Handle == IntPtr.Zero) return;
        Native.GetClientRect(surface.Handle, out var rect);
        var width = rect.Right; var height = rect.Bottom;
        if (graph is IBasicVideo basic && basic.GetVideoSize(out var sourceWidth, out var sourceHeight) >= 0 && sourceWidth > 0 && sourceHeight > 0)
        {
            var scale = Math.Min(width / (double)sourceWidth, height / (double)sourceHeight);
            var fittedWidth = (int)(sourceWidth * scale); var fittedHeight = (int)(sourceHeight * scale);
            video.SetWindowPosition((width - fittedWidth) / 2, (height - fittedHeight) / 2, fittedWidth, fittedHeight);
        }
        else video.SetWindowPosition(0, 0, width, height);
    }

    private void Stop()
    {
        timer.Stop();
        try
        {
            if (graph is IMediaControl control) control.Stop();
            if (graph is IVideoWindow video) { video.put_Visible(OABool.False); video.put_Owner(IntPtr.Zero); }
        }
        catch (COMException) { /* Device removal must not prevent releasing the preview. */ }
        finally
        {
            if (camera is not null) Marshal.ReleaseComObject(camera); camera = null;
            if (builder is not null) Marshal.ReleaseComObject(builder); builder = null;
            if (graph is not null) Marshal.ReleaseComObject(graph); graph = null;
            surface.Visibility = Visibility.Collapsed;
        }
    }
    private void Fail(Exception error)
    {
        if (MediaPermission.IsDenied(error))
        {
            status.Text = MediaPermission.Request(Window.GetWindow(this), false);
            report(new(status.Text, false, Failed: true));
            return;
        }
        status.Text = MediaFailure.Describe(error, "摄像头");
        report(new(status.Text, false, "检查摄像头连接及 Windows 摄像头隐私权限，关闭其他视频软件后重试。", Failed: true));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; Stop();
        foreach (var device in cameras) device.Dispose(); cameras = [];
        surface.Dispose();
    }

    private sealed class CameraSurface : HwndHost
    {
        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var handle = Native.CreateWindowEx(0, "static", "", 0x50000000 | 0x02000000, 0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            return new HandleRef(this, handle);
        }
        protected override void DestroyWindowCore(HandleRef hwnd) => Native.DestroyWindow(hwnd.Handle);
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    }
}
