using System.Text;
using System.Windows;
using System.Windows.Threading;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Streams <c>docker compose events --json</c> into a searchable events table until stopped or closed.</summary>
public static class ComposeEventsViewer
{
    private const int Limit = 2000;

    public static void Show(string title, IReadOnlyList<string> args, string? context)
    {
        var host = AppHost.Current;
        var table = new DataTable<ComposeEventRow>("Compose events", "Search events");
        table.EmptyTitle = "Waiting for events";
        table.EmptyMessage = "Events appear when containers of this project are created, started, stopped, die or change health. Start or stop a service to see them.";
        table.Column("Time", e => e.Time, 110, true)
            .Column("Service", e => e.Service, 130, true)
            .ChipColumn("Action", e => e.Action, e => e.Action switch { "start" or "create" or "health_status: healthy" => Tone.Success, "die" or "kill" or "oom" or "health_status: unhealthy" => Tone.Error, "stop" or "pause" or "destroy" => Tone.Warning, _ => Tone.Neutral }, 140)
            .Column("Type", e => e.Type, 90)
            .Column("Container", e => e.Container, 120, true)
            .Column("Attributes", e => e.Attributes, 320, true);
        var events = new List<ComposeEventRow>();
        var pending = new StringBuilder();
        var cts = new CancellationTokenSource();
        var state = Ui.Caption("Connecting…");
        var dirty = false;
        void Drain()
        {
            var text = pending.ToString();
            var cut = text.LastIndexOf('\n');
            if (cut < 0) return;
            pending.Remove(0, cut + 1);
            foreach (var line in text[..cut].Split('\n'))
                if (ComposeEventRow.Parse(line) is { } row) { events.Add(row); dirty = true; }
            if (events.Count > Limit) events.RemoveRange(0, events.Count - Limit);
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => { Drain(); if (!dirty) return; dirty = false; table.SetItems(Enumerable.Reverse(events).ToList()); state.Text = $"{events.Count} event{(events.Count == 1 ? "" : "s")} received · newest first" + (events.Count >= Limit ? $" · oldest dropped beyond {Limit}" : ""); };
        timer.Start();
        Action? close = null;
        var stop = Ui.Button("Stop", Icons.Stop, ButtonKind.Tonal, () => cts.Cancel());
        var closeButton = Ui.Button("Close", null, ButtonKind.Filled, () => close?.Invoke());
        var body = Ui.Rows(12, [0, 1, 0], Ui.VStack(2, Ui.Title(title, 20), Ui.Caption("docker compose events --json · live")), table, Ui.Columns(8, [1, 0, 0], state, stop, closeButton));
        var window = Application.Current?.MainWindow?.ActualHeight ?? 0;
        body.Height = window <= 0 ? 600 : Math.Max(320, Math.Min(620, window * 0.86 - 72));
        close = host.Overlay.Show(body, 1000, () => cts.Cancel());
        body.Unloaded += (_, _) => { cts.Cancel(); timer.Stop(); };
        _ = Run();
        async Task Run()
        {
            state.Text = Loc.T("Listening for events.");
            try
            {
                var result = await host.Docker.ExecuteAsync(args, context, new Progress<DockerOutput>(o => { if (!o.IsError) pending.Append(o.Text); }), cts.Token);
                await Task.Delay(350);
                pending.Append('\n');
                if (result.Cancelled) state.Text = Loc.T("Stopped. The events above were received before stopping.");
                else if (!result.Success) state.Text = $"docker compose events failed with exit code {result.ExitCode}: " + (result.StandardError.Trim().Split('\n').FirstOrDefault() ?? "");
                else state.Text = Loc.T("The event stream ended.");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or IOException)
            {
                state.Text = "Could not start the event stream: " + e.Message;
            }
            finally { stop.IsEnabled = false; }
        }
    }
}
