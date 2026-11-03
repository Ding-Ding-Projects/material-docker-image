using MaterialDockerImage.UI;
var count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
void Reject(Action action,string name){try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){count++;Console.WriteLine("PASS "+name);return;}throw new Exception("Expected refusal: "+name);}
var source="# retained\r\n  FROM   alpine:3.20   AS   base  \r\nRUN echo ok\r\nFROM --platform=linux/amd64 base AS final\r\nCOPY --from=base /a /b\r\n";
var graph=DockerfileGraph.Parse(source);
Check(graph.Stages.Count==2&&graph.Edges.Count==2,"platform base and copy relationships");
var edited=DockerfileGraph.EditStage(source,graph.Stages[0],"alpine:3.21","base");
Check(edited==source.Replace("alpine:3.20","alpine:3.21"),"exact token change retains comments whitespace CRLF");
Reject(()=>DockerfileGraph.EditStage(source,graph.Stages[0],"alpine:3.20","renamed"),"referenced platform-stage alias cannot be silently renamed");
var forwardSource="FROM alpine AS first\nCOPY --from=build /x /x\nFROM alpine AS build\nRUN touch /x";
var forwardGraph=DockerfileGraph.Parse(forwardSource);
Check(forwardGraph.Edges.Any(x=>x.From==1&&x.To==0&&x.Label=="COPY"),"forward copy reference resolves after every stage is collected");
Reject(()=>DockerfileGraph.EditStage(forwardSource,forwardGraph.Stages[1],"alpine","builder"),"forward-referenced alias rename rejected without dangling copy");
Check(DockerfileGraph.EditStage(forwardSource,forwardGraph.Stages[1],"alpine:3.21","build")==forwardSource.Replace("FROM alpine AS build","FROM alpine:3.21 AS build"),"forward-reference base edit preserves the unchanged alias and source");
Reject(()=>DockerfileGraph.EditStage(source,graph.Stages[0],"base","base"),"self stage base rejected");
Reject(()=>DockerfileGraph.EditStage(source,graph.Stages[0],"final","base"),"forward stage base rejected");
Reject(()=>DockerfileGraph.EditStage(source,graph.Stages[0],"alpine","final"),"duplicate stage alias rejected");
Reject(()=>DockerfileGraph.EditStage(source.Replace("alpine:3.20","ubuntu"),graph.Stages[0],"alpine","base"),"stale source edit rejected");
foreach(var unsupported in new[]{"FROM alpine \\\n AS base\nFROM base AS final", "FROM alpine\nRUN <<EOF\nFROM imaginary\nEOF", "# escape=`\nFROM alpine"})
 Check(DockerfileGraph.Parse(unsupported).Stages.Count==0,"unsupported grammar remains source only");
var lf="FROM alpine AS build\nRUN echo preserved\n";
Check(DockerfileGraph.EditStage(lf,DockerfileGraph.Parse(lf).Stages[0],"ubuntu","build")=="FROM ubuntu AS build\nRUN echo preserved\n","LF preserved");
Check(SourceSafety.IsCurrentSnapshot("a",1,"a",1),"same snapshot accepted");
Check(!SourceSafety.IsCurrentSnapshot("a",1,"b",1),"different context rejected");
Check(!SourceSafety.IsCurrentSnapshot("a",1,"a",2),"returned-to-context stale generation rejected");
var loaded=Path.Combine(Path.GetTempPath(),"loaded-compose.yaml");var other=Path.Combine(Path.GetTempPath(),"unseen-compose.yaml");
Check(SourceSafety.RequireLoadedPath(loaded,loaded)==Path.GetFullPath(loaded),"save is bound to loaded source");
Reject(()=>SourceSafety.RequireLoadedPath(loaded,other),"edited path cannot overwrite unseen source");
Reject(()=>SourceSafety.RequireLoadedPath("",other),"unloaded source cannot be saved");
Check(!UserSettings.DataDirectory.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialDockerImage")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"settings are outside Squirrel installation root");
Check(!string.Equals(UserSettings.DataDirectory,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialDockerImage"),StringComparison.OrdinalIgnoreCase),"settings do not use Squirrel installation root");
Check(StructuredInputs.Argv("[\"/bin/echo\",\"a b; $(unchanged)\"]").SequenceEqual(new[]{"/bin/echo","a b; $(unchanged)"}),"JSON argv preserves argument boundaries without shell parsing");
Reject(()=>StructuredInputs.Argv("[1]"),"non-string command arguments rejected");
Reject(()=>StructuredInputs.Argv("[\"x\\nnext\"]"),"control characters in command arguments rejected");
Check(StructuredInputs.Pairs("NAME=value with spaces\nEMPTY=")["EMPTY"]=="","environment rows preserve spaces and empty values");
Check(StructuredInputs.Pairs("NAME=value  \nSPACE=   ")["NAME"]=="value  ","environment trailing spaces preserved");
Check(StructuredInputs.Pairs("NAME=value  \nSPACE=   ")["SPACE"]=="   ","all-space environment value preserved");
Check(StructuredInputs.Pairs("example.com/component:kind=worker  ",true)["example.com/component:kind"]=="worker  ","label punctuation and trailing value spaces match Core semantics");
Reject(()=>StructuredInputs.Pairs(" bad=1",true),"label key whitespace rejected");
Reject(()=>StructuredInputs.Pairs("--bad=1",true),"label option-like key rejected");
var oversizedGraph=DockerfileGraph.Parse("FROM alpine AS base\n"+new string('x',1024*1024));
Check(oversizedGraph.Stages.Count==0&&oversizedGraph.Warnings.Single().Contains("no content was truncated"),"oversized Canvas input explicitly remains source only");
Reject(()=>StructuredInputs.Pairs("NAME=a\nNAME=b"),"duplicate environment key rejected");
Reject(()=>StructuredInputs.Pairs("BAD-NAME=x"),"invalid environment key rejected");
Check(StructuredInputs.Pairs("org.example.version=1",true).Count==1,"namespaced labels accepted");
Check(StructuredInputs.Cpus("1.5")==1.5&&StructuredInputs.Memory("512m")=="512m","CPU and memory formats accepted");
Reject(()=>StructuredInputs.Cpus("NaN"),"non-finite CPU rejected");
Reject(()=>StructuredInputs.Memory("-1g"),"negative memory rejected");
Check(StructuredInputs.Port("127.0.0.1","8080","80","tcp")=="127.0.0.1:8080:80/tcp","loopback port mapping retained");
Check(StructuredInputs.Port("::1","8080","80","udp")=="[::1]:8080:80/udp","IPv6 port address bracketed");
Reject(()=>StructuredInputs.Port("127.0.0.1","65536","80","tcp"),"out-of-range port rejected");
Reject(()=>StructuredInputs.Port("example.com","8080","80","tcp"),"ambiguous bind hostname rejected");
Check(StructuredInputs.Mount("bind","/remote/not-present-locally","/data",true)=="type=bind,source=/remote/not-present-locally,target=/data,readonly","remote bind path does not require local existence");
Check(StructuredInputs.Mount("volume","project_data","/data",false)=="type=volume,source=project_data,target=/data","named volume mapping remains explicit");
Reject(()=>StructuredInputs.Mount("bind","/data,readonly","/target",true),"mount CSV option injection rejected");
Reject(()=>StructuredInputs.Mount("bind","relative","/target",true),"relative bind source rejected");
Check(StructuredInputs.Secret("build_auth","C:/selected/file")=="type=file,id=build_auth,src=C:/selected/file","secret file command uses metadata only");
Reject(()=>StructuredInputs.Secret("id,env=other","C:/file"),"secret identifier option injection rejected");
Reject(()=>StructuredInputs.Export("C:/image,compression=zstd.tar"),"OCI destination option injection rejected");
Check(StructuredInputs.Cache("type=registry,ref=registry.example/team/cache").StartsWith("type=registry"),"explicit cache registry specification accepted");
Reject(()=>StructuredInputs.Cache("--push"),"unstructured cache option rejected");

// Personal wording overlay: validated in full, never applied partially.
Check(MaterialDockerImage.Services.Loc.ParseVocabulary("{\"version\":1,\"entries\":{\"Build\":\"Make\"}}")["Build"]=="Make","personal wording file accepted");
foreach(var bad in new[]{"[]","{\"entries\":{}}","{\"version\":2,\"entries\":{}}","{\"version\":1,\"entries\":{\"a\":1}}","{\"version\":1,\"entries\":{\"a\":\"x\",\"a\":\"y\"}}","{\"version\":1,\"entries\":{\""+new string('k',161)+"\":\"x\"}}","{\"version\":1,\"entries\":{\"k\":\""+new string('v',1001)+"\"}}"})
{ try { MaterialDockerImage.Services.Loc.ParseVocabulary(bad); throw new Exception("Expected refusal: invalid personal wording "+bad[..Math.Min(30,bad.Length)]); } catch(Exception e) when (e is System.Text.Json.JsonException or InvalidDataException) { count++; Console.WriteLine("PASS invalid personal wording rejected"); } }
MaterialDockerImage.Services.Loc.Language="Cantonese"; MaterialDockerImage.Services.Loc.FunnyCantonese=1;
Check(MaterialDockerImage.Services.Loc.T("Images")=="映像","Cantonese label");
Check(MaterialDockerImage.Services.Loc.Voice("Build completed.","Done!","搞掂！")=="Build completed.","funny level 1 states the fact only");
MaterialDockerImage.Services.Loc.FunnyCantonese=5;
Check(MaterialDockerImage.Services.Loc.Voice("Build completed.","Done!","搞掂！").EndsWith("Build completed."),"funny level 5 keeps the fact");
MaterialDockerImage.Services.Loc.SchoolMode=true;
Check(MaterialDockerImage.Services.Loc.T("Images")=="Images","school mode forces English");
MaterialDockerImage.Services.Loc.SchoolMode=false; MaterialDockerImage.Services.Loc.Language="Bilingual";
Check(MaterialDockerImage.Services.Loc.T("Images")=="Images · 映像","bilingual keeps the primary label first");
MaterialDockerImage.Services.Loc.Language="English";

// Local version history: append-only, restores are new revisions, labels and diffs work.
var historyRoot=Path.Combine(Path.GetTempPath(),"mdi-history-"+Guid.NewGuid().ToString("N"));
try
{
    var history=new MaterialDockerImage.Services.HistoryStore(historyRoot);
    Check(history.Record("dockerfile","Saved first",new Dictionary<string,string>{["files/dockerfile/a.txt"]="FROM alpine\n"}),"history records a revision");
    Check(history.Record("dockerfile","Saved second",new Dictionary<string,string>{["files/dockerfile/a.txt"]="FROM ubuntu\n"}),"history records a second revision");
    var list=history.List();
    Check(list.Count==3&&list[0].Summary.Contains("Saved second")&&list[0].Action=="dockerfile","history lists newest first with actions");
    Check(list[0].Paths.Contains("files/dockerfile/a.txt"),"history names the changed file");
    Check(history.Diff(list[0].Sha).Contains("+FROM ubuntu"),"history diff shows the change");
    var restored=history.Restore(list[1].Sha,"files/dockerfile/a.txt");
    Check(restored=="FROM alpine\n","restore returns the earlier content");
    var after=history.List();
    Check(after.Count==4&&after[0].Action=="restore","restore is a new revision");
    Check(history.Restore(after[0].Sha,"files/dockerfile/a.txt")=="FROM alpine\n","a restore of a restore works");
    Check(history.Label(after[0].Sha,"before upgrade")&&history.List().Any(e=>e.Labels.Contains("before_upgrade")),"labels attach to revisions");
    var zip=Path.Combine(historyRoot,"..",Path.GetFileName(historyRoot)+".zip");
    history.Export(history.List(),zip);
    using(var archive=System.IO.Compression.ZipFile.OpenRead(zip)) Check(archive.Entries.Any(e=>e.FullName=="index.json")&&archive.Entries.Count(e=>e.FullName.StartsWith("patches/"))>=5,"history export holds an index and patches");
    File.Delete(zip);
    Check(!history.Record("x","escape",new Dictionary<string,string>{["../outside.txt"]="no"})&&!File.Exists(Path.Combine(historyRoot,"..","outside.txt")),"history refuses paths outside its folder");
}
finally { try { foreach(var f in Directory.EnumerateFiles(historyRoot,"*",SearchOption.AllDirectories)) File.SetAttributes(f,FileAttributes.Normal); Directory.Delete(historyRoot,true); } catch(IOException) { } }

// Base64 toolkit: variants, padding, strict offsets, data URLs.
{
    var bytes=new byte[]{0xfb,0xff,0x01,0x02,0x03};
    foreach(var variant in Enum.GetValues<MaterialDockerImage.UI.Base64Variant>())
    foreach(var pad in new[]{true,false})
    {
        var text=MaterialDockerImage.UI.Base64Tools.Encode(bytes,variant,pad);
        var back=MaterialDockerImage.UI.Base64Tools.Decode(text,variant,true,pad);
        Check(back.Success&&back.Bytes.SequenceEqual(bytes),$"base64 round trip {variant} padding {pad}");
    }
    Check(MaterialDockerImage.UI.Base64Tools.Encode(bytes,MaterialDockerImage.UI.Base64Variant.UrlSafe,false)=="-_8BAgM","URL-safe alphabet without padding");
    var strict=MaterialDockerImage.UI.Base64Tools.Decode("QUJD*REVG",MaterialDockerImage.UI.Base64Variant.Standard,true);
    Check(!strict.Success&&strict.ErrorOffset==4,"strict decoding reports the exact offset of the bad character");
    Check(MaterialDockerImage.UI.Base64Tools.TryParseDataUrl(MaterialDockerImage.UI.Base64Tools.BuildDataUrl("text/plain",new byte[]{104,105}),out var url,out _)&&url!.MimeType=="text/plain"&&url.Bytes.SequenceEqual(new byte[]{104,105}),"data URL builds and parses");
}

// Dockerfile Creator model: every starter renders, parses back to identical text, and survives JSON.
foreach(var starter in MaterialDockerImage.UI.DockerfileStarters.All)
{
    var document=starter.Create();
    var text=MaterialDockerImage.UI.DockerfileModel.Render(document);
    var parsed=MaterialDockerImage.UI.DockerfileModel.Parse(text);
    Check(MaterialDockerImage.UI.DockerfileModel.Render(parsed.Document)==text,$"starter {starter.Id} round trips through the parser");
    Check(MaterialDockerImage.UI.DockerfileModel.Render(MaterialDockerImage.UI.DockerfileModel.Deserialize(MaterialDockerImage.UI.DockerfileModel.Serialize(document)))==text,$"starter {starter.Id} survives draft JSON");
    if(starter.Id!="blank") Check(DockerfileGraph.Parse(text).Stages.Count==document.Stages.Count,$"starter {starter.Id} is readable by the canvas parser");
}
Check(MaterialDockerImage.UI.DockerfileModel.ExecJson(new[]{"echo","a \"quoted\" \\ value"})=="[\"echo\", \"a \\\"quoted\\\" \\\\ value\"]","exec form escapes quotes and backslashes");
{
    var bad=MaterialDockerImage.UI.DockerfileModel.Parse("FROM ubuntu\nRUN apt-get update\nADD . /app\nENV API_TOKEN=x\nCMD node server.js\n").Document;
    var ids=MaterialDockerImage.UI.DockerfileLint.Check(bad).Where(r=>r.Severity!=MaterialDockerImage.UI.LintSeverity.Pass).Select(r=>r.Title).ToList();
    Check(ids.Count>=5,"lint flags an unpinned base, root user, missing health check, ADD, secret-like ENV and shell-form CMD");
    var fixable=MaterialDockerImage.UI.DockerfileLint.Check(bad).First(r=>r.Fix?.Apply!=null);
    var before=MaterialDockerImage.UI.DockerfileLint.CountProblems(MaterialDockerImage.UI.DockerfileLint.Check(bad));
    fixable.Fix!.Apply!(bad);
    Check(MaterialDockerImage.UI.DockerfileLint.CountProblems(MaterialDockerImage.UI.DockerfileLint.Check(bad))<before,"an automatic lint fix removes its finding");
}

// Appearance: colour conversions, contrast, presets and undo.
{
    var tolerance=1.0/255+1e-9;
    var samples=new[]{"#3A6FD8","#000000","#FFFFFF","#FF0000","#00FF00","#0000FF","#7F7F7F","#C42B1C","#12345680","#FFD60A","#0B7A99","#E2582C"};
    foreach(var hex in samples)
    {
        Check(AppearanceRgba.TryParseHex(hex,out var c),"hex parses "+hex);
        Check(AppearanceRgba.TryParseHex(c.ToHex8(),out var c8)&&AppearanceRgba.Distance(c,c8)<=tolerance,"hex8 round trip "+hex);
        var (h,s,l)=c.ToHsl(); Check(AppearanceRgba.Distance(c,AppearanceRgba.FromHsl(h,s,l,c.A))<=tolerance,"hsl round trip "+hex);
        var (hv,sv,vv)=c.ToHsv(); Check(AppearanceRgba.Distance(c,AppearanceRgba.FromHsv(hv,sv,vv,c.A))<=tolerance,"hsv round trip "+hex);
        var (hw,ww,bw)=c.ToHwb(); Check(AppearanceRgba.Distance(c,AppearanceRgba.FromHwb(hw,ww,bw,c.A))<=tolerance,"hwb round trip "+hex);
        var (ol,oc,oh)=c.ToOklch(); var back=AppearanceRgba.FromOklch(ol,oc,oh,out var fits,c.A); Check(fits&&AppearanceRgba.Distance(c,back)<=tolerance,"oklch round trip "+hex);
        var (cc,cm,cy,ck)=c.ToCmyk(); Check(AppearanceRgba.Distance(c.WithAlpha(1),AppearanceRgba.FromCmyk(cc,cm,cy,ck))<=tolerance,"cmyk round trip "+hex);
        foreach(var format in Enum.GetValues<AppearanceColorFormat>())
        {
            var text=c.Format(format);
            var expectAlpha=format is not (AppearanceColorFormat.Hex or AppearanceColorFormat.Cmyk);
            Check(AppearanceRgba.TryParse(text,out var parsed,out _)&&AppearanceRgba.Distance(expectAlpha?c:c.WithAlpha(1),parsed)<=tolerance,$"{format} text round trip {hex} via {text}");
        }
    }
    Check(AppearanceRgba.TryParseStorageHex("#80123456",out var storage)&&storage.A8==0x80&&storage.R8==0x12&&storage.ToStorageHex()=="#80123456","settings hex keeps Windows alpha-first order");
    Check(Math.Abs(AppearanceRgba.ContrastRatio(new(0,0,0),new(1,1,1))-21)<1e-9,"black on white contrast is 21");
    Check(Math.Abs(AppearanceRgba.ContrastRatio(new(.2,.4,.6),new(.2,.4,.6))-1)<1e-12,"same colour contrast is 1");
    AppearanceRgba.TryParseHex("#767676",out var grey);
    Check(Math.Abs(AppearanceRgba.ContrastRatio(grey,new(1,1,1))-4.54)<0.01,"#767676 on white is 4.54");
    Check(AppearanceRgba.TryParse("oklch(70% 0.4 150)",out var clipped,out var gamutWarning)&&gamutWarning!=null&&clipped.InGamut(),"out-of-gamut OKLCH is mapped and warned");
    Check(!AppearanceRgba.TryParse("rgb(300, 0, 0)",out _,out _)&&!AppearanceRgba.TryParse("#12",out _,out _)&&!AppearanceRgba.TryParse("hsl(nan, 1, 1)",out _,out _),"invalid colour text rejected");
    Check(AppearanceRoles.ContrastPartner("OnPrimary")=="Primary"&&AppearanceRoles.ContrastPartner("SurfaceLow")=="OnSurface"&&AppearanceRoles.ContrastPartner("PrimaryContainer")=="OnPrimaryContainer","contrast partners");

    Check(AppearanceMotion.Effective("Expressive",true,false,true,false)=="Reduced"&&AppearanceMotion.Effective("Standard",false,false,false,false)=="Off"&&AppearanceMotion.Effective("Expressive",false,false,true,true)=="Off"&&AppearanceMotion.Effective("Standard",false,true,true,false)=="Reduced","reduced motion, low stimulation, Windows setting and capture all stop motion");
    Check(AppearanceMotion.Duration(AppearanceMotion.Medium2,"Reduced",1)==0&&AppearanceMotion.Duration(AppearanceMotion.Medium2,"Standard",2)==150&&AppearanceMotion.Duration(AppearanceMotion.Medium2,"Standard",0.5)==600,"durations snap when reduced and follow the speed multiplier");
    Check(AppearanceMotion.RainbowHue(210,7,false)==210&&AppearanceMotion.RainbowHue(210,6,true)!=210,"rainbow settles on the saved hue without motion");

    foreach(var preset in AppearancePresets.BuiltIn)
    {
        Check(preset.State.Validate().Count==0,"built-in preset valid: "+preset.Name);
        var again=AppearancePresets.Import(AppearancePresets.Export(preset));
        Check(again.Name==preset.Name&&again.State.SameAs(preset.State),"preset JSON round trip: "+preset.Name);
    }
    var good=AppearancePresets.Export(AppearancePresets.BuiltIn[3]);
    foreach(var bad in new[]{good.Replace("material-docker-image.appearance","other"),good.Replace("\"version\": 1","\"version\": 2"),good.Replace("\"Tertiary\"","\"NotARole\""),good.Replace("\"radiusScale\": 1.15","\"radiusScale\": 9"),good.Replace("\"name\": \"Ocean\"","\"name\": \"\""),good.Replace("#0B7A99","blue"),"{",new string(' ',70000),good.Replace("\"theme\"","\"extra\": 1, \"theme\"")})
    { try { AppearancePresets.Import(bad); throw new Exception("Expected refusal: invalid preset"); } catch(InvalidDataException) { count++; Console.WriteLine("PASS invalid preset rejected"); } }
    var store=Path.Combine(Path.GetTempPath(),"mdi-presets-"+Guid.NewGuid().ToString("N")+".json");
    try
    {
        AppearancePresets.SaveUser(store,new[]{new AppearancePreset("Mine",AppearancePresets.BuiltIn[5].State)});
        var loadedPresets=AppearancePresets.LoadUser(store);
        Check(loadedPresets.Count==1&&loadedPresets[0].Name=="Mine"&&loadedPresets[0].State.SameAs(AppearancePresets.BuiltIn[5].State),"user preset file round trip");
    }
    finally { File.Delete(store); }

    var undo=new AppearanceUndoStack(3);
    var states=Enumerable.Range(0,5).Select(i=>{var st=new AppearanceState{RadiusScale=0.5+i*0.1};st.Normalize();return st;}).ToList();
    for(var i=0;i<4;i++) undo.Push(states[i]);
    Check(undo.UndoCount==3,"undo stack is bounded");
    var current=states[4];
    var u1=undo.Undo(current)!; Check(u1.SameAs(states[3])&&undo.CanRedo,"undo returns the previous state");
    var r1=undo.Redo(u1)!; Check(r1.SameAs(states[4]),"redo returns the undone state");
    undo.Undo(r1); undo.Push(states[0]); Check(!undo.CanRedo,"a new change clears redo");
    var settingsRoundTrip=new UserSettings(); AppearancePresets.BuiltIn[2].State.WriteTo(settingsRoundTrip);
    Check(AppearanceState.FromSettings(settingsRoundTrip).SameAs(AppearancePresets.BuiltIn[2].State),"appearance round trips through settings");
    var corrupt=new UserSettings{CornerScale=double.NaN,MotionLevel="Wild",RoleOverrides=new(){["Bogus"]="#FFF",["Primary"]="nope"}};
    var repaired=AppearanceState.FromSettings(corrupt);
    Check(repaired.RadiusScale==1&&repaired.MotionLevel=="Standard"&&repaired.Roles.Count==0,"corrupt saved appearance is repaired, not applied");
}
Console.WriteLine($"UI state checks: {count} passed.");
