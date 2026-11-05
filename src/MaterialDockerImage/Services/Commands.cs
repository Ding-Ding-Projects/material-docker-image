using System.Windows;
using System.Windows.Input;

namespace MaterialDockerImage.Services;

/// <summary>A command the palette lists. Gesture is the single source for the binding and for every displayed shortcut.</summary>
public sealed class AppCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Group { get; init; } = "Commands";
    public string? Icon { get; init; }
    public string Keywords { get; init; } = "";
    /// <summary>One-line English description shown under the title in the palette and searched with it. Filled from
    /// <see cref="UI.CommandDescriptions"/> at registration when the command does not set its own.</summary>
    public string? Description { get; set; }
    public KeyGesture? Gesture { get; init; }
    public Func<bool>? CanRun { get; init; }
    public Action? Run { get; init; }
    /// <summary>Optional live control (switch, segmented) rendered in the palette row, wired to the same setting.</summary>
    public Func<FrameworkElement>? LiveControl { get; init; }
    public string GestureText => Gesture?.GetDisplayStringForCulture(System.Globalization.CultureInfo.CurrentCulture) ?? "";
}

public sealed class CommandRegistry
{
    private readonly Dictionary<string, AppCommand> commands = new(StringComparer.Ordinal);
    public IReadOnlyCollection<AppCommand> All => commands.Values;
    public void Register(AppCommand command)
    {
        command.Description ??= UI.CommandDescriptions.For(command.Id);
        commands[command.Id] = command;
    }
    public AppCommand? Find(string id) => commands.TryGetValue(id, out var c) ? c : null;
    public string GestureText(string id) => Find(id)?.GestureText ?? "";
    public void Bind(UIElement target)
    {
        foreach (var command in commands.Values.Where(c => c.Gesture != null && c.Run != null))
        {
            var routed = new RoutedCommand(command.Id, typeof(CommandRegistry));
            target.CommandBindings.Add(new CommandBinding(routed, (_, _) => { if (command.CanRun?.Invoke() != false) UI.Ui.Guard(command.Run!); }));
            target.InputBindings.Add(new KeyBinding(routed, command.Gesture!));
        }
    }
}
