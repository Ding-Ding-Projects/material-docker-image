using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// BuildKit secret rows: files chosen with a picker (only the path is passed; contents are never read) and
/// environment variables passed by name (values are never read, shown or stored).
/// </summary>
public sealed class BuildSecretFiles : StackPanel
{
    private sealed record FileRow(TextBox Id, TextBox Path, FrameworkElement Element);
    private readonly StackPanel fileRows = new();
    private readonly List<FileRow> files = new();
    private readonly List<NameValue> envItems = new();
    private readonly BuildKeyValueRows envRows;
    private readonly Action changed;

    public BuildSecretFiles(Action changed)
    {
        this.changed = changed;
        var addFile = Ui.Button("Add secret file", Icons.Key, ButtonKind.Text, AddFile);
        addFile.HorizontalAlignment = HorizontalAlignment.Left;
        envRows = new BuildKeyValueRows(envItems, "Secret ID", "Environment variable", "Add environment secret", id => BuildKitCommand.ValidateSecretId(id), changed);
        Children.Add(Ui.VStack(14,
            Ui.Field("Secret files", Ui.VStack(6, fileRows, addFile),
                "Choose an existing local file and the id used by RUN --mount=type=secret. Only the path is passed to BuildKit; this application never reads, logs or saves the file contents or the selection.",
                "Id: npmrc  ·  File: C:\\Users\\me\\.npmrc\nRUN --mount=type=secret,id=npmrc,target=/root/.npmrc npm ci"),
            Ui.Field("Secrets from environment variables", envRows,
                "Passes the value of an environment variable of this application to BuildKit (--secret id=ID,env=VARIABLE). Only the variable name is used here; its value is never read, shown or saved. The variable must be set before the application starts.",
                "GITHUB_TOKEN = GITHUB_TOKEN\nRUN --mount=type=secret,id=GITHUB_TOKEN,env=GITHUB_TOKEN ./fetch.sh")));
    }

    public IReadOnlyList<KeyValuePair<string, string>> Files => files.Select(f => new KeyValuePair<string, string>(f.Id.Text.Trim(), f.Path.Text)).ToList();
    public IReadOnlyList<KeyValuePair<string, string>> EnvironmentSecrets => envRows.Pairs();

    private void AddFile()
    {
        var picker = new OpenFileDialog { Title = Loc.T("Choose a BuildKit secret source file") };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        var id = Ui.Input(SuggestId(picker.FileName), "Secret ID", true);
        var path = Ui.Input(picker.FileName, "Selected local file", true);
        path.IsReadOnly = true;
        FileRow? row = null;
        var replace = Ui.IconButton(Icons.FolderOpen, "Replace secret file", () =>
        {
            var replacement = new OpenFileDialog { Title = Loc.T("Replace the BuildKit secret source") };
            if (replacement.ShowDialog(Window.GetWindow(this)) == true) path.Text = replacement.FileName;
        });
        var remove = Ui.IconButton(Icons.Delete, "Remove secret", () => { files.Remove(row!); fileRows.Children.Remove(row!.Element); changed(); });
        var element = Ui.Card(Ui.VStack(8, Ui.Columns(8, [1, 0, 0], Ui.Field("Secret ID", id), replace, remove), Ui.Field("Selected local file", path)), 12, "SurfaceContainer", 12);
        element.Margin = new Thickness(0, 0, 0, 8);
        replace.VerticalAlignment = remove.VerticalAlignment = VerticalAlignment.Bottom;
        id.TextChanged += (_, _) => changed();
        path.TextChanged += (_, _) => changed();
        row = new FileRow(id, path, element);
        files.Add(row);
        fileRows.Children.Add(element);
        changed();
    }

    private static string SuggestId(string path)
    {
        var name = Regex.Replace(Path.GetFileName(path).TrimStart('.'), "[^A-Za-z0-9_.-]", "_", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') ? name : "secret";
    }
}
