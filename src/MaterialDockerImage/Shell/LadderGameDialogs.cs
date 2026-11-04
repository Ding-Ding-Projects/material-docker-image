using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Shell;

/// <summary>
/// The playable rungs of the unlock ladder: a dish question, ten sums and whack-a-mole. The dialogs only show questions and collect answers;
/// <see cref="UnlockLadder"/> makes and grades them. A win ends the wait and nothing more, and the credential is still asked for afterwards.
/// </summary>
public static class LadderGameDialogs
{
    private static AppHost Host => AppHost.Current;

    public const string Honesty = "Winning only ends the wait. You still need your PIN or code, and the next wrong answer still earns a longer wait.";

    /// <summary>Whether a game can be offered right now for a lock that is waiting, and why not when it cannot.</summary>
    public static LadderOffer Offer(string lockId)
    {
        var stamp = Host.Locks.LockoutStamp(lockId);
        return GameServices.Ladder.Offer(lockId, stamp, Host.Settings.SchoolMode);
    }

    public static string Describe(LadderOffer offer) => offer.Why switch
    {
        LadderUnavailable.BudgetSpent => "All the game wins for this hour are used. The clock is the way through until an hour has passed.",
        LadderUnavailable.FellThisLockout => "The games are over for this wait. The clock is the way through.",
        LadderUnavailable.NotWaiting => "There is no wait to shorten.",
        _ => Loc.T("Games left this hour:") + " " + offer.SkipsLeft + " / " + LadderRules.SkipsPerHour + "."
    };

    /// <summary>Plays the ladder from wherever this lockout got to. Returns true when the wait was ended.</summary>
    public static async Task<bool> PlayAsync(string lockId)
    {
        var store = Host.Locks; var ladder = GameServices.Ladder;
        string? note = null;
        for (var turn = 0; turn < 60; turn++)
        {
            var stamp = store.LockoutStamp(lockId);
            if (stamp == 0) return true;
            var school = Host.Settings.SchoolMode;
            var offer = ladder.Offer(lockId, stamp, school);
            if (!offer.Available) { Host.Notify.Info(Describe(offer)); return false; }
            var step = offer.Rung switch
            {
                LadderRung.DimSum => await DishRoundAsync(lockId, stamp, school, offer, note),
                LadderRung.Sums => await SumsRoundAsync(lockId, stamp, school, offer, note),
                LadderRung.Mole => await MoleRoundAsync(lockId, stamp, school, offer, note),
                _ => null
            };
            if (step == null) return false;
            note = null;
            if (step.Cleared)
            {
                var ended = store.ClearWaitByLadder(lockId);
                Host.Notify.Success(ended ? "The wait is over. You still need your PIN or code to unlock." : "The wait had already ended.");
                return ended;
            }
            switch (step.Kind)
            {
                case LadderStepKind.Rejected when step.Reject == LadderReject.BudgetSpent: Host.Notify.Warning(Describe(ladder.Offer(lockId, stamp, school))); return false;
                case LadderStepKind.Rejected: note = "That round could not be used. Here is a fresh one."; break;
                case LadderStepKind.Retry: note = "Not that one. Try another question."; break;
                default:
                    if (step.Rung == LadderRung.Clock) { Host.Notify.Info("That was the last game. The clock is the way through now."); return false; }
                    note = "That round is lost. Here is the next game."; break;
            }
        }
        return false;
    }

    // ---- Shared frame ----------------------------------------------------------------------------------------------------------

    private sealed class Frame
    {
        public required Action Close { get; set; }
        public required TaskCompletionSource<LadderStep?> Done { get; init; }
        public required ContentControl Holder { get; init; }
        public bool Closed { get; set; }
        public Action? OnAbandon { get; set; }
        public void Finish(LadderStep? step) { if (Closed) return; Closed = true; OnAbandon?.Invoke(); Done.TrySetResult(step); Close(); }
    }

    private static Frame Open(string title, LadderOffer offer, string? note, double width = 560, Func<FrameworkElement, KeyEventArgs, bool>? keys = null)
    {
        var done = new TaskCompletionSource<LadderStep?>();
        var holder = new ContentControl();
        Frame? frame = null;
        var noteText = note == null ? null : Ui.Text(note, 13, FontWeights.SemiBold, "Primary");
        if (noteText != null) { AutomationProperties.SetLiveSetting(noteText, AutomationLiveSetting.Polite); }
        var body = Ui.VStack(12, Ui.Title(title, 20), noteText, holder,
            Ui.Caption(Describe(offer)), Ui.Caption(Honesty));
        body.PreviewKeyDown += (_, e) => { if (keys != null && keys(body, e)) e.Handled = true; };
        var close = Host.Overlay.Show(body, width, () => { if (frame is { Closed: false }) { frame.Closed = true; frame.OnAbandon?.Invoke(); done.TrySetResult(null); } });
        frame = new Frame { Close = close, Done = done, Holder = holder };
        return frame;
    }

    private static string Label(IReadOnlyDictionary<string, DishName> names, string english)
    {
        if (!names.TryGetValue(english, out var dish)) return english;
        return Loc.Language switch { "Cantonese" => dish.Cantonese, "Bilingual" => dish.English + " · " + dish.Cantonese, _ => dish.English };
    }

    // ---- Rung 1: one dish, four choices ---------------------------------------------------------------------------------------

    private static async Task<LadderStep?> DishRoundAsync(string lockId, long stamp, bool school, LadderOffer offer, string? note)
    {
        var ladder = GameServices.Ladder; var source = GameServices.DimSum;
        var frame = Open("Name the dish", offer, note);
        var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => frame.Finish(null));
        frame.Holder.Content = Ui.VStack(10, Ui.Caption("Looking for a dish picture…"), cancel);
        _ = Prepare();
        return await frame.Done.Task;

        async Task Prepare()
        {
            try
            {
                DishChallenge? q = null; string? picture = null; IReadOnlyList<DishName> pool = DimSumRules.OfflineNames;
                var dishes = await source.LoadAsync();
                if (frame.Closed) return;
                if (dishes.Count >= LadderRules.DishChoices)
                {
                    pool = DimSumRules.AsNames(dishes);
                    q = ladder.NextDish(lockId, stamp, school, pool, true);
                    var target = q == null ? null : ladder.PictureFor(q.Nonce);
                    var dish = target == null ? null : dishes.FirstOrDefault(d => d.Id == target.Id);
                    picture = dish == null ? null : await source.PictureAsync(dish);
                    if (picture == null) { q = null; pool = DimSumRules.OfflineNames; }
                }
                if (frame.Closed) return;
                q ??= ladder.NextDish(lockId, stamp, school, pool, false);
                if (q == null) { frame.Finish(null); return; }
                Show(q, picture, pool);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException) { frame.Finish(null); }
        }

        void Show(DishChallenge q, string? picture, IReadOnlyList<DishName> pool)
        {
            var names = pool.GroupBy(p => p.English).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var rows = Ui.VStack(10);
            if (picture != null)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 360; bitmap.UriSource = new Uri(picture); bitmap.EndInit(); bitmap.Freeze();
                var image = new System.Windows.Controls.Image { Source = bitmap, Width = 280, Height = 280, Stretch = System.Windows.Media.Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(image, Loc.T("A picture of a dim sum dish. Pick its name from the four choices."));
                rows.Children.Add(image);
                rows.Children.Add(Ui.Caption("Which dish is this?"));
            }
            else
            {
                rows.Children.Add(Ui.Caption("No picture is available right now, so this one asks for a name instead."));
                rows.Children.Add(Ui.Text(q.Clue, 22, FontWeights.SemiBold, "OnSurface", true, false));
                rows.Children.Add(Ui.Caption("Which English name matches?"));
            }
            for (var i = 0; i < q.Choices.Count; i++)
            {
                var index = i;
                var button = Ui.Button((i + 1) + ".  " + Label(names, q.Choices[i]), null, ButtonKind.Tonal, () => frame.Finish(ladder.AnswerDish(lockId, stamp, school, q.Nonce, index)), false);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                AutomationProperties.SetName(button, Loc.T("Choice") + " " + (i + 1) + ": " + Label(names, q.Choices[i]));
                rows.Children.Add(button);
            }
            rows.Children.Add(cancel);
            rows.PreviewKeyDown += (_, e) =>
            {
                var n = DigitOf(e.Key);
                if (n is >= 1 and <= 4 && rows.Children.OfType<Button>().Skip(n - 1).FirstOrDefault() is { } pick) { pick.Focus(); pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            };
            frame.Holder.Content = rows;
            (rows.Children.OfType<Button>().FirstOrDefault())?.Focus();
        }
    }

    private static int DigitOf(Key key) => key >= Key.D0 && key <= Key.D9 ? key - Key.D0 : key >= Key.NumPad0 && key <= Key.NumPad9 ? key - Key.NumPad0 : -1;

    // ---- Rung 2: ten easy sums ------------------------------------------------------------------------------------------------

    private static Task<LadderStep?> SumsRoundAsync(string lockId, long stamp, bool school, LadderOffer offer, string? note)
    {
        var ladder = GameServices.Ladder;
        var q = ladder.NextSums(lockId, stamp, school);
        if (q == null) return Task.FromResult<LadderStep?>(null);
        var frame = Open("Ten sums", offer, note, 600);
        var boxes = new List<TextBox>();
        var error = Ui.Text("", 13, null, "Error");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < q.Problems.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var box = Ui.Input("", null, true); box.Width = 90; box.MaxLength = 6;
            AutomationProperties.SetName(box, Loc.T("Answer to sum") + " " + (i + 1) + ": " + q.Problems[i]);
            boxes.Add(box);
            var row = Ui.HStack(8, Ui.Text((i + 1) + ".", 14, null, "OnSurfaceVariant", false, false), Ui.Text(q.Problems[i] + " =", 18, FontWeights.SemiBold, "OnSurface", false, false), box);
            row.Margin = new Thickness(0, 4, 12, 4);
            Grid.SetRow(row, i / 2); Grid.SetColumn(row, i % 2);
            grid.Children.Add(row);
        }
        void Submit()
        {
            var answers = new List<int>();
            foreach (var box in boxes)
            {
                if (!int.TryParse(box.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) { error.Text = Loc.T("Fill in all ten sums with whole numbers."); box.Focus(); return; }
                answers.Add(value);
            }
            frame.Finish(ladder.AnswerSums(lockId, stamp, school, q.Nonce, answers));
        }
        boxes[^1].KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        frame.Holder.Content = Ui.VStack(10,
            Ui.Caption("Every sum has to be right. One slip moves you on to the next game."), grid, error,
            Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => frame.Finish(null)), Ui.Button("Check my answers", Icons.Extension, ButtonKind.Filled, Submit)));
        boxes[0].Loaded += (_, _) => boxes[0].Focus();
        return frame.Done.Task;
    }

    // ---- Rung 3: whack-a-mole -------------------------------------------------------------------------------------------------

    private static Task<LadderStep?> MoleRoundAsync(string lockId, long stamp, bool school, LadderOffer offer, string? note)
    {
        var ladder = GameServices.Ladder;
        var q = ladder.NextMole(lockId, stamp, school);
        if (q == null) return Task.FromResult<LadderStep?>(null);
        var frame = Open("Whack-a-mole", offer, note, 520);
        var hits = new List<MoleHit>();
        var cells = new Button[q.Cells];
        var cellText = new TextBlock[q.Cells];
        var board = new UniformGrid { Rows = 3, Columns = 3 };
        var status = Ui.Text("", 15, FontWeights.SemiBold);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var running = false; var sw = new Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        frame.OnAbandon = () => timer.Stop();
        var up = -1;

        void Paint(long t)
        {
            var mole = q.Moles.FirstOrDefault(m => t >= m.AppearMs && t < m.AppearMs + m.VisibleMs);
            var now = mole?.Cell ?? -1;
            if (now != up)
            {
                if (up >= 0) SetCell(up, false);
                if (now >= 0) SetCell(now, true);
                up = now;
            }
            var left = Math.Max(0, (int)Math.Ceiling((q.DurationMs - t) / 1000.0));
            var counted = UnlockLadder.CountHits(q.Moles, hits);
            var text = Loc.T("Hits") + " " + counted + " / " + q.Needed + " · " + left + " " + Loc.T("seconds left");
            if (status.Text != text) status.Text = text;
        }
        void SetCell(int index, bool isUp)
        {
            cellText[index].Text = (index + 1) + (isUp ? "\n" + Loc.T("MOLE") : "");
            AutomationProperties.SetName(cells[index], Loc.T("Cell") + " " + (index + 1) + (isUp ? ", " + Loc.T("mole is up") : ""));
            cells[index].Style = (Style)Application.Current.FindResource(isUp ? "Btn.Filled" : "Btn.Tonal");
        }
        void Hit(int index) { if (running) hits.Add(new MoleHit(index, (int)sw.ElapsedMilliseconds)); }
        void End()
        {
            timer.Stop(); running = false;
            frame.Finish(ladder.AnswerMole(lockId, stamp, school, q.Nonce, hits.ToList()));
        }
        timer.Tick += (_, _) =>
        {
            var t = sw.ElapsedMilliseconds;
            if (t >= q.DurationMs) { End(); return; }
            Paint(t);
        };
        for (var i = 0; i < q.Cells; i++)
        {
            var index = i;
            cellText[i] = new TextBlock { Text = (i + 1).ToString(), TextAlignment = TextAlignment.Center, FontSize = 16, FontWeight = FontWeights.SemiBold };
            cells[i] = new Button { Content = cellText[i], MinWidth = 96, MinHeight = 76, Margin = new Thickness(4), IsEnabled = false, Style = (Style)Application.Current.FindResource("Btn.Tonal") };
            AutomationProperties.SetName(cells[i], Loc.T("Cell") + " " + (i + 1));
            cells[i].Click += (_, _) => Hit(index);
            board.Children.Add(cells[i]);
        }
        var start = Ui.Button("Start the round", Icons.Timer, ButtonKind.Filled);
        start.Click += (_, _) =>
        {
            start.Visibility = Visibility.Collapsed; foreach (var c in cells) c.IsEnabled = true;
            running = true; sw.Restart(); timer.Start(); Paint(0); cells[4].Focus();
        };
        board.PreviewKeyDown += (_, e) => { var n = DigitOf(e.Key); if (running && n is >= 1 and <= 9) { Hit(n - 1); e.Handled = true; } };
        frame.Holder.Content = Ui.VStack(10,
            Ui.Caption(LadderRules.MoleIntro),
            status, board, Ui.HStack(8, Ui.Button("Give up", null, ButtonKind.Text, () => frame.Finish(null)), start));
        start.Loaded += (_, _) => start.Focus();
        return frame.Done.Task;
    }
}
