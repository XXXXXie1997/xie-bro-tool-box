using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;


namespace XieBox.Platform.UI;

/// <summary>Pixel wheel scrolling shared by ordinary and virtualized scroll viewers.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits, Changed));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(SmoothScroll));
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
    public static void ScrollHorizontal(ScrollViewer viewer, double delta)
    {
        if (viewer.GetValue(StateProperty) is State state) state.ScrollBy(true, delta);
        else viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + delta);
    }
    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not ScrollViewer viewer) return;
        if (viewer.GetValue(StateProperty) is State previous) previous.Dispose();
        viewer.SetValue(StateProperty, (bool)args.NewValue ? new State(viewer) : null);
    }

    private sealed class State : IDisposable
    {
        private readonly ScrollViewer _viewer;
        private bool _running;
        private readonly Stopwatch _clock = new();
        private bool _horizontal;
        private double _position, _velocity, _target, _frequency, _lastSeconds;
        private TimeSpan _lastFrame = TimeSpan.MinValue;
        public State(ScrollViewer viewer)
        {
            _viewer = viewer;
            viewer.PreviewMouseWheel += Wheel;
            viewer.PreviewMouseDown += Interrupt;
            viewer.PreviewKeyDown += Interrupt;
            viewer.Unloaded += Interrupt;
        }
        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || SystemParameters.WheelScrollLines == 0) return;
            // Parent viewers see tunneling events first; let the innermost viewer handle them.
            for (var node = e.OriginalSource as DependencyObject; node is not null && node != _viewer;)
            {
                if (node is ScrollViewer) return;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            var horizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || _viewer.ScrollableHeight <= 0;
            var maximum = horizontal ? _viewer.ScrollableWidth : _viewer.ScrollableHeight;
            if (maximum <= 0) return;
            var current = horizontal ? _viewer.HorizontalOffset : _viewer.VerticalOffset;
            var viewport = horizontal ? _viewer.ViewportWidth : _viewer.ViewportHeight;
            var distance = SystemParameters.WheelScrollLines < 0 ? viewport : SystemParameters.WheelScrollLines * 16.0;
            ScrollBy(horizontal, -e.Delta / 120.0 * distance);
            e.Handled = true;
        }
        public void ScrollBy(bool horizontal, double delta)
        {
            var maximum = horizontal ? _viewer.ScrollableWidth : _viewer.ScrollableHeight;
            var current = horizontal ? _viewer.HorizontalOffset : _viewer.VerticalOffset;
            var continuing = _running && _horizontal == horizontal;
            var target = Math.Clamp((continuing ? _target : current) + delta, 0, maximum);
            if (!continuing)
            {
                Stop();
                _position = current;
                _velocity = 0;
            }
            _horizontal = horizontal;
            _target = target;

            if (!SystemParameters.ClientAreaAnimation) { Stop(); Move(target); return; }
            var seconds = _viewer.TryFindResource("Motion.FeedbackDuration") is Duration duration && duration.HasTimeSpan
                ? Math.Max(0.05, duration.TimeSpan.TotalSeconds) : 0.14;
            _frequency = 5 / seconds;
            if (_running) return; // Keep position and velocity continuous across wheel events.
            _lastSeconds = 0;
            _lastFrame = TimeSpan.MinValue;
            _clock.Restart();
            _running = true;
            CompositionTarget.Rendering += Render;
        }
        private void Render(object? sender, EventArgs e)
        {
            if (e is not RenderingEventArgs frame || frame.RenderingTime == _lastFrame) return;
            _lastFrame = frame.RenderingTime;
            if (!_viewer.IsVisible) { Stop(); return; }
            var now = _clock.Elapsed.TotalSeconds;
            var dt = now - _lastSeconds;
            _lastSeconds = now;
            var maximum = _horizontal ? _viewer.ScrollableWidth : _viewer.ScrollableHeight;
            _target = Math.Clamp(_target, 0, maximum);
            // Exact critically damped spring: stable across refresh rates and delayed frames.
            var displacement = _position - _target;
            var coefficient = _velocity + _frequency * displacement;
            var decay = Math.Exp(-_frequency * dt);
            _position = _target + (displacement + coefficient * dt) * decay;
            _velocity = (_velocity - _frequency * coefficient * dt) * decay;
            var bounded = Math.Clamp(_position, 0, maximum);
            if (bounded != _position) { _position = bounded; _velocity = 0; }
            if (Math.Abs(_target - _position) < 0.15 && Math.Abs(_velocity) < 3)
            {
                Move(_target);
                Stop();
                return;
            }
            var current = _horizontal ? _viewer.HorizontalOffset : _viewer.VerticalOffset;
            if (Math.Abs(_position - current) >= 0.05) Move(_position);
        }
        private void Stop()
        {
            if (_running) CompositionTarget.Rendering -= Render;
            _running = false;
            _clock.Stop();
        }
        private void Move(double value)
        {
            if (_horizontal) _viewer.ScrollToHorizontalOffset(value);
            else _viewer.ScrollToVerticalOffset(value);
        }
        private void Interrupt(object? sender, RoutedEventArgs e) => Stop();
        public void Dispose()
        {
            Stop();
            _viewer.PreviewMouseWheel -= Wheel;
            _viewer.PreviewMouseDown -= Interrupt;
            _viewer.PreviewKeyDown -= Interrupt;
            _viewer.Unloaded -= Interrupt;
        }
    }
}



