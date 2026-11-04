using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.UI;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>One row of <c>docker secret ls</c> or <c>docker config ls</c>.</summary>
public sealed record SwarmObjectRow(string Id, string Name, string Driver, string CreatedAt, string UpdatedAt);

/// <summary>
/// Secrets: list, create from a typed value (sent through standard input) or from a file (read by the docker CLI),
/// inspect metadata and remove. Secret contents are never read, shown, logged or recorded by this application.
/// </summary>
public sealed class SwarmSecretsView : SwarmTabView
{
    private readonly DataTable<SwarmObjectRow> table = new("Swarm secrets", "Search secrets");

    public SwarmSecretsView(Func<string?> context, Func<bool> isCurrent) : base(context, isCurrent)
    {
        table.Key = s => s.Id;
        table.EmptyTitle = Loc.T("No secrets");
        table.EmptyMessage = Loc.T("This swarm stores no secrets. Create one to give services passwords or keys without putting them in images or environment variables.");
        table.Column("Name", s => s.Name, 260, true)
            .Column("Driver", s => s.Driver.Length > 0 ? s.Driver : "internal", 100)
            .Column("Created", s => s.CreatedAt, 200)
            .Column("Updated", s => s.UpdatedAt, 200)
            .Column("ID", s => s.Id, 220, true)
            .RowAction("Inspect metadata", Icons.DataObject, s => InspectAsync("secret", s.Id, Loc.T("Secret") + " " + s.Name), "Enter")
            .RowAction("Copy name", Icons.ContentCopy, s => { PageKit.Copy(s.Name, "the secret name"); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true);
        table.Activated += s => _ = Guard(() => InspectAsync("secret", s.Id, Loc.T("Secret") + " " + s.Name));
        var create = Ui.AsyncButton("Create secret…", Icons.VpnKey, ButtonKind.Filled, CreateAsync);
        var note = Ui.Caption("The engine never returns secret contents; inspect shows metadata only. Services read secrets as files under /run/secrets.");
        note.Margin = new Thickness(12, 0, 0, 0);
        LayoutWith(Ui.Wrap(8, create, note), table);
    }

    public override async Task LoadAsync()
    {
        var rows = await Host.ReadRows(["secret", "ls", "--format", "{{json .}}"], Context());
        var items = rows.Select(r => new SwarmObjectRow(CliValues.Str(r, "ID"), CliValues.Str(r, "Name"), CliValues.Str(r, "Driver"), CliValues.Str(r, "CreatedAt"), CliValues.Str(r, "UpdatedAt"))).ToList();
        table.SetItems(items);
        SetSummary(Loc.Line($"{items.Count} secret{(items.Count == 1 ? "" : "s")}"));
    }

    private Task CreateAsync()
    {
        if (Stale()) return Task.CompletedTask;
        var name = Ui.Input("", "Name", true);
        var fromFile = false;
        var value = new PasswordBox();
        var file = Ui.Input("", "File", true);
        var browse = Ui.IconButton(Icons.FolderOpen, "Choose the secret file", () => { if (PageKit.PickOpenPath("Choose the secret file", "All files|*.*") is { } p) file.Text = p; });
        var valueField = Ui.Field("Value", value, "Typed here and sent to docker through standard input. It is not shown, logged or stored by this application.");
        var fileField = Ui.Field("File", Ui.Columns(8, [1, 0], file, browse), "The docker CLI reads the file and sends its contents to the swarm. This application does not open it. Maximum 500 KB.", "C:\\secrets\\db-password.txt");
        fileField.Visibility = Visibility.Collapsed;
        var source = Ui.Segmented(["Typed value", "From file"], "Typed value", v => { fromFile = v == "From file"; valueField.Visibility = fromFile ? Visibility.Collapsed : Visibility.Visible; fileField.Visibility = fromFile ? Visibility.Visible : Visibility.Collapsed; });
        source.Width = 300; source.HorizontalAlignment = HorizontalAlignment.Left;
        var body = Ui.VStack(14,
            Ui.Field("Name", name, "Services refer to the secret by this name. Secrets cannot be changed later: create a new one and update the services.", "db_password"),
            Ui.Field("Source", source), valueField, fileField);
        PageKit.Dialog("Create secret", "docker secret create", body, "Create", async () =>
        {
            var n = name.Text.Trim();
            if (SwarmRules.ValidateName(n, "secret") is { } problem) return problem;
            if (Stale()) return PageKit.StaleMessage(Context());
            Core.CommandResult? result;
            if (fromFile)
            {
                var path = file.Text.Trim();
                if (!File.Exists(path)) return "Choose an existing file.";
                result = await Run(Loc.T("Create secret") + " " + n, ["secret", "create", n, Path.GetFullPath(path)]);
            }
            else
            {
                if (value.Password.Length == 0) return "Type the secret value.";
                result = await Run(Loc.T("Create secret") + " " + n, ["secret", "create", n, "-"], value.Password);
                value.Clear();
            }
            if (result?.Success != true) return PageKit.Failure(result);
            await AfterChange();
            return null;
        });
        return Task.CompletedTask;
    }

    private async Task RemoveAsync(SwarmObjectRow s)
    {
        if (Stale()) return;
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Remove secret") + " " + s.Name, "Deletes the secret from the swarm. The engine refuses while a service still uses it. The value cannot be recovered afterwards.", "Remove", true)) return;
        if (Stale()) return;
        await Run(Loc.T("Remove secret") + " " + s.Name, ["secret", "rm", s.Id]);
        await AfterChange();
    }
}
