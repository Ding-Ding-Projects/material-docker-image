using System.Text.Json;
using System.Windows;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Configs: list, create from a file, inspect with the decoded content, and remove.</summary>
public sealed class SwarmConfigsView : SwarmTabView
{
    private readonly DataTable<SwarmObjectRow> table = new("Swarm configs", "Search configs");

    public SwarmConfigsView(Func<string?> context, Func<bool> isCurrent) : base(context, isCurrent)
    {
        table.Key = c => c.Id;
        table.EmptyTitle = Loc.T("No configs");
        table.EmptyMessage = Loc.T("This swarm stores no configs. Create one from a file to mount non-secret configuration into services.");
        table.Column("Name", c => c.Name, 260, true)
            .Column("Created", c => c.CreatedAt, 200)
            .Column("Updated", c => c.UpdatedAt, 200)
            .Column("ID", c => c.Id, 220, true)
            .RowAction("Show content", Icons.Description, ShowContentAsync, "Enter")
            .RowAction("Inspect", Icons.DataObject, c => InspectAsync("config", c.Id, Loc.T("Config") + " " + c.Name))
            .RowAction("Copy name", Icons.ContentCopy, c => { PageKit.Copy(c.Name, "the config name"); return Task.CompletedTask; })
            .RowAction("Remove…", Icons.Delete, RemoveAsync, "Del", true);
        table.Activated += c => _ = Guard(() => ShowContentAsync(c));
        var create = Ui.AsyncButton("Create config…", Icons.Add, ButtonKind.Filled, CreateAsync);
        var note = Ui.Caption("Configs are not encrypted and their content can be read by anyone with manager access. Use secrets for passwords and keys.");
        note.Margin = new Thickness(12, 0, 0, 0);
        LayoutWith(Ui.Wrap(8, create, note), table);
    }

    public override async Task LoadAsync()
    {
        var rows = await Host.ReadRows(["config", "ls", "--format", "{{json .}}"], Context());
        var items = rows.Select(r => new SwarmObjectRow(CliValues.Str(r, "ID"), CliValues.Str(r, "Name"), "", CliValues.Str(r, "CreatedAt"), CliValues.Str(r, "UpdatedAt"))).ToList();
        table.SetItems(items);
        SetSummary(Loc.Line($"{items.Count} config{(items.Count == 1 ? "" : "s")}"));
    }

    private async Task ShowContentAsync(SwarmObjectRow c)
    {
        var r = await Host.Read(["config", "inspect", c.Id], Context());
        if (!r.Success) { Host.Notify.Error($"Inspect {c.Name} failed: {r.StandardError.Trim()}"); return; }
        string? data;
        try { data = SwarmRules.ConfigDataFromInspect(r.StandardOutput); }
        catch (JsonException e) { Host.Notify.Error(Loc.T("The config could not be parsed:") + " " + e.Message); return; }
        ResourceTextViewer.Show("Config " + c.Name, data == null ? "(The engine returned no data for this config.)" : SwarmRules.DecodeConfigData(data), "Content decoded from base64 (Spec.Data of docker config inspect)");
    }

    private Task CreateAsync()
    {
        if (Stale()) return Task.CompletedTask;
        var name = Ui.Input("", "Name", true);
        var file = Ui.Input("", "File", true);
        var browse = Ui.IconButton(Icons.FolderOpen, "Choose the config file", () => { if (PageKit.PickOpenPath("Choose the config file", "All files|*.*") is { } p) { file.Text = p; if (name.Text.Length == 0) name.Text = PageKit.SafeFileName(Path.GetFileName(p)); } });
        var body = Ui.VStack(14,
            Ui.Field("File", Ui.Columns(8, [1, 0], file, browse), "The docker CLI reads this file and stores its content in the swarm. Maximum 500 KB.", "C:\\src\\shop\\nginx.conf"),
            Ui.Field("Name", name, "Services refer to the config by this name. Configs cannot be changed later: create a new one and update the services.", "nginx_conf_v2"));
        PageKit.Dialog("Create config", "docker config create", body, "Create", async () =>
        {
            var n = name.Text.Trim();
            if (SwarmRules.ValidateName(n, "config") is { } problem) return problem;
            var path = file.Text.Trim();
            if (!File.Exists(path)) return "Choose an existing file.";
            if (Stale()) return PageKit.StaleMessage(Context());
            var result = await Run(Loc.T("Create config") + " " + n, ["config", "create", n, Path.GetFullPath(path)]);
            if (result?.Success != true) return PageKit.Failure(result);
            await AfterChange();
            return null;
        });
        return Task.CompletedTask;
    }

    private async Task RemoveAsync(SwarmObjectRow c)
    {
        if (Stale()) return;
        if (!await Host.Overlay.ConfirmAsync(Loc.T("Remove config") + " " + c.Name, "Deletes the config from the swarm. The engine refuses while a service still uses it.", "Remove", true)) return;
        if (Stale()) return;
        await Run(Loc.T("Remove config") + " " + c.Name, ["config", "rm", c.Id]);
        await AfterChange();
    }
}
