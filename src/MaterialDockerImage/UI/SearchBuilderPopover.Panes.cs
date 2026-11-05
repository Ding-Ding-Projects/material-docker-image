using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// The three-pane arrangement of the builder: tokens and library on the left, the pattern with its flags, explanation and tests in the
/// centre, and captures, replacement, conversion and actions on the right. Panes only arrange the existing panels; every panel keeps its
/// controls, its per-field state and its keyboard gesture. A builder narrower than three panes lets the side panes take turns beside the
/// pattern, and a very narrow one shows a single pane at a time. The pane buttons above the panes switch them with the keyboard or pointer.
/// </summary>
internal sealed partial class SearchBuilderPopover
{
    private const int LeftPane = 0, CenterPane = 1, RightPane = 2;
    private const double WideWidth = 1100, NarrowWidth = 720, LeftWidth = 260, RightWidth = 340, MediumRightWidth = 300;
    private const double MaxCardWidth = 1300, MaxCardHeight = 800;

    private static readonly string[] PaneTitles = ["Tokens and library", "Pattern and test", "Captures and actions"];
    private static readonly string[] PaneTips =
    [
        "Shows or hides the pane with the insert palette, the library and my patterns.",
        "Shows the pane with the pattern, its flags, the explanation and the tests. On a wide builder it is always shown.",
        "Shows or hides the pane with the captures, the replace preview, conversion and the actions.",
    ];

    /// <summary>The pane each panel belongs to. Panels not listed here sit in the centre pane.</summary>
    private static readonly IReadOnlyDictionary<string, int> PaneOfPanel = new Dictionary<string, int>
    {
        ["insert"] = LeftPane,
        ["replace"] = RightPane, ["convert"] = RightPane, ["templates"] = RightPane, ["preferences"] = RightPane,
    };

    private readonly StackPanel[] paneStacks = [Ui.VStack(14), Ui.VStack(14), Ui.VStack(14)];
    private readonly Border[] paneHosts = new Border[3];
    private readonly ToggleButton[] paneChips = new ToggleButton[3];
    private readonly Grid paneGrid = new();
    private readonly TextBlock paneNote = Ui.Caption("");
    private FrameworkElement? capturesElement;
    private bool panesBuilt, paneSyncing, showLeft = true, showRight = true;
    private int lastSide = RightPane, narrowPane = CenterPane;

    private static int PaneFor(string id) => PaneOfPanel.TryGetValue(id, out var pane) ? pane : CenterPane;

    private FrameworkElement BuildPanes()
    {
        var chips = new WrapPanel();
        AutomationProperties.SetName(chips, Loc.T("Panes"));
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var scroller = new ScrollViewer
            {
                Content = paneStacks[i], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(i == CenterPane ? 4 : 14, 12, i == CenterPane ? 8 : 14, 12), Focusable = false,
            };
            var host = new Border { Child = scroller, CornerRadius = new CornerRadius(12), Margin = new Thickness(i == RightPane ? 8 : 0, 0, i == LeftPane ? 8 : 0, 0) };
            if (i != CenterPane) host.SetResourceReference(Border.BackgroundProperty, "SurfaceLow");
            AutomationProperties.SetName(host, Loc.T(PaneTitles[i]));
            Grid.SetColumn(host, i);
            paneGrid.ColumnDefinitions.Add(new ColumnDefinition());
            paneGrid.Children.Add(host);
            paneHosts[i] = host;

            var chip = Ui.Chip(PaneTitles[i], true, v => OnPaneChip(index, v));
            chip.ToolTip = Loc.T(PaneTips[i]);
            AutomationProperties.SetHelpText(chip, Loc.T(PaneTips[i]));
            chip.Margin = new Thickness(0, 0, 6, 6);
            paneChips[i] = chip;
            chips.Children.Add(chip);
        }
        paneNote.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = Header();
        header.Margin = new Thickness(0, 0, 0, 8);
        var toolbar = Ui.VStack(0, chips, paneNote);
        toolbar.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(paneGrid, 2);
        root.Children.Add(header);
        root.Children.Add(toolbar);
        root.Children.Add(paneGrid);
        panesBuilt = true;
        return root;
    }

    /// <summary>Captures of the last test sit at the top of the right pane, as in the design. They follow the Test panel when it is hidden or shown.</summary>
    private FrameworkElement CapturesSection() => Section("Captures", "Every group of every match in the sample. Groups appear here when the pattern has capture groups and the sample matches.", groups);

    /// <summary>Three panes on a wide builder, the pattern plus one side pane on a medium one, a single pane on a narrow one.</summary>
    private int PaneMode()
    {
        var width = double.IsNaN(card.Width) ? card.ActualWidth : card.Width;
        return width <= 0 || width >= WideWidth ? 3 : width >= NarrowWidth ? 2 : 1;
    }

    private void OnPaneChip(int pane, bool on)
    {
        if (paneSyncing) return;
        if (PaneMode() == 1) narrowPane = pane;
        else if (pane == LeftPane) { showLeft = on; if (on) lastSide = LeftPane; }
        else if (pane == RightPane) { showRight = on; if (on) lastSide = RightPane; }
        UpdatePanes();
    }

    /// <summary>Makes sure the pane that holds a panel is shown, so a keyboard jump (F6, Ctrl+digit, Alt+Home) never lands in a hidden pane.</summary>
    private void RevealPane(int pane)
    {
        if (PaneMode() == 1) narrowPane = pane;
        else if (pane == LeftPane) { showLeft = true; lastSide = LeftPane; }
        else if (pane == RightPane) { showRight = true; lastSide = RightPane; }
        UpdatePanes();
        card.UpdateLayout();
    }

    private void UpdatePanes()
    {
        if (!panesBuilt) return;
        var mode = PaneMode();
        bool left, center, right;
        if (mode == 3) { left = showLeft; center = true; right = showRight; }
        else if (mode == 2)
        {
            center = true;
            if (showLeft && showRight) { left = lastSide == LeftPane; right = !left; }
            else { left = showLeft; right = showRight; }
        }
        else { left = narrowPane == LeftPane; center = narrowPane == CenterPane; right = narrowPane == RightPane; }

        var star = new GridLength(1, GridUnitType.Star);
        SetPane(LeftPane, left, mode == 1 ? star : new GridLength(LeftWidth));
        SetPane(CenterPane, center, star);
        SetPane(RightPane, right, mode == 1 ? star : new GridLength(mode == 3 ? RightWidth : MediumRightWidth));

        paneSyncing = true;
        try { paneChips[LeftPane].IsChecked = left; paneChips[CenterPane].IsChecked = center; paneChips[RightPane].IsChecked = right; }
        finally { paneSyncing = false; }
        paneNote.Text = mode switch
        {
            3 => "",
            2 => Loc.T("The builder is narrower than three panes, so the two side panes take turns beside the pattern. Use the pane buttons to switch."),
            _ => Loc.T("The builder is narrow, so one pane is shown at a time. Use the pane buttons to switch."),
        };
        paneNote.Visibility = paneNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        // Keyboard focus must not stay inside a pane that was just hidden.
        if (Keyboard.FocusedElement is DependencyObject focused)
            for (var i = 0; i < 3; i++)
                if (paneHosts[i].Visibility != Visibility.Visible && paneHosts[i].IsAncestorOf(focused)) paneChips[i].Focus();
    }

    private void SetPane(int pane, bool visible, GridLength width)
    {
        paneGrid.ColumnDefinitions[pane].Width = visible ? width : new GridLength(0);
        paneHosts[pane].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Sizes the card to the window: up to 1300 by 800, leaving a margin on every side. The builder has its own
    /// Pattern panel, so it is centred on the window and may cover the field it edits.</summary>
    private void SizeToWindow()
    {
        var window = Window.GetWindow(field);
        var windowWidth = window?.ActualWidth ?? 960;
        var windowHeight = window?.ActualHeight ?? 720;
        card.Width = Math.Min(MaxCardWidth, Math.Max(320, windowWidth - 48));
        card.Height = Math.Min(MaxCardHeight, Math.Max(300, windowHeight - 80));
        UpdatePanes();
    }

    /// <summary>Vertical offset, in device pixels relative to the field, that centres the card on the window.</summary>
    private double CenteredTop()
    {
        var window = Window.GetWindow(field);
        if (window == null || !window.IsAncestorOf(field)) return field.ActualHeight + 4;
        var fieldTop = field.TranslatePoint(new Point(0, 0), window).Y;
        var total = card.Height + card.Margin.Top + card.Margin.Bottom;
        var dpi = PresentationSource.FromVisual(field)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        return Math.Max(-fieldTop, (window.ActualHeight - total) / 2 - fieldTop) * dpi;
    }

    /// <summary>Horizontal offset, in device pixels relative to the field, that centres the card on the window.</summary>
    private double CenteredOffset()
    {
        var window = Window.GetWindow(field);
        if (window == null || !window.IsAncestorOf(field)) return 0;
        var fieldLeft = field.TranslatePoint(new Point(0, 0), window).X;
        var total = card.Width + card.Margin.Left + card.Margin.Right;
        var dpi = PresentationSource.FromVisual(field)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return Math.Max(-fieldLeft, (window.ActualWidth - total) / 2 - fieldLeft) * dpi;
    }
}
