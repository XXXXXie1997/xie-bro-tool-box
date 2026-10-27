using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace XieBox.Platform.UI;

public static class StatusAttention
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(StatusAttention), new PropertyMetadata("", Changed));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(StatusAttention));
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    private sealed class State
    {
        public string? Shown;
        public TranslateTransform? Offset;
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBlock text) return;
        if (text.GetValue(StateProperty) is not State)
        {
            text.SetValue(StateProperty, new State());
            text.Loaded += (_, _) => Show(text);
            text.IsVisibleChanged += (_, _) => { if (text.IsVisible) Show(text); else Stop(text); };
            text.Unloaded += (_, _) => Stop(text);
        }
        // Clear stale movement immediately when the message changes, even if it becomes informational.
        Stop(text);
        Show(text);
    }
    private static void Stop(TextBlock text)
    {
        if (text.GetValue(StateProperty) is State { Offset: { } offset })
        {
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            offset.X = 0;
        }
    }
    private static void Show(TextBlock text)
    {
        if (!text.IsLoaded || !text.IsVisible || text.GetValue(StateProperty) is not State state) return;
        var message = GetText(text);
        if (state.Shown == message) return;
        state.Shown = message;
        if (StatusToneConverter.Classify(message) == "Info" || !SystemParameters.ClientAreaAnimation) return;
        if (state.Offset is null)
        {
            state.Offset = new TranslateTransform();
            var transform = new TransformGroup();
            transform.Children.Add(text.RenderTransform);
            transform.Children.Add(state.Offset);
            text.RenderTransform = transform;
        }
        var motion = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360), FillBehavior = FillBehavior.Stop };
        foreach (var (ms, x) in new[] { (0, 0d), (45, -4d), (100, 4d), (160, -3d), (220, 3d), (285, -1.5d), (360, 0d) })
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        state.Offset.BeginAnimation(TranslateTransform.XProperty, motion, HandoffBehavior.SnapshotAndReplace);
    }
}
