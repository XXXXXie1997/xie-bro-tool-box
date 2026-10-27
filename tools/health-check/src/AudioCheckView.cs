using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using XieBox.Platform.UI;

namespace XieBox.HealthCheck;

public sealed record VerificationEvidence(string Detail, bool Ready, string NextStep = "", bool Failed = false);

public static class AudioTestData
{
    public static byte[] Tone(int channel, int rate = 48000)
    {
        if (channel is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(channel));
        var bytes = new byte[rate * 4];
        for (var i = 0; i < rate; i++)
        {
            var fade = Math.Min(1d, Math.Min(i, rate - 1 - i) / (rate * .03));
            var value = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 2000 * fade);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 4 + channel * 2, 2), value);
        }
        return bytes;
    }

    public static double Peak(byte[] data, int count, WaveFormat format)
    {
        var floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        var stride = format.BitsPerSample / 8;
        if (stride < 1) return 0;
        double peak = 0;
        for (var i = 0; i + stride <= Math.Min(count, data.Length); i += stride)
        {
            double value = floating && stride == 4 ? BitConverter.ToSingle(data, i) : stride switch
            {
                2 => BitConverter.ToInt16(data, i) / 32768d,
                3 => ((data[i] | data[i + 1] << 8 | data[i + 2] << 16) << 8 >> 8) / 8388608d,
                4 => BitConverter.ToInt32(data, i) / 2147483648d,
                1 => (data[i] - 128) / 128d,
                _ => 0
            };
            if (double.IsFinite(value)) peak = Math.Max(peak, Math.Abs(value));
        }
        return Math.Clamp(peak, 0, 1);
    }
}

public sealed class AudioCheckView : StackPanel, IDisposable
{
    private sealed record Endpoint(string Id, string Name) { public override string ToString() => Name; }
    private readonly bool microphone;
    private readonly Action<VerificationEvidence> report;
    private readonly ComboBox input = new() { MinWidth = 200, Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox output = new() { MinWidth = 200, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBlock status = ToolPage.Text("请选择设备，然后开始测试。", 12);
    private readonly ProgressBar meter = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 8, 0, 8) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly object recordingLock = new();
    private readonly MemoryStream recording = new();
    private WasapiCapture? capture;
    private MMDevice? captureDevice, playbackDevice;
    private WasapiOut? player;
    private RawSourceWaveStream? source;
    private WaveFormat? recordedFormat;
    private double peak, maximumPeak;
    private DateTime started;
    private bool disposed, stoppedRecording, leftPlayed, rightPlayed;
    private string recordedName = "";
    private readonly Button record, replay;

    public AudioCheckView(bool microphone, Action<VerificationEvidence> report)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/XieBox.HealthCheck;component/MediaStyles.xaml", UriKind.Relative) });
        input.DisplayMemberPath = output.DisplayMemberPath = "Name";
        this.microphone = microphone; this.report = report;
        if (microphone) { Children.Add(ToolPage.Text("输入设备", 12, true)); Children.Add(input); }
        Children.Add(ToolPage.Text(microphone ? "回放设备" : "输出设备", 12, true)); Children.Add(output);
        Children.Add(ToolPage.Text(microphone ? "点击开始后说一句话，最多录制 10 秒，再回放确认。录音仅保存在内存，关闭即清除。" : "先调低音量，分别播放左右声道。测试只改变播放目标，不修改系统默认设备。", 12));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) }; Children.Add(actions);
        record = ToolPage.Button("开始录音", StartRecording);
        replay = ToolPage.Button("回放录音", Replay); replay.IsEnabled = false;
        if (microphone) { actions.Children.Add(record); actions.Children.Add(replay); Children.Add(meter); }
        else
        {
            actions.Children.Add(ToolPage.Button("播放左声道", () => PlayTone(0)));
            actions.Children.Add(ToolPage.Button("播放右声道", () => PlayTone(1)));
        }
        actions.Children.Add(ToolPage.Button("停止", Stop));
        actions.Children.Add(ToolPage.Button("刷新设备", Refresh));
        Children.Add(status);
        input.SelectionChanged += (_, _) => Reset(); output.SelectionChanged += (_, _) => Reset();
        timer.Tick += (_, _) =>
        {
            if (capture is null) return;
            double current; lock (recordingLock) current = peak;
            meter.Value = current * 100;
            var seconds = (DateTime.UtcNow - started).TotalSeconds;
            status.Text = $"正在录音 · {Math.Min(seconds, 10):0.0} / 10 秒 · 输入音量 {current:P0}";
            if (seconds >= 10) StopRecording();
        };
        Loaded += (_, _) => { if (!disposed) Refresh(); };
        Unloaded += (_, _) => Dispose();
    }

    private void Refresh()
    {
        Reset();
        try
        {
            using var devices = new MMDeviceEnumerator();
            Populate(input, DataFlow.Capture); Populate(output, DataFlow.Render);
            void Populate(ComboBox box, DataFlow flow)
            {
                var choices = new List<Endpoint>();
                foreach (var device in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
                { using (device) choices.Add(new(device.ID, device.FriendlyName)); }
                box.ItemsSource = choices;
                string? defaultId = null;
                if (choices.Count > 0)
                {
                    try { using var preferred = devices.GetDefaultAudioEndpoint(flow, Role.Multimedia); defaultId = preferred.ID; }
                    catch (COMException) { }
                }
                box.SelectedItem = choices.FirstOrDefault(c => c.Id == defaultId) ?? choices.FirstOrDefault();
            }
            status.Text = output.SelectedItem is null || microphone && input.SelectedItem is null
                ? "未检测到可用设备。连接设备后点击刷新；无需测试时可跳过。" : "设备已就绪，请点击开始测试。";
            report(new(status.Text, false, "确认设备已连接并启用，刷新列表后重新测试。"));
        }
        catch (Exception e) when (MediaFailure.Expected(e)) { Failure(e); }
    }

    private void Reset()
    {
        Stop();
        lock (recordingLock) { recording.SetLength(0); peak = maximumPeak = 0; }
        recordedFormat = null; stoppedRecording = leftPlayed = rightPlayed = false;
        record.Content = "开始录音";
        replay.IsEnabled = false; meter.Value = 0;
        report(new("设备或测试已改变，请重新验证。", false));
    }

    private void StartRecording()
    {
        if (disposed) return;
        if (capture is not null) { StopRecording(); return; }
        if (MediaPermission.NeedsConsent(true))
        {
            status.Text = MediaPermission.Request(Window.GetWindow(this), true);
            report(new(status.Text, false, Failed: true));
            return;
        }
        Reset();
        if (input.SelectedItem is not Endpoint selected) { status.Text = "请先选择输入设备。"; return; }
        try
        {
            using var devices = new MMDeviceEnumerator();
            captureDevice = devices.GetDevice(selected.Id);
            capture = new WasapiCapture(captureDevice);
            var active = capture; recordedFormat = active.WaveFormat; recordedName = selected.Name;
            active.DataAvailable += (_, e) =>
            {
                lock (recordingLock)
                {
                    if (disposed || !ReferenceEquals(capture, active)) return;
                    var remaining = Math.Max(0, recordedFormat.AverageBytesPerSecond * 10L - recording.Length);
                    var count = (int)Math.Min(remaining, e.BytesRecorded);
                    count -= count % recordedFormat.BlockAlign;
                    recording.Write(e.Buffer, 0, count);
                    peak = AudioTestData.Peak(e.Buffer, count, recordedFormat); maximumPeak = Math.Max(maximumPeak, peak);
                }
            };
            active.RecordingStopped += (_, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed || !ReferenceEquals(capture, active)) return;
                StopRecording(); if (e.Exception is not null) { replay.IsEnabled = false; Failure(e.Exception, true); }
            }));
            started = DateTime.UtcNow; active.StartRecording(); timer.Start(); record.Content = "结束录音";
        }
        catch (Exception e) when (MediaFailure.Expected(e)) { StopRecording(); Failure(e, true); }
    }

    private void StopRecording()
    {
        timer.Stop(); var active = capture; capture = null;
        try { active?.StopRecording(); } catch (Exception e) when (MediaFailure.Expected(e)) { }
        active?.Dispose(); captureDevice?.Dispose(); captureDevice = null;
        record.Content = "重新录音";
        lock (recordingLock)
        {
            stoppedRecording = recording.Length > 0;
            replay.IsEnabled = stoppedRecording;
            if (active is not null) status.Text = maximumPeak > .001 ? "录音结束，请回放并确认声音是否清晰。" : "刚才没有收到明显声音。请检查静音、距离或切换输入设备后重试。";
        }
    }

    private void Replay()
    {
        if (!stoppedRecording || recordedFormat is null) return;
        byte[] bytes; double max; lock (recordingLock) { bytes = recording.ToArray(); max = maximumPeak; }
        var seconds = bytes.Length / (double)recordedFormat.AverageBytesPerSecond;
        Play(bytes, recordedFormat, () =>
        {
            var detail = $"输入：{recordedName}；回放：{output.Text}。已录制并回放 {seconds:0.0} 秒，峰值 {max:P0}。";
            status.Text = max > .001 ? "回放结束。请根据听到的声音确认结果。" : "录音中没有明显声音，请重试；不能据此判定麦克风损坏。";
            report(new(detail, max > .001, "检查静音和麦克风权限，或切换输入设备后重新录音。"));
        });
    }

    private void PlayTone(int channel)
    {
        Play(AudioTestData.Tone(channel), new WaveFormat(48000, 16, 2), () =>
        {
            if (channel == 0) leftPlayed = true; else rightPlayed = true;
            status.Text = leftPlayed && rightPlayed ? "两个声道均已播放，请确认声音与左右方向。" : "请继续播放另一个声道。";
            report(new($"输出：{output.Text}；左声道{(leftPlayed ? "已播放" : "未播放")}，右声道{(rightPlayed ? "已播放" : "未播放")}。声音及方向由用户确认。", leftPlayed && rightPlayed,
                "检查音量、静音和耳机连接，或切换输出设备后重新测试。"));
        });
    }

    private void Play(byte[] bytes, WaveFormat format, Action completed)
    {
        if (disposed) return;
        StopPlayback();
        report(new("正在播放，请听完后再确认。", false));
        if (output.SelectedItem is not Endpoint selected) { status.Text = "请先选择回放或输出设备。"; return; }
        try
        {
            using var devices = new MMDeviceEnumerator(); playbackDevice = devices.GetDevice(selected.Id);
            source = new RawSourceWaveStream(new MemoryStream(bytes, false), format);
            player = new WasapiOut(playbackDevice, AudioClientShareMode.Shared, true, 100);
            var active = player;
            player.PlaybackStopped += (_, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed || !ReferenceEquals(player, active)) return;
                StopPlayback(); if (e.Exception is not null) Failure(e.Exception); else completed();
            }));
            player.Init(source); player.Play(); status.Text = "正在播放…";
        }
        catch (Exception e) when (MediaFailure.Expected(e)) { StopPlayback(); Failure(e); }
    }
    private void StopPlayback()
    {
        var active = player; player = null;
        try { active?.Stop(); } catch (Exception e) when (MediaFailure.Expected(e)) { }
        active?.Dispose(); source?.Dispose(); source = null; playbackDevice?.Dispose(); playbackDevice = null;
    }
    private void Stop() { StopRecording(); StopPlayback(); }
    private void Failure(Exception error, bool capturing = false)
    {
        if (capturing && microphone && MediaPermission.IsDenied(error))
        {
            status.Text = MediaPermission.Request(Window.GetWindow(this), true);
            report(new(status.Text, false, Failed: true));
            return;
        }
        var message = MediaFailure.Describe(error, microphone ? "音频" : "输出设备");
        status.Text = message; report(new(message, false, "检查设备连接、静音及隐私权限，关闭占用设备的软件后重试。", Failed: true));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; Stop();
        lock (recordingLock) { if (recording.TryGetBuffer(out var buffer)) Array.Clear(buffer.Array!, 0, (int)recording.Length); recording.Dispose(); }
    }
}

public static class MediaFailure
{
    public static bool Expected(Exception e) => e is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NAudio.MmException or NotSupportedException or System.ComponentModel.Win32Exception;
    public static string Describe(Exception e, string device) => e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005)
        ? $"无法访问{device}：Windows 未允许此次访问。请检查设备访问设置。"
        : $"{device}暂时无法使用：可能已断开、被其他软件占用，或当前格式不受支持。请刷新设备后重试（代码 0x{e.HResult:X8}）。";
}
