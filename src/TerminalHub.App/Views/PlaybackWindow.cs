using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using TerminalHub.App.Controls;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Views;

public sealed class PlaybackWindow : Window
{
    public PlaybackWindow(TerminalPlayback playback)
    {
        Title = "回放 · " + playback.Header.Title; Width = 1100; Height = 720;
        this.Bind(BackgroundProperty, this.GetResourceObservable("UiCanvas"));
        var terminal = new TerminalView { Emulator = playback.Emulator, IsPreview = true, FullFramePreview = true };
        var grid = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(18) }; grid.Children.Add(terminal);
        var controls = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto"), Margin = new Thickness(0,16,0,0) }; Grid.SetRow(controls, 1); grid.Children.Add(controls);
        var play = new Button { Content = "播放", Padding = new Thickness(16,8) }; controls.Children.Add(play);
        var speed = new ComboBox { ItemsSource = new[] { .5, 1d, 2d, 4d }, SelectedIndex = 1, Width = 90, Margin = new Thickness(12,0) }; Grid.SetColumn(speed, 1); controls.Children.Add(speed);
        var slider = new Slider { Minimum = 0, Maximum = Math.Max(1, playback.DurationMs), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(slider, 2); controls.Children.Add(slider);
        var label = new TextBlock { Width = 130, Margin = new Thickness(16,0,0,0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("UiInk")); Grid.SetColumn(label, 3); controls.Children.Add(label);
        Content = grid; bool playing = false, ticking = false; var previous = Stopwatch.GetTimestamp();
        void Update(double position)
        { playback.Seek(position); terminal.Emulator = playback.Emulator; label.Text = $"{TimeSpan.FromMilliseconds(playback.PositionMs):mm\\:ss} / {TimeSpan.FromMilliseconds(playback.DurationMs):mm\\:ss}"; terminal.InvalidateVisual(); }
        play.Click += (_, _) => { if (playback.PositionMs >= playback.DurationMs) { slider.Value = 0; Update(0); } playing = !playing; play.Content = playing ? "暂停" : "播放"; previous = Stopwatch.GetTimestamp(); };
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !ticking) { Update(slider.Value); previous = Stopwatch.GetTimestamp(); } };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) =>
        {
            var elapsed = Stopwatch.GetElapsedTime(previous).TotalMilliseconds; previous = Stopwatch.GetTimestamp(); if (!playing) return;
            Update(playback.PositionMs + elapsed * (speed.SelectedItem is double n ? n : 1)); ticking = true; slider.Value = playback.PositionMs; ticking = false;
            if (playback.PositionMs >= playback.DurationMs) { playing = false; play.Content = "播放"; }
        };
        Opened += (_, _) => timer.Start(); Closed += (_, _) => { timer.Stop(); playback.Dispose(); }; Update(0);
    }
}
