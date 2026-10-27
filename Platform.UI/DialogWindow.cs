using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace XieBox.Platform.UI;

/// <summary>Shared chrome and lifecycle for platform and module dialogs.</summary>
public class DialogWindow : Window
{
    private bool _closingAnimation;
    private bool _closeReady;

    public DialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(DialogWindow));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Close, Key.Escape, ModifierKeys.None));
        SourceInitialized += (_, _) => { if (SystemParameters.ClientAreaAnimation) Opacity = 0; };
        Loaded += (_, _) =>
        {
            if (!_closingAnimation && SystemParameters.ClientAreaAnimation)
                BeginAnimation(OpacityProperty, Fade(1, "Motion.EnterDuration", 260));
            else if (!_closingAnimation) Opacity = 1;
        };
    }

    private DoubleAnimation Fade(double to, string resource, int fallback) => new(to,
        TryFindResource(resource) is Duration duration ? duration : new Duration(TimeSpan.FromMilliseconds(fallback)))
    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeReady || !IsVisible || !SystemParameters.ClientAreaAnimation || Dispatcher.HasShutdownStarted) return;
        e.Cancel = true;
        if (_closingAnimation) return;
        _closingAnimation = true;
        IsHitTestVisible = false;
        var fade = Fade(0, "Motion.FeedbackDuration", 140);
        // Closing must be retried after the original Closing event has returned.
        fade.Completed += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            _closeReady = true;
            Close();
            if (IsVisible) // A consumer may veto the final close.
            {
                _closeReady = _closingAnimation = false;
                IsHitTestVisible = true;
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
            }
        }));
        BeginAnimation(OpacityProperty, fade);
    }

    public DialogWindow FitContent()
    {
        SizeToContent = SizeToContent.Height;
        MinHeight = 0;
        MaxHeight = Math.Min(600, Math.Max(160, SystemParameters.WorkArea.Height - 80));
        ResizeMode = ResizeMode.NoResize;
        return this;
    }

    public static bool Confirm(Window owner, string message, string title, string acceptLabel)
    {
        var accepted = false;
        var dialog = new DialogWindow { Owner = owner, Title = title, Width = 520 }.FitContent();
        var panel = new DockPanel { Margin = new Thickness(24, 20, 24, 24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "取消", IsDefault = true, MinWidth = 90 };
        var accept = new Button { Content = acceptLabel, MinWidth = 100, Margin = new Thickness(12, 0, 0, 0) };
        accept.SetResourceReference(StyleProperty, "PrimaryButton");
        cancel.Click += (_, _) => dialog.Close();
        accept.Click += (_, _) => { accepted = true; dialog.Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(accept);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { MaxHeight = Math.Max(60, dialog.MaxHeight - 160), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 24 } });
        dialog.Content = panel;
        dialog.ShowDialog();
        return accepted;
    }
}

