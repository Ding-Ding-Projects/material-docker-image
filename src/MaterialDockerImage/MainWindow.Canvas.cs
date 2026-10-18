using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MaterialDockerImage.Core;
using MaterialDockerImage.UI;

namespace MaterialDockerImage;

public partial class MainWindow
{
    private Canvas? graphCanvas;
    private ScrollViewer? graphScroll;
    private ListBox? graphObjects;
    private StackPanel? graphInspector;
    private TextBlock? graphState;
    private readonly ScaleTransform graphScale = new(1,1);
    private DockerGraph? currentGraph;
    private string graphSource = "";
    private bool runtimeGraph;
    private Point? panPoint;
    private double panHorizontal, panVertical;

    private FrameworkElement CanvasPage()
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });
        var header = Page("Canvas","A real native graph of your Dockerfile or inspected runtime relationships. Drag with the middle mouse button to pan; arrow keys scroll. Select objects in the list for keyboard access.");
        var actions = Actions(header); actions.Children.Add(Button("Parse Dockerfile",DrawDockerfile,true)); actions.Children.Add(AsyncButton("Container topology",DrawRuntime));
        actions.Children.Add(Button("Zoom in",()=>Zoom(1.2))); actions.Children.Add(Button("Zoom out",()=>Zoom(1/1.2))); actions.Children.Add(Button("Fit graph",FitGraph));
        graphState = new TextBlock { Text = "No source parsed. Open a Dockerfile in Build, or load a real runtime snapshot.", Opacity = .7 }; header.Children.Add(graphState); root.Children.Add(header);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); Grid.SetRow(layout,1);
        graphObjects = new ListBox { Margin = new Thickness(4), DisplayMemberPath = "Label" }; System.Windows.Automation.AutomationProperties.SetAutomationId(graphObjects,"CanvasObjects");
        graphObjects.SelectionChanged += (_,_) => { if(graphObjects.SelectedItem is GraphItem item) item.Select(); }; layout.Children.Add(graphObjects);
        graphCanvas = new Canvas { Width = 1000, Height = 650, Background = new SolidColorBrush(Color.FromRgb(20,27,35)), LayoutTransform = graphScale, Focusable = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(graphCanvas,"GraphCanvas");
        graphScroll = new ScrollViewer { Content = graphCanvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(8), CanContentScroll = false }; Grid.SetColumn(graphScroll,1); layout.Children.Add(graphScroll);
        graphCanvas.PreviewMouseWheel += (_,e)=> { if(Keyboard.Modifiers == ModifierKeys.Control) { Zoom(e.Delta > 0 ? 1.1 : 1/1.1); e.Handled = true; } };
        graphCanvas.MouseDown += (_,e)=> { if(e.ChangedButton == MouseButton.Middle) { panPoint = e.GetPosition(graphScroll); panHorizontal = graphScroll.HorizontalOffset; panVertical = graphScroll.VerticalOffset; graphCanvas.CaptureMouse(); e.Handled = true; } };
        graphCanvas.MouseMove += (_,e)=> { if(panPoint is { } start) { var point = e.GetPosition(graphScroll); graphScroll.ScrollToHorizontalOffset(panHorizontal + start.X-point.X); graphScroll.ScrollToVerticalOffset(panVertical + start.Y-point.Y); } };
        graphCanvas.MouseUp += (_,e)=> { if(e.ChangedButton == MouseButton.Middle) { panPoint = null; graphCanvas.ReleaseMouseCapture(); } };
        graphCanvas.KeyDown += (_,e)=> { switch(e.Key) { case Key.Left: graphScroll.ScrollToHorizontalOffset(graphScroll.HorizontalOffset-60); break; case Key.Right: graphScroll.ScrollToHorizontalOffset(graphScroll.HorizontalOffset+60); break; case Key.Up: graphScroll.ScrollToVerticalOffset(graphScroll.VerticalOffset-60); break; case Key.Down: graphScroll.ScrollToVerticalOffset(graphScroll.VerticalOffset+60); break; case Key.Escape: panPoint = null; graphCanvas.ReleaseMouseCapture(); break; } };
        graphInspector = new StackPanel { Margin = new Thickness(12) }; Grid.SetColumn(graphInspector,2); layout.Children.Add(new ScrollViewer { Content = graphInspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.InColumn(2));
        root.Children.Add(layout); return root;
    }
    private sealed record GraphItem(string Label, Action Select);
    private void Zoom(double factor) { graphScale.ScaleX = graphScale.ScaleY = Math.Clamp(graphScale.ScaleX*factor,.25,2.5); if(graphState != null) graphState.ToolTip = $"Zoom {graphScale.ScaleX:P0}"; }
    private void FitGraph() { if(graphCanvas != null && graphScroll != null) { var factor = Math.Min(graphScroll.ViewportWidth / graphCanvas.Width,graphScroll.ViewportHeight / graphCanvas.Height); graphScale.ScaleX = graphScale.ScaleY = Math.Clamp(factor,.25,1.5); graphScroll.ScrollToHome(); } }
    private void ResetGraph(double width, double height) { graphCanvas!.Children.Clear(); graphCanvas.Width = width; graphCanvas.Height = height; graphObjects!.Items.Clear(); graphInspector!.Children.Clear(); }
    private void Edge(double x1,double y1,double x2,double y2,string label)
    {
        graphCanvas!.Children.Add(new Line { X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=new SolidColorBrush(Color.FromRgb(111,151,204)),StrokeThickness=2 });
        var text = new TextBlock { Text=label,FontSize=11,Background=new SolidColorBrush(Color.FromRgb(20,27,35)),Foreground=Brushes.LightSteelBlue }; Canvas.SetLeft(text,(x1+x2)/2); Canvas.SetTop(text,(y1+y2)/2-10); graphCanvas.Children.Add(text);
    }
    private void Node(double x,double y,string title,string detail,Action selected,bool runtime=false)
    {
        var content=new StackPanel(); content.Children.Add(new TextBlock { Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White }); content.Children.Add(new TextBlock { Text=detail,FontSize=12,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap });
        var b=new Button { Content=content,Width=220,Height=112,Background=new SolidColorBrush(runtime?Color.FromRgb(35,62,67):Color.FromRgb(37,51,74)),HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip=title+"\n"+detail };
        System.Windows.Automation.AutomationProperties.SetName(b,title+". "+detail); b.Click+=(_,_)=>selected(); Canvas.SetLeft(b,x); Canvas.SetTop(b,y); graphCanvas!.Children.Add(b); graphObjects!.Items.Add(new GraphItem(title,selected));
    }
    private void DrawDockerfile()
    {
        runtimeGraph=false; graphSource=dockerfile.Text; currentGraph=DockerfileGraph.Parse(graphSource);
        ResetGraph(Math.Max(700,currentGraph.Stages.Count*270+60),500);
        foreach(var edge in currentGraph.Edges) Edge(50+edge.From*270+220,150,50+edge.To*270,150,edge.Label);
        foreach(var stage in currentGraph.Stages) Node(50+stage.Index*270,95,stage.Name,$"Base: {stage.Base}\n{stage.Instructions.Count} source lines",()=>SelectStage(stage));
        graphState!.Text = $"{currentGraph.Stages.Count} stages · {currentGraph.Edges.Count} relationships. " + (currentGraph.Warnings.Count > 0 ? string.Join(" ",currentGraph.Warnings) : "Parsed from the current unsaved editor source; not executed.");
        if(currentGraph.Stages.Count > 0) SelectStage(currentGraph.Stages[0]);
    }
    private void SelectStage(DockerStage stage)
    {
        graphInspector!.Children.Clear(); graphInspector.Children.Add(new TextBlock { Text="Stage "+stage.Name,FontSize=22,Margin=new Thickness(0,0,0,12) });
        var alias=new TextBox { Text=stage.Name }; var baseImage=new TextBox { Text=stage.Base }; Field(graphInspector,"Stage alias",alias); Field(graphInspector,"Base image",baseImage);
        graphInspector.Children.Add(Button("Apply stage changes",()=> { if(dockerfile.Text!=graphSource) throw new InvalidOperationException("Source changed. Parse the current Dockerfile before applying changes."); var updated=DockerfileGraph.EditStage(dockerfile.Text,stage,baseImage.Text.Trim(),alias.Text.Trim()); dockerfile.BeginChange(); dockerfile.Text=updated; dockerfile.EndChange(); DrawDockerfile(); Notify("Stage source updated in the editor. Save explicitly before building."); }));
        graphInspector.Children.Add(Button("Select build target",()=> { target.Text=stage.Name; Notify("Build target set to "+stage.Name); }));
        graphInspector.Children.Add(Button("Open source",()=> { OpenPage("Build"); dockerfile.Focus(); dockerfile.ScrollToLine(stage.Line); var start=dockerfile.GetCharacterIndexFromLineIndex(stage.Line); if(start>=0) { dockerfile.Select(start,dockerfile.GetLineLength(stage.Line)); } }));
        graphInspector.Children.Add(new TextBlock { Text=string.Join(Environment.NewLine,stage.Instructions),FontFamily=new FontFamily("Consolas"),FontSize=12,Margin=new Thickness(0,16,0,0) });
    }
    private async Task DrawRuntime()
    {
        var list=await Execute("Read container topology",["container","ls","--all","--format","{{json .}}"],false);
        if(list?.Success!=true) { if(graphState!=null) graphState.Text="Snapshot unavailable. Any previous graph is stale."; return; }
        var rows=DockerJson.ParseRows(list.StandardOutput); var ids=rows.Select(x=>DockerJson.Text(x,"ID")).Where(x=>x.Length>0).Take(100).ToArray();
        if(ids.Length==0) { ResetGraph(700,500); graphState!.Text="No containers reported by the selected engine."; return; }
        var args=new List<string>{"container","inspect"}; args.AddRange(ids); var result=await Execute("Inspect topology relationships",args,false); if(result?.Success!=true) { graphState!.Text="Topology inspection failed. Previous graph is stale."; return; }
        using var document=JsonDocument.Parse(result.StandardOutput); var containers=document.RootElement.EnumerateArray().Select(x=>x.Clone()).ToArray();
        var networks=new Dictionary<string,List<int>>(); var volumes=new Dictionary<string,List<int>>();
        for(int i=0;i<containers.Length;i++)
        {
            var c=containers[i]; if(c.TryGetProperty("NetworkSettings",out var ns)&&ns.TryGetProperty("Networks",out var nets)) foreach(var n in nets.EnumerateObject()) { if(!networks.ContainsKey(n.Name))networks[n.Name]=new(); networks[n.Name].Add(i); }
            if(c.TryGetProperty("Mounts",out var mounts)) foreach(var m in mounts.EnumerateArray()) if(DockerJson.Text(m,"Type")=="volume") { var name=DockerJson.Text(m,"Name"); if(!volumes.ContainsKey(name))volumes[name]=new(); volumes[name].Add(i); }
        }
        runtimeGraph=true; ResetGraph(900,Math.Max(520,Math.Max(containers.Length,networks.Count+volumes.Count)*150+80));
        int resource=0; foreach(var group in networks.Concat(volumes)) { var y=50+resource*150; foreach(var index in group.Value)Edge(290,106+index*150,560,y+56,networks.ContainsKey(group.Key)?"network":"volume"); resource++; }
        for(int i=0;i<containers.Length;i++) { var c=containers[i]; var id=DockerJson.Text(c,"Id"); var name=DockerJson.Text(c,"Name").TrimStart('/'); var state=c.TryGetProperty("State",out var s)?DockerJson.Text(s,"Status"):"unknown"; Node(60,50+i*150,name,"Container · "+state,()=>RuntimeDetails("container",id,name,c),true); }
        resource=0; foreach(var group in networks) { var name=group.Key; Node(560,50+resource++*150,name,"Network · "+group.Value.Count+" observed attachments",()=>RuntimeResource("network",name),true); }
        foreach(var group in volumes) { var name=group.Key; Node(560,50+resource++*150,name,"Volume · "+group.Value.Count+" observed attachments",()=>RuntimeResource("volume",name),true); }
        graphState!.Text=$"Observed {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · context {Context ?? "current"} · {containers.Length} containers. "+(rows.Count>100?"Limited to the first 100 containers.":"Edges come from inspected memberships and mounts.");
    }
    private void RuntimeDetails(string kind,string id,string name,JsonElement record)
    {
        graphInspector!.Children.Clear(); graphInspector.Children.Add(new TextBlock{Text=name,FontSize=22}); graphInspector.Children.Add(new TextBlock{Text=kind+"\n"+id,FontSize=11,Margin=new Thickness(0,12,0,12)});
        graphInspector.Children.Add(AsyncButton("Inspect",()=>Inspect(kind,id)));
        foreach(var action in new[]{"start","stop","restart"}) graphInspector.Children.Add(AsyncButton(action,async()=>{if(action!="start"&&!Confirm(action+" container",id))return;await Execute(action+" container",["container",action,id]);await DrawRuntime();}));
        if(record.TryGetProperty("Mounts",out var mounts)) graphInspector.Children.Add(new TextBlock{Text="Mounts\n"+string.Join("\n",mounts.EnumerateArray().Select(m=>DockerJson.Text(m,"Type")+" → "+DockerJson.Text(m,"Destination"))),Margin=new Thickness(0,16,0,0)});
    }
    private void RuntimeResource(string kind,string name) { graphInspector!.Children.Clear();graphInspector.Children.Add(new TextBlock{Text=kind+": "+name,FontSize=20});graphInspector.Children.Add(AsyncButton("Inspect",()=>Inspect(kind,name))); }
}

internal static class GridPlacement
{
    public static T InColumn<T>(this T element,int column) where T: UIElement { Grid.SetColumn(element,column);return element; }
}
