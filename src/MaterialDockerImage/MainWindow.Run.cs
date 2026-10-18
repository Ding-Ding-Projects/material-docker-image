using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage;
public partial class MainWindow
{
    private StackPanel Expert(Panel owner,string title)
    {
        var content=new StackPanel { Margin=new Thickness(8) };
        owner.Children.Add(new Expander { Header=title,Content=content,Foreground=(Brush)FindResource("Ink"),Margin=new Thickness(0,8,0,8) });return content;
    }
    private static string? Optional(TextBox input) => string.IsNullOrWhiteSpace(input.Text)?null:input.Text.Trim();
    private void RunDialog(string image,string? snapshotContext,long snapshotGeneration)
    {
        var p=Page("Run container","Create a detached container from "+image+". Port bindings default to loopback. Paths refer to the selected engine host, which may be remote.");
        var name=new TextBox();var port=new TextBox();var network=new TextBox();var mount=new TextBox();var readOnly=new CheckBox { Content="Read-only root filesystem" };
        Field(p,"Container name",name);Field(p,"Published port (host:container)",port);Field(p,"Network (optional)",network);Field(p,"Mount (source:destination:ro)",mount);p.Children.Add(readOnly);
        var portRows=new List<Func<string>>();var mountRows=new List<Func<string>>();
        var ports=Expert(p,"Additional port mappings");
        ports.Children.Add(new TextBlock { Text="Each mapping has its own bind address. 0.0.0.0 or :: publishes to all host interfaces; only choose this intentionally." });
        var portList=new StackPanel();ports.Children.Add(portList);
        ports.Children.Add(Button("Add port",()=>
        {
            var row=new StackPanel { Margin=new Thickness(0,8,0,8) };var address=new TextBox{Text="127.0.0.1"};var host=new TextBox();var container=new TextBox();var protocol=new ComboBox{ItemsSource=new[]{"tcp","udp"},SelectedIndex=0};
            Field(row,"Bind address",address);Field(row,"Host port",host);Field(row,"Container port",container);Field(row,"Protocol",protocol);
            Func<string> value=()=>StructuredInputs.Port(address.Text.Trim(),host.Text.Trim(),container.Text.Trim(),protocol.SelectedItem?.ToString()??"tcp");
            portRows.Add(value);row.Children.Add(Button("Remove port row",()=>{portRows.Remove(value);portList.Children.Remove(row);}));portList.Children.Add(row);
        }));
        var mounts=Expert(p,"Additional bind paths and named volumes");
        mounts.Children.Add(new TextBlock { Text="Bind sources are paths on the engine host, not necessarily this computer. Browse selects a local folder only; confirm the engine can access it. Write access is off by default. Remote paths are not checked against local storage." });
        var mountList=new StackPanel();mounts.Children.Add(mountList);
        mounts.Children.Add(Button("Add mount",()=>
        {
            var row=new StackPanel { Margin=new Thickness(0,8,0,8) };var type=new ComboBox{ItemsSource=new[]{"bind","volume"},SelectedIndex=0};var source=new TextBox();var destination=new TextBox();var ro=new CheckBox{Content="Read-only mount",IsChecked=true};
            Field(row,"Mount type",type);Field(row,"Engine-host source path or volume name",source);Field(row,"Container destination",destination);row.Children.Add(ro);
            var browse=Button("Browse local source folder",()=>{var picker=new OpenFolderDialog{Title="Choose a local bind source; verify it is accessible to this engine"};if(picker.ShowDialog(this)==true)source.Text=picker.FolderName;});row.Children.Add(browse);
            type.SelectionChanged+=(_,_)=>browse.IsEnabled=type.SelectedItem?.ToString()=="bind";
            Func<string> value=()=>StructuredInputs.Mount(type.SelectedItem?.ToString()??"bind",source.Text.Trim(),destination.Text.Trim(),ro.IsChecked==true);
            mountRows.Add(value);row.Children.Add(Button("Remove mount row",()=>{mountRows.Remove(value);mountList.Children.Remove(row);}));mountList.Children.Add(row);
        }));
        var execution=Expert(p,"Command and non-secret environment");
        var argv=new TextBox{AcceptsReturn=true,Height=80};var environment=new TextBox{AcceptsReturn=true,Height=100};var entrypoint=new TextBox();var user=new TextBox();var workingDirectory=new TextBox();
        execution.Children.Add(new TextBlock{Text="Command argv is an explicit JSON string array, for example [\"/bin/echo\",\"hello world\"]. Values are passed directly to Docker, never to a host shell. Leave blank to retain the image command. Environment rows use NAME=value; do not enter credentials or other secrets."});
        Field(execution,"Command argv JSON",argv);Field(execution,"Environment (one NAME=value per line)",environment);Field(execution,"Entrypoint executable",entrypoint);Field(execution,"Container user or UID",user);Field(execution,"Container working directory",workingDirectory);
        var resources=Expert(p,"Resources, restart and security");var cpus=new TextBox();var memory=new TextBox();var restart=new ComboBox{ItemsSource=new[]{"no","always","unless-stopped","on-failure"},SelectedIndex=0};var noNewPrivileges=new CheckBox{Content="Prevent gaining new privileges",IsChecked=true};
        Field(resources,"CPU limit",cpus);Field(resources,"Memory limit (for example 512m)",memory);Field(resources,"Restart policy",restart);resources.Children.Add(noNewPrivileges);
        resources.Children.Add(new TextBlock{Text="No privileged mode or host networking is enabled. Health checks, devices, GPUs and interactive terminals are outside this editor's current scope.",Opacity=.7});
        var resultText=new TextBlock{Margin=new Thickness(0,12,0,8)};p.Children.Add(resultText);
        var d=Dialog("Run container",p);
        var launch=AsyncButton("Run",async()=>
        {
            try
            {
                if(!SourceSafety.IsCurrentSnapshot(snapshotContext,snapshotGeneration,Context,contextGeneration))throw new InvalidOperationException("The engine changed. Close this dialog and refresh the image inventory.");
                var command=StructuredInputs.Argv(argv.Text);
                var options=new RunOptions { Image=image,Name=Optional(name),Network=Optional(network),ReadOnly=readOnly.IsChecked==true,Environment=StructuredInputs.Pairs(environment.Text),EntryPoint=Optional(entrypoint),User=Optional(user),WorkingDirectory=Optional(workingDirectory),Cpus=StructuredInputs.Cpus(cpus.Text.Trim()),Memory=StructuredInputs.Memory(memory.Text.Trim()),RestartPolicy=restart.SelectedItem?.ToString()??"no" };
                var args=DockerService.CreateRunArguments(options).ToList();args.RemoveAt(args.Count-1);
                if(port.Text.Trim().Length>0)
                {
                    var parts=port.Text.Trim().Split('/');var pair=parts[0].Split(':');if(pair.Length!=2||parts.Length>2)throw new ArgumentException("Use host:container ports, optionally /tcp or /udp.");
                    args.AddRange(["--publish",StructuredInputs.Port("127.0.0.1",pair[0],pair[1],parts.Length==2?parts[1]:"tcp")]);
                }
                if(mount.Text.Trim().Length>0)
                {
                    var match=System.Text.RegularExpressions.Regex.Match(mount.Text.Trim(),@"^(.+):(/[^:]+?)(?::(ro|rw))?$",System.Text.RegularExpressions.RegexOptions.None,TimeSpan.FromMilliseconds(100));
                    if(!match.Success)throw new ArgumentException("Basic mount uses source:/container/path[:ro]. Use additional mount rows for other destination formats.");
                    var source=match.Groups[1].Value;var type=source.Contains('/')||source.Contains('\\')||source.Contains(':')?"bind":"volume";
                    args.AddRange(["--mount",StructuredInputs.Mount(type,source,match.Groups[2].Value,match.Groups[3].Value=="ro")]);
                }
                foreach(var row in portRows)args.AddRange(["--publish",row()]);foreach(var row in mountRows)args.AddRange(["--mount",row()]);
                if(noNewPrivileges.IsChecked==true)args.AddRange(["--security-opt","no-new-privileges:true"]);args.Add(image);args.AddRange(command);
                resultText.Text="Creating container on context "+(snapshotContext??"current")+". Waiting for the actual process result.";
                var result=await Execute("Run container",args,boundContext:snapshotContext);
                resultText.Text=result?.Success==true?"Container created.":"Container creation did not complete. Review operation output.";
                if(result?.Success==true)d.Close();
            }
            catch(Exception e){resultText.Text=e.Message;Notify(e.Message);}
        },true);
        p.Children.Add(launch);d.Closing+=(_,e)=>{if(operation!=null){e.Cancel=true;resultText.Text="Cancel the active operation before closing this dialog.";}};
        p.Children.Add(Button("Cancel operation",()=>operation?.Cancel()));d.ShowDialog();
    }
}
