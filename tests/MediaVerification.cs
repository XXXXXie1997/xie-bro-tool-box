using System.Runtime.InteropServices;
using System.Windows.Controls;
using NAudio.Wave;
using XieBox.Contracts;
using XieBox.HealthCheck;

internal static partial class Program
{
    private static void CheckMediaVerification(IPlatformContext context, string preview)
    {
        foreach (var channel in new[] { 0, 1 })
        {
            var bytes = AudioTestData.Tone(channel);
            bool audible = false, silentOther = true;
            for (var i = 0; i < bytes.Length; i += 4)
            {
                audible |= BitConverter.ToInt16(bytes, i + channel * 2) != 0;
                silentOther &= BitConverter.ToInt16(bytes, i + (1 - channel) * 2) == 0;
            }
            Check(bytes.Length == 48000 * 4 && audible && silentOther, $"audio channel {channel} has one second of signal only in the selected stereo channel");
        }
        Check(AudioTestData.Peak(new byte[128], 128, new WaveFormat(48000, 16, 1)) == 0, "silent microphone data is not treated as a signal");
        Check(Math.Abs(AudioTestData.Peak(BitConverter.GetBytes((short)16384), 2, new WaveFormat(48000, 16, 1)) - .5) < .001, "PCM microphone level is normalized correctly");
        Check(AudioTestData.Peak(BitConverter.GetBytes(float.NaN), 4, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)) == 0, "invalid floating microphone sample cannot poison the meter");
        Check(MediaFailure.Describe(new COMException("fixture", unchecked((int)0x80070005)), "摄像头").Contains("Windows 未允许此次访问"), "camera access denial is explained separately from device absence");
        using (var mic = new AudioCheckView(true, _ => { }))
        using (var output = new AudioCheckView(false, _ => { }))
        using (var camera = new CameraCheckView(_ => { }))
        {
            RenderUtility(mic, System.IO.Path.Combine(preview, "health-microphone.png"));
            RenderUtility(output, System.IO.Path.Combine(preview, "health-audio.png"));
            RenderUtility(camera, System.IO.Path.Combine(preview, "health-camera.png"));
            Check(!TabDescendants(camera).OfType<Button>().Any(b => Equals(b.Content, "打开系统相机")), "camera verification uses internal preview instead of opening another application");
        }
        var model = new HealthModel(context, [], _ => GuidedChecks.Items.ToDictionary(i => i.Kind, _ => new Presence(true, "fixture connected")));
        RunUi(model.CheckAsync);
        Check(model.ManualResults.All(r => r.State == HealthState.Pending), "device presence alone never passes functional verification");
        model.RecordManualState("麦克风", HealthState.Incomplete, "未收到声音", "切换输入设备后重试");
        Check(model.ManualResults.Single(r => r.Name == "麦克风").State == HealthState.Incomplete && model.BuildReport().Contains("下一步：切换输入设备后重试"), "incomplete media verification retains actionable guidance in report");
        model.RecordManualState("麦克风", HealthState.Normal, "用户确认回放清晰");
        Check(model.ManualResults.Single(r => r.Name == "麦克风").NextStep == "", "successful retest clears previous failure advice");
        model.RecordManualState("摄像头", HealthState.Skipped, "本次跳过");
        Check(model.ManualResults.Single(r => r.Name == "摄像头").State == HealthState.Skipped, "skipped camera is not marked healthy");
        RunUi(model.RefreshDevicesAsync);
        Check(model.ManualResults.All(r => r.State == HealthState.Pending), "device refresh invalidates old manual results");
        var owner = new System.Windows.Window { Width = 900, Height = 700, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        owner.Show();
        try
        {
            foreach (var kind in new[] { "screen", "input", "audio", "microphone", "camera", "ports" })
            {
                var check = GuidedChecks.Items.Single(i => i.Kind == kind);
                bool captured = false;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (_, _) =>
                {
                    var dialog = System.Windows.Application.Current.Windows.OfType<XieBox.Platform.UI.DialogWindow>().FirstOrDefault(w => w.Title == check.Title + " · 功能验证");
                    if (dialog is null) return;
                    timer.Stop(); dialog.UpdateLayout();
                    Render((System.Windows.FrameworkElement)dialog.Content, System.IO.Path.Combine(preview, "health-dialog-" + kind + ".png"), dialog.ActualWidth - 2, dialog.ActualHeight - 42);
                    XieBox.Services.ThemePalette.Apply(new System.Windows.ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Palette.Dark.xaml", UriKind.Relative) });
                    dialog.UpdateLayout();
                    Render((System.Windows.FrameworkElement)dialog.Content, System.IO.Path.Combine(preview, "health-dialog-" + kind + "-dark.png"), dialog.ActualWidth - 2, dialog.ActualHeight - 42);
                    XieBox.Services.ThemePalette.Apply(new System.Windows.ResourceDictionary { Source = new Uri("/XieBroToolBox;component/Resources/Palette.Light.xaml", UriKind.Relative) });
                    var buttons = TabDescendants(dialog).OfType<Button>().ToArray();
                    Check(buttons.Count(b => Equals(b.Content, "跳过") || Equals(b.Content, "发现问题") || Equals(b.Content, "正常")) == 3 && !buttons.Any(b => Equals(b.Content, "未能完成")), kind + " has only three result choices");
                    var notes = TabDescendants(dialog).OfType<Expander>().Single(e => Equals(e.Header, "添加备注（可选）"));
                    Check(!notes.IsExpanded, kind + " hides optional notes initially");
                    notes.IsExpanded = true; dialog.UpdateLayout();
                    Check(TabDescendants(notes).OfType<TextBox>().Any(t => t.IsVisible), kind + " allows expanding and editing notes");
                    captured = dialog.ActualHeight <= dialog.MaxHeight && dialog.ActualHeight > 200;
                    dialog.Close();
                };
                timer.Start();
                try { GuidedChecks.Show(owner, check, model); } finally { timer.Stop(); }
                Check(captured, kind + " guided dialog fits content and closes without starting media capture");
            }
        }
        finally { owner.Close(); }
    }
}
