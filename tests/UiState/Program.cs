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
Console.WriteLine($"UI state checks: {count} passed.");
