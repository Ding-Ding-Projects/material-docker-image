using System.IO;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.Shell;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Appearance &gt; Title-bar logo: default glyph, any bundled Material Symbol, or a local image copied into the app data folder.</summary>
public sealed partial class SettingsPage
{
    private FrameworkElement LogoControl()
    {
        var previewSlot = new Border { MinWidth = 48, MinHeight = 48, VerticalAlignment = VerticalAlignment.Center };
        var status = Ui.Caption("");
        var symbolError = Ui.Text("", 12, null, "Error");
        void Refresh()
        {
            previewSlot.Child = AppLogo.CreatePreview(S, 32);
            var resolved = AppIdentity.Resolve(S.LogoKind, S.LogoSymbol, S.LogoImageFile, UserSettings.DataDirectory);
            if (resolved.ImagePath != null && AppLogo.TryLoad(resolved.ImagePath, out _) == null) status.Text = Loc.T("The custom logo file is missing or invalid, so the default logo is shown.");
            else if (resolved.Notice.Length > 0) status.Text = Loc.T(resolved.Notice);
            else status.Text = Loc.T(resolved.Kind switch { LogoKinds.Symbol => "Using the symbol", LogoKinds.Image => "Using your own image", _ => "Using the default logo" }) + (resolved.Kind == LogoKinds.Symbol ? " " + S.LogoSymbol : "") + ".";
        }
        void Apply(string kind, string symbol, string imageFile, string summary)
        {
            S.LogoKind = kind; S.LogoSymbol = symbol; S.LogoImageFile = imageFile;
            Commit(summary);
            Refresh();
        }

        var symbolBox = Ui.Input(S.LogoKind == LogoKinds.Symbol ? S.LogoSymbol : "", "Logo symbol name");
        symbolBox.MinWidth = 220;
        var suggestions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void UseSymbol(string text)
        {
            if (!AppIdentity.TryResolveSymbol(text, out var canonical, out _)) { symbolError.Text = Loc.T("That symbol is not in the bundled set. Try a suggestion below."); return; }
            symbolError.Text = "";
            symbolBox.Text = canonical;
            if (S.LogoKind == LogoKinds.Symbol && S.LogoSymbol == canonical) return;
            Apply(LogoKinds.Symbol, canonical, S.LogoImageFile, "Logo symbol set to " + canonical);
        }
        void Suggest()
        {
            suggestions.Children.Clear();
            foreach (var name in AppIdentity.SuggestSymbols(symbolBox.Text, AppIdentity.BundledSymbols, 12))
                suggestions.Children.Add(Ui.Button(name, AppIdentity.BundledSymbols[name], ButtonKind.Text, () => UseSymbol(name), false).Margin(0, 0, 6, 4));
        }
        symbolBox.TextChanged += (_, _) => { symbolError.Text = ""; Suggest(); };
        symbolBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) UseSymbol(symbolBox.Text); };
        Suggest();
        var useSymbol = Ui.Button("Use symbol", Icons.Check, ButtonKind.Tonal, () => UseSymbol(symbolBox.Text));

        var choose = Ui.Button("Choose image…", Icons.FolderOpen, ButtonKind.Tonal, () =>
        {
            var dialog = new OpenFileDialog { Filter = "Images (PNG, ICO, JPG)|*.png;*.ico;*.jpg;*.jpeg", Title = Loc.T("Choose a logo image") };
            if (dialog.ShowDialog() != true) return;
            var check = AppIdentity.CheckImageFile(dialog.FileName);
            if (!check.Ok) { Host.Notify.Error(Loc.T(check.Error)); return; }
            if (AppLogo.TryLoad(dialog.FileName, out var decodeError) == null) { Host.Notify.Error(Loc.T(decodeError)); return; }
            var stored = AppIdentity.StoreImage(dialog.FileName, UserSettings.DataDirectory);
            if (!stored.Ok) { Host.Notify.Error(Loc.T(stored.Error)); return; }
            Apply(LogoKinds.Image, S.LogoSymbol, stored.StoredName, "Logo set to a custom image");
            Host.Notify.Success(Loc.T("The logo image was copied into the app data folder. It is never uploaded."));
        });
        var reset = Ui.Button("Use default logo", Icons.RestartAlt, ButtonKind.Text, () =>
        {
            if (S.LogoKind == LogoKinds.Default && S.LogoImageFile.Length == 0 && S.LogoSymbol == AppIdentity.DefaultSymbolName) { Refresh(); return; }
            AppIdentity.ClearStoredImages(UserSettings.DataDirectory);
            symbolBox.Text = "";
            Apply(LogoKinds.Default, AppIdentity.DefaultSymbolName, "", "Logo reset to the default");
        });

        Refresh();
        return Ui.VStack(8,
            Ui.HStack(12, previewSlot, status, reset),
            Ui.HStack(8, symbolBox, useSymbol), symbolError, suggestions,
            new DockPanel { Children = { choose.Margin(0, 0, 8, 0).Dock(Dock.Left), Ui.Caption("PNG, ICO or JPG up to 1 MiB. The file is copied into the app data folder and never uploaded.") } });
    }
}
