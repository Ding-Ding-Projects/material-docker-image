using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The on-screen PIN keypad shown under the PIN field of the unlock prompt. It fills the same field that typing fills, so the keypad and the keyboard feed one validator and one
/// attempt budget. Keys are large touch targets with their own accessible names, and what a screen reader is told is only how many digits are entered, never which.
/// </summary>
public static class PinKeypadView
{
    public static FrameworkElement Create(PasswordBox field, Action rebuild)
    {
        var host = AppHost.Current;
        var shuffle = host.Settings.PinKeypadShuffle;
        var pad = new PinKeypad();
        pad.SetFromManual(field.Password);
        var announce = Ui.Caption("");
        string Said() => pad.Length == 0 ? Loc.T("No digits entered") : pad.Length == 1 ? Loc.T("1 digit entered") : pad.Length + " " + Loc.T("digits entered");
        announce.Text = Said();
        announce.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);

        void Sync()
        {
            field.Password = pad.EntryForField();
            announce.Text = Said();
        }

        Button Key(KeypadKey key)
        {
            var label = key.Kind == KeypadKind.Digit ? key.Digit.ToString() : Loc.T(key.Label);
            var button = Ui.Button(label, null, ButtonKind.Tonal, () =>
            {
                pad.SetFromManual(field.Password);
                switch (key.Kind)
                {
                    case KeypadKind.Digit: pad.Press(key.Digit); break;
                    case KeypadKind.Backspace: pad.Backspace(); break;
                    case KeypadKind.Clear: pad.Clear(); break;
                }
                Sync();
                field.Focus();
            }, false);
            button.MinWidth = 72; button.MinHeight = 56; button.Margin = new Thickness(3);
            AutomationProperties.SetName(button, key.Kind == KeypadKind.Digit ? Loc.T("Digit") + " " + key.Digit : Loc.T(PinKeypad.AccessibleName(key)));
            return button;
        }

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var layout = PinKeypad.Layout(shuffle);
        for (var r = 0; r < layout.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < layout[r].Count; c++) grid.Children.Add(Key(layout[r][c]).At(r, c));
        }
        var shuffleSwitch = Ui.Switch("Shuffle the keys", shuffle, v => { host.Settings.PinKeypadShuffle = v; host.SaveSettings(null); rebuild(); },
            "Off by default. Shuffling moves the digits around so the pad cannot be read from finger positions.");
        AutomationProperties.SetName(grid, Loc.T("PIN keypad"));
        return Ui.VStack(8, Ui.Caption("Or use the keypad. The keyboard digits work too."), grid, announce, shuffleSwitch);
    }
}
