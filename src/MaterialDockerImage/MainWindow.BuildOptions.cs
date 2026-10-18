using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage;
public partial class MainWindow
{
    private readonly ComboBox buildDestination=new(){ItemsSource=new[]{"Load into local engine","Push to registry","Export OCI archive"},SelectedIndex=0};
    private readonly TextBox buildLabels=new(){AcceptsReturn=true,Height=90};
    private readonly TextBox cacheFrom=new(){AcceptsReturn=true,Height=75},cacheTo=new(){AcceptsReturn=true,Height=75},ociPath=new();
    private readonly List<Func<(string Id,string Path)>> buildSecrets=new();
    private void ConfigureBuildExtras(Panel parent)
    {
        var outputs=Expert(parent,"Output, additional tags and labels");
        outputs.Children.Add(new TextBlock{Text="Image tag accepts one or more tags separated by commas or lines. Registry publication uses the existing Docker authentication context and always requires confirmation."});
        Field(outputs,"Build destination",buildDestination);Field(outputs,"OCI archive path",ociPath);
        var browse=Button("Choose OCI archive",()=>{var dialog=new SaveFileDialog{Filter="OCI tar archive|*.tar",FileName="image.oci.tar",OverwritePrompt=true};if(dialog.ShowDialog(this)==true)ociPath.Text=dialog.FileName;});outputs.Children.Add(browse);
        void DestinationChanged(){var enabled=buildDestination.SelectedIndex==2;ociPath.IsEnabled=enabled;browse.IsEnabled=enabled;}
        buildDestination.SelectionChanged+=(_,_)=>DestinationChanged();DestinationChanged();Field(outputs,"Image labels (one key=value per line)",buildLabels);
        var cache=Expert(parent,"Build cache import and export");
        cache.Children.Add(new TextBlock{Text="One BuildKit cache specification per line, for example type=local,src=C:/cache or type=registry,ref=registry.example/team/cache. Cache backends depend on the installed builder. Do not enter access keys or credentials. Cache export is a write operation and requires confirmation."});
        Field(cache,"Cache from",cacheFrom);Field(cache,"Cache to",cacheTo);
        var secrets=Expert(parent,"BuildKit secret files");
        secrets.Children.Add(new TextBlock{Text="Choose an existing local secret file and the identifier used by RUN --mount=type=secret. Only its path is passed to BuildKit; this application does not read or log its bytes or persist the selection. The Dockerfile remains responsible for not printing or copying secret content."});
        var rows=new StackPanel();secrets.Children.Add(rows);
        secrets.Children.Add(Button("Add secret file",()=>
        {
            var picker=new OpenFileDialog{Title="Choose a BuildKit secret source file"};if(picker.ShowDialog(this)!=true)return;
            var row=new StackPanel{Margin=new Thickness(0,8,0,8)};var id=new TextBox();var path=new TextBox{Text=picker.FileName,IsReadOnly=true};Field(row,"Secret ID",id);Field(row,"Selected local file",path);
            Func<(string Id,string Path)> value=()=> (id.Text.Trim(),path.Text);buildSecrets.Add(value);
            row.Children.Add(Button("Replace secret file",()=>{var replacement=new OpenFileDialog{Title="Replace the BuildKit secret source"};if(replacement.ShowDialog(this)==true)path.Text=replacement.FileName;}));
            row.Children.Add(Button("Remove secret",()=>{buildSecrets.Remove(value);rows.Children.Remove(row);}));rows.Children.Add(row);
        }));
        parent.Children.Add(AsyncButton("Check Dockerfile with BuildKit",RunBuildCheck));
    }
    private IReadOnlyList<string>? AdvancedBuildArguments()
    {
        var tags=tag.Text.Split([',','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var requestedPlatforms=platforms.Text.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(buildDestination.SelectedIndex==0&&requestedPlatforms.Length>1)throw new ArgumentException("Local load supports one platform in this editor. Choose registry push or OCI export for a multi-platform build.");
        string? export=null;
        if(buildDestination.SelectedIndex==2)
        {
            var path=Path.GetFullPath(ociPath.Text);if(!Directory.Exists(Path.GetDirectoryName(path)))throw new ArgumentException("Choose an OCI archive in an existing output directory.");
            export=StructuredInputs.Export(path);
            if(File.Exists(path)&&!Confirm("Replace existing OCI archive",path))return null;
        }
        if(buildDestination.SelectedIndex==1&&tags.Length==0)throw new ArgumentException("Registry push requires at least one explicit destination tag.");
        var options=new BuildOptions { ContextPath=folder.Text,Dockerfile=dockerfilePath,Tags=tags,Platforms=requestedPlatforms.Length==0?Array.Empty<string>():new[]{string.Join(',',requestedPlatforms)},Target=Optional(target),BuildArguments=StructuredInputs.Pairs(buildArgs.Text),Labels=StructuredInputs.Pairs(buildLabels.Text,true),NoCache=noCache.IsChecked==true,Pull=pullBase.IsChecked==true,Load=buildDestination.SelectedIndex==0,Push=buildDestination.SelectedIndex==1,Output=export };
        var args=DockerService.CreateBuildArguments(options).ToList();var context=args[^1];args.RemoveAt(args.Count-1);
        foreach(var spec in StructuredInputs.Lines(cacheFrom.Text))args.AddRange(["--cache-from",StructuredInputs.Cache(spec)]);
        var exportCaches=StructuredInputs.Lines(cacheTo.Text);foreach(var spec in exportCaches)args.AddRange(["--cache-to",StructuredInputs.Cache(spec)]);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var get in buildSecrets)
        {
            var secret=get();if(!ids.Add(secret.Id))throw new ArgumentException("BuildKit secret identifiers must be unique.");
            if(!File.Exists(secret.Path))throw new ArgumentException("A selected BuildKit secret file is unavailable. Replace it or remove its row.");
            args.AddRange(["--secret",StructuredInputs.Secret(secret.Id,secret.Path)]);
        }
        if(buildDestination.SelectedIndex==1&&!Confirm("Publish image tags to their registries",string.Join(Environment.NewLine,tags)))return null;
        if(exportCaches.Length>0&&!Confirm("Export build cache to these destinations",string.Join(Environment.NewLine,exportCaches)))return null;
        args.Add(context);return args;
    }
    private async Task RunBuildCheck()
    {
        if(dirtyDockerfile)throw new InvalidOperationException("Save the Dockerfile before checking the exact file.");
        if(!File.Exists(dockerfilePath)||!Directory.Exists(folder.Text))throw new ArgumentException("Load a Dockerfile and choose an existing context folder first.");
        await Execute("BuildKit Dockerfile check",["buildx","build","--check","--file",dockerfilePath,Path.GetFullPath(folder.Text)]);
    }
}
