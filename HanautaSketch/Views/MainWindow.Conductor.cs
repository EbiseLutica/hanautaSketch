using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HanautaSketch.Controls;
using HanautaSketch.Engine.Core;
using Key = HanautaSketch.Engine.Core.Key;

namespace HanautaSketch.Views;

/// <summary>
/// コンダクターレーン（テンポ・拍子・キーの変更点）の編集用フライアウト。
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// レーンのクリックで、その位置の変更点を追加・編集するフライアウトをカーソル位置に出す。
    /// 初期値は、既存の変更点ならその値、追加ならその位置で効いている値。
    /// </summary>
    private void OnConductorLaneClicked(object? sender, ConductorLaneClickedEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not Control target) return;

        var conductor = vm.Song.Conductor;
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 8, MinWidth = 180 };
        Action apply;

        switch (e.Kind)
        {
            case ConductorChangeKind.Tempo:
            {
                var input = new NumericUpDown
                {
                    Minimum = (decimal)Conductor.MinBpm, Maximum = (decimal)Conductor.MaxBpm, Increment = 1,
                    FormatString = "0.##", Value = (decimal)conductor.BpmAt(e.Position),
                };
                panel.Children.Add(Labeled("テンポ（BPM）", input));
                apply = () => vm.SetTempoChange(e.Position, (double)(input.Value ?? 120));
                break;
            }
            case ConductorChangeKind.Meter:
            {
                var input = new NumericUpDown
                {
                    Minimum = Conductor.MinBeatsPerBar, Maximum = Conductor.MaxBeatsPerBar, Increment = 1,
                    FormatString = "0", Value = conductor.BeatsPerBarAt(conductor.BarToTick(e.Position)),
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.Add(input);
                row.Children.Add(new TextBlock { Text = "/ 4", VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(Labeled($"拍子（{e.Position + 1}小節目から）", row));
                apply = () => vm.SetMeterChange(e.Position, (int)(input.Value ?? 4));
                break;
            }
            default:
            {
                var current = conductor.KeyAt(e.Position);
                var tonic = new ComboBox { ItemsSource = Key.PitchNames, SelectedIndex = current.Tonic, Width = 80 };
                var mode = new ComboBox { ItemsSource = Enum.GetValues<Mode>(), SelectedItem = current.Mode, Width = 100 };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.Add(tonic);
                row.Children.Add(mode);
                panel.Children.Add(Labeled("キー", row));
                apply = () => vm.SetKeyChange(e.Position,
                    new Key(Math.Max(0, tonic.SelectedIndex), mode.SelectedItem as Mode? ?? Mode.Major));
                break;
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var ok = new Button { Content = e.IsExisting ? "変更" : "追加" };
        ok.Click += (_, _) =>
        {
            apply();
            flyout.Hide();
        };
        buttons.Children.Add(ok);

        if (e.IsExisting && e.Position > 0)
        {
            var remove = new Button { Content = "削除" };
            remove.Click += (_, _) =>
            {
                vm.RemoveConductorChange(e.Kind, e.Position);
                flyout.Hide();
            };
            buttons.Children.Add(remove);
        }
        panel.Children.Add(buttons);

        // Enter で確定（NumericUpDown の入力中も）
        panel.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            apply();
            flyout.Hide();
            args.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        flyout.Content = panel;
        flyout.ShowAt(target, true);
    }

    private void OnConductorChangeMoveRequested(object? sender, ConductorChangeMoveRequestedEventArgs e) =>
        ViewModel?.MoveConductorChange(e.Kind, e.From, e.To);

    private static StackPanel Labeled(string label, Control content)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.7 });
        panel.Children.Add(content);
        return panel;
    }
}
