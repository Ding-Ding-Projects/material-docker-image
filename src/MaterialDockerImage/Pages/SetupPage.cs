using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// First-run wizard: detect Docker, choose the engine context for this app, pick a project folder and personalize.
/// Progress is saved in settings, so it resumes where it was left. It never changes the global Docker context.
/// </summary>
public sealed class SetupPage : PageBase
{
    public override string Key => "Setup";
    private static readonly string[] StepNames = ["Detect Docker", "Choose engine context", "Pick a project folder", "Personalize"];
    private static readonly string[] StepTitles = ["Detect Docker", "Choose an engine context", "Pick a project folder", "Make it yours"];
    private static readonly string[] StepIntros =
    [
        "Checks that the Docker CLI is installed and that the engine answers.",
        "The app talks to Docker through a context. You can switch at any time from the title bar.",
        "Build uses this folder as its build context.",
        "Choose how the app looks and which language it uses."
    ];
    private readonly SetupSteps steps;
    private readonly StackPanel stepper = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock title = Ui.Title("", 26);
    private readonly TextBlock intro = Ui.Caption("");
    private readonly TextBlock summary = Ui.Caption("");
    private readonly ContentControl stepHost = new() { Focusable = false };
    private readonly Button back, skip, next, finish;
    private readonly FrameworkElement[] views;
    private int step;
    private bool contextsLoaded;
    private bool detectedShown;

    public SetupPage()
    {
        steps = new SetupSteps(UpdateNav);
        views = [steps.Detect(), steps.Context(), steps.Folder(), steps.Personalize()];
        back = Ui.Button("Back", Icons.ArrowBack, ButtonKind.Text, () => Go(step - 1));
        skip = Ui.Button("Skip for now", null, ButtonKind.Text, () => { if (step < 3) Go(step + 1); else Finish(); });
        next = Ui.Button("Next", Icons.ArrowForward, ButtonKind.Filled, () => Go(step + 1));
        finish = Ui.Button("Finish", Icons.Check, ButtonKind.Filled, Finish);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in new[] { back, skip, next, finish }) actions.Children.Add(b.Margin(8, 4, 0, 4));
        var footer = new DockPanel();
        footer.Children.Add(actions.Dock(Dock.Right));
        footer.Children.Add(summary);
        var card = Ui.Card(Ui.VStack(20,
            Ui.Text(Loc.T("Welcome to") + " " + Host.Settings.DisplayName, 14, FontWeights.SemiBold, "Primary", true, false),
            Ui.VStack(6, title, intro),
            Ui.Scroll(stepper, true),
            stepHost,
            Ui.Divider(),
            footer), 32, "SurfaceLow", 28);
        card.MaxWidth = 960;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        var header = Ui.Header("Setup", "Setup only reads from Docker and saves your choices for this app. It never installs or changes system services.", out _);
        Layout(header, Ui.Scroll(card.Margin(0, 0, 8, 24)));
        PreviewKeyDown += OnKey;
        step = Math.Clamp(Host.Settings.SetupStep, 0, 3);
        Render();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.Right && step < 3) { Go(step + 1); e.Handled = true; }
        else if (key == System.Windows.Input.Key.Left && step > 0) { Go(step - 1); e.Handled = true; }
    }

    public override async Task RefreshAsync()
    {
        if (step == 0 || !steps.Detected) await steps.DetectAsync();
        if (step == 1 || !contextsLoaded) { contextsLoaded = true; await steps.LoadContextsAsync(); }
    }

    private void Go(int target)
    {
        target = Math.Clamp(target, 0, 3);
        if (target == step) return;
        step = target;
        if (Host.Settings.SetupStep != step) { Host.Settings.SetupStep = step; Host.SaveSettings(null); }
        Render();
        if (step == 1 && !contextsLoaded) { contextsLoaded = true; _ = steps.LoadContextsAsync(); }
        Dispatcher.BeginInvoke(() => stepHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Render()
    {
        title.Text = Loc.T(StepTitles[step]);
        intro.Text = Loc.T(StepIntros[step]);
        stepHost.Content = views[step];
        stepper.Children.Clear();
        detectedShown = steps.Detected;
        for (var i = 0; i < StepNames.Length; i++)
        {
            var done = i < step || (i == 0 && steps.Detected);
            var now = i == step;
            var dot = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14) };
            dot.SetResourceReference(Border.BackgroundProperty, done && !now ? "SuccessContainer" : now ? "Primary" : "SurfaceHighest");
            dot.Child = done && !now ? Ui.Icon(Icons.Check, 18, false, "OnSuccessContainer") : Ui.Text((i + 1).ToString(), 13, FontWeights.SemiBold, now ? "OnPrimary" : "OnSurfaceVariant", false, false);
            if (dot.Child is TextBlock t) t.HorizontalAlignment = HorizontalAlignment.Center;
            var label = Ui.Text(StepNames[i], 13, now ? FontWeights.SemiBold : FontWeights.Normal, i > step ? "OnSurfaceVariant" : "OnSurface", false);
            var content = Ui.HStack(8, dot, label);
            var index = i;
            var button = new Button { Style = (Style)Application.Current.FindResource("Btn.Text"), Content = content, Padding = new Thickness(8, 0, 12, 0), Margin = new Thickness(0, 0, 4, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Step {i + 1} of 4: {Loc.T(StepNames[i])}" + (now ? ", current" : done ? ", done" : ""));
            button.Click += (_, _) => Go(index);
            stepper.Children.Add(button);
            if (i < StepNames.Length - 1)
            {
                var bar = new Border { Width = 32, Height = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                bar.SetResourceReference(Border.BackgroundProperty, i < step ? "Primary" : "SurfaceHighest");
                stepper.Children.Add(bar);
            }
        }
        UpdateNav();
    }

    private void UpdateNav()
    {
        if (stepper.Children.Count > 0 && step == 0 && steps.Detected != detectedShown) { detectedShown = steps.Detected; Render(); return; }
        back.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        next.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
        finish.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        skip.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
        next.IsEnabled = step != 0 || steps.Detected;
        next.ToolTip = next.IsEnabled ? Loc.T("Alt+Right") : Loc.T("Docker was not detected yet. Run detection again, or use Skip for now.");
        back.ToolTip = Loc.T("Alt+Left");
        var parts = new List<string>();
        if (steps.DetectSummary != null) parts.Add(steps.DetectSummary);
        parts.Add(Loc.T("Context:") + " " + (Host.Context ?? "CLI default"));
        if (Host.Settings.BuildFolder.Length > 0) parts.Add(Loc.T("Folder:") + " " + Path.GetFileName(Path.TrimEndingDirectorySeparator(Host.Settings.BuildFolder)));
        summary.Text = string.Join(" · ", parts);
    }

    private void Finish()
    {
        Host.Settings.SetupComplete = true;
        Host.Settings.SetupStep = 3;
        Host.SaveSettings("Setup finished");
        Host.Notify.Success(Loc.Voice("Setup finished. You can run it again from Settings, Engine.", "All set!", "搞掂晒！"));
        Host.Navigate("Build");
    }

    public override IEnumerable<AppCommand> PageCommands() =>
    [
        new AppCommand { Id = "setup.open", Title = "Run setup again", Group = "Setup", Icon = Icons.RocketLaunch, Keywords = "wizard first run detect docker", Run = () => Host.Navigate(Key) },
    ];
}
