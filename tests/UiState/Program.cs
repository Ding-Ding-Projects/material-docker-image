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

// Minecraft server manager: versions, runtime tags, environment mapping, secrets, manifest parsing and area math.
{
    var V=typeof(MaterialDockerImage.UI.MinecraftVersions);
    foreach(var ok in new[]{"LATEST","snapshot","1.21.4","1.20","1.20.5-pre1","1.21-rc1","26.3-rc-3","26.1","26.1.2","26.4-snapshot-3","24w14a","1.14 Pre-Release 5"})
        Check(MaterialDockerImage.UI.MinecraftVersions.ValidateJava(ok)==null,$"java version {ok} accepted");
    foreach(var bad in new[]{"","1","latest-ish","1.21.4;rm","--version","24w1a","1.2.3.4"})
        Check(MaterialDockerImage.UI.MinecraftVersions.ValidateJava(bad)!=null,$"java version '{bad}' rejected");
    Check(MaterialDockerImage.UI.MinecraftVersions.ValidateBedrock("1.21.44.01")==null&&MaterialDockerImage.UI.MinecraftVersions.ValidateBedrock("PREVIEW")==null&&MaterialDockerImage.UI.MinecraftVersions.ValidateBedrock("1.21-rc1")!=null,"bedrock versions validated");
    var tags=new Dictionary<string,string>{["LATEST"]="latest",["SNAPSHOT"]="latest",["1.20.5"]="java21",["1.21.4"]="java21",["1.20.4"]="java17",["1.17"]="java17",["1.16.5"]="java8",["1.8.9"]="java8",["1.20.5-pre1"]="java21",["24w14a"]="java21",["24w13a"]="java17",["21w19a"]="java17",["20w45a"]="java8",["26.1"]="java25",["26.4-snapshot-3"]="java25"};
    foreach(var (version,tag) in tags) Check(MaterialDockerImage.UI.MinecraftVersions.JavaRuntimeTag(version)==tag,$"java runtime for {version} is {tag}");
    Check(MaterialDockerImage.UI.MinecraftVersions.Image(MaterialDockerImage.UI.MinecraftEdition.Java,"1.12.2")=="itzg/minecraft-server:java8","image reference uses the runtime tag");

    var spec=new MaterialDockerImage.UI.MinecraftServerSpec{Name="survival",EulaAccepted=true,Flavour="PAPER",Version="1.21.4",MemoryGb=4,Difficulty="hard",GameMode="creative",Pvp=false,Whitelist=true,Seed="12345",LevelType="minecraft:flat",Ops={"Steve"},WhitelistPlayers={"Alex","Steve"},ModrinthProjects={"chunky"},SpigetResources={"81534"}};
    Check(MaterialDockerImage.UI.MinecraftSpecRules.Validate(spec).Count==0,"valid spec has no problems");
    var env=MaterialDockerImage.UI.MinecraftSpecRules.Environment(spec).ToDictionary(e=>e.Key,e=>e.Value);
    Check(env["EULA"]=="TRUE"&&env["TYPE"]=="PAPER"&&env["MEMORY"]=="4G"&&env["DIFFICULTY"]=="hard"&&env["MODE"]=="creative"&&env["PVP"]=="false"&&env["ENABLE_WHITELIST"]=="true"&&env["SEED"]=="12345"&&env["LEVEL_TYPE"]=="minecraft:flat","properties map to itzg variables");
    Check(env["OPS"]=="Steve"&&env["WHITELIST"]=="Alex,Steve"&&env["MODRINTH_PROJECTS"]=="chunky"&&env["SPIGET_RESOURCES"]=="81534","player and add-on rows map to comma lists");
    Check(!env.ContainsKey("RCON_PASSWORD"),"environment never carries the RCON password");
    var noEula=spec.Clone(); noEula.EulaAccepted=false;
    Check(MaterialDockerImage.UI.MinecraftSpecRules.Environment(noEula).Single(e=>e.Key=="EULA").Value=="FALSE"&&MaterialDockerImage.UI.MinecraftSpecRules.Validate(noEula).Any(p=>p.StartsWith("EULA")),"EULA stays FALSE and blocks creation until the user accepts");
    var vanilla=spec.Clone(); vanilla.Flavour="VANILLA";
    Check(MaterialDockerImage.UI.MinecraftSpecRules.Validate(vanilla).Any(p=>p.Contains("Modrinth")),"Modrinth projects rejected for Vanilla");
    var secret="S3cret-Value-For-Test";
    var withSecret=spec.Clone(); withSecret.ExtraEnvironment.Add(new("RCON_PASSWORD",secret));
    var argv=MaterialDockerImage.UI.MinecraftSpecRules.RunArguments(withSecret,"C:/temp/mc.env");
    Check(!argv.Any(a=>a.Contains(secret))&&argv.Contains("--env-file"),"run arguments pass the password only through the env file");
    var preview=MaterialDockerImage.UI.MinecraftSpecRules.Preview(argv.Append("-e").Append("RCON_PASSWORD="+secret).ToList());
    Check(!preview.Contains(secret)&&preview.Contains("RCON_PASSWORD=•••")&&!preview.Contains("C:/temp/mc.env"),"command preview masks the password and the env file");
    Check(argv.Contains("127.0.0.1:25565:25565")&&argv.Contains("type=volume,source=mc-survival-data,target=/data")&&argv.Contains("material-docker-image.minecraft=survival")&&argv[^1]=="itzg/minecraft-server:java21","run arguments bind loopback, mount the labelled volume and pick the runtime image");
    var bedrock=new MaterialDockerImage.UI.MinecraftServerSpec{Name="pocket",Edition=MaterialDockerImage.UI.MinecraftEdition.Bedrock,EulaAccepted=true,Port=19132,LevelType="FLAT",SimulationDistance=4};
    var bargs=MaterialDockerImage.UI.MinecraftSpecRules.RunArguments(bedrock,null);
    Check(bargs.Contains("127.0.0.1:19132:19132/udp")&&bargs[^1]=="itzg/minecraft-bedrock-server:latest"&&MaterialDockerImage.UI.MinecraftSpecRules.Validate(bedrock).Count==0,"bedrock uses UDP 19132 and its own image");
    var round=MaterialDockerImage.UI.MinecraftSpecRules.FromEnvironment("survival",MaterialDockerImage.UI.MinecraftEdition.Java,MaterialDockerImage.UI.MinecraftSpecRules.Environment(spec).Select(e=>e.Key+"="+e.Value).Append("RCON_PASSWORD="+secret).Append("PATH=/usr/bin").Append("CUSTOM_FLAG=1"),"127.0.0.1:25565",out var pw);
    Check(pw==secret&&MaterialDockerImage.UI.MinecraftSpecRules.Diff(spec,round).SequenceEqual(new[]{"+ CUSTOM_FLAG=1"})&&round.ExtraEnvironment.Count==1&&round.ExtraEnvironment[0].Key=="CUSTOM_FLAG","environment round trips, keeps unknown options and separates the password");
    var changed=round.Clone(); changed.MaxPlayers=50;
    Check(MaterialDockerImage.UI.MinecraftSpecRules.Diff(round,changed).SequenceEqual(new[]{"~ MAX_PLAYERS: 10 → 50"}),"diff lists only the changed setting");

    var manifest=MaterialDockerImage.UI.MinecraftVersions.ParseManifest("{\"latest\":{\"release\":\"1.21.4\",\"snapshot\":\"25w02a\"},\"versions\":[{\"id\":\"25w02a\",\"type\":\"snapshot\",\"url\":\"x\",\"releaseTime\":\"2025-01-08T13:00:00+00:00\"},{\"id\":\"1.21.4\",\"type\":\"release\",\"releaseTime\":\"2024-12-03T10:12:57+00:00\"},{\"type\":\"release\"}]}");
    Check(manifest.LatestRelease=="1.21.4"&&manifest.LatestSnapshot=="25w02a"&&manifest.Versions.Count==2&&manifest.Versions[1].IsRelease&&manifest.Versions[1].DateText=="2024-12-03","version manifest parses latest ids, entries and dates");
    var threw=false; try{MaterialDockerImage.UI.MinecraftVersions.ParseManifest("{\"nope\":1}");}catch(FormatException){threw=true;}
    Check(threw,"non-manifest JSON is rejected");

    var area=MaterialDockerImage.UI.MinecraftChunkArea.Default;
    Check(area.WidthChunks==100&&area.DepthChunks==100&&area.ChunkCount==10000&&area.CentreBlockX==0&&area.CentreBlockZ==0&&area.RadiusBlocksX==800,"default area is 100 x 100 chunks, radius 800 blocks around 0, 0");
    var dragged=MaterialDockerImage.UI.MinecraftChunkArea.FromCorners(9,4,0,-5);
    Check(dragged.MinChunkX==0&&dragged.MaxChunkZ==4&&dragged.WidthChunks==10&&dragged.CentreBlockX==80&&dragged.CentreBlockZ==0&&dragged.RadiusBlocksX==80,"dragged rectangle normalises corners and computes centre and radius");
    var rect=MaterialDockerImage.UI.MinecraftChunkArea.FromCorners(0,0,3,1);
    Check(rect.ChunkyCommands("world").SequenceEqual(new[]{"chunky world world","chunky center 32 16","chunky shape rectangle","chunky radius 32 16","chunky start"}),"rectangular areas produce rectangle Chunky commands");
    var list=MaterialDockerImage.UI.MinecraftOutput.ParseList("There are 2 of a max of 20 players online: Steve, Alex");
    Check(list is {Online:2,Max:20}&&list.Value.Names.SequenceEqual(new[]{"Steve","Alex"})&&MaterialDockerImage.UI.MinecraftOutput.ParseList("There are 0 of a max of 10 players online: ")!.Value.Names.Count==0,"player list reply parses");
    var progress=MaterialDockerImage.UI.MinecraftOutput.ParseChunkyProgress("[12:00:00 INFO]: [Chunky] Task running for minecraft:overworld. Processed: 1,234 chunks (12.34%), ETA: 0:01:23, Rate: 100.0 cps, Current: 1, 2");
    Check(MaterialDockerImage.UI.MinecraftOutput.StripFormatting("\u001b[0;33m§aThere are 1\u001b[0m")=="There are 1","colour codes and ANSI sequences are stripped from replies");
    Check(progress is {Chunks:1234,Percent:12.34,Eta:"0:01:23"},"Chunky progress parses");
    var ls=MaterialDockerImage.UI.MinecraftOutput.ParseLs("total 12\ndrwxr-xr-x    5 1000     1000          4096 Jan  8 12:00 .\ndrwxr-xr-x    1 root     root          4096 Jan  8 12:00 ..\n-rw-r--r--    1 1000     1000          1234 Jan  8 12:01 server.properties\ndrwxr-xr-x    2 1000     1000          4096 Jan  8 12:01 my world\n");
    Check(ls.Count==2&&ls[0].Size==1234&&!ls[0].IsDirectory&&ls[1].IsDirectory&&ls[1].Name=="my world","ls -la output parses names with spaces");
}
// Advanced Dockerfile syntax: mounts, network, security, heredocs, COPY/ADD flags, global ARGs, directives.
{
    string RoundTrip(string text)=>MaterialDockerImage.UI.DockerfileModel.Render(MaterialDockerImage.UI.DockerfileModel.Parse(text).Document);
    bool NoRaw(string text)=>MaterialDockerImage.UI.DockerfileModel.Parse(text).Document.Stages.SelectMany(s=>s.Instructions).All(i=>i.Kind!=MaterialDockerImage.UI.InstructionKind.Raw);
    var mounts=new Dictionary<string,string>
    {
        ["cache"]="RUN --mount=type=cache,target=/root/.npm,id=npm,sharing=locked,from=deps,source=/cache,mode=0755,uid=1000,gid=1000,readonly npm ci",
        ["bind"]="RUN --mount=type=bind,target=/src,source=app,from=build,rw make",
        ["tmpfs"]="RUN --mount=type=tmpfs,target=/tmp,size=64m ls /tmp",
        ["secret"]="RUN --mount=type=secret,id=npmrc,target=/root/.npmrc,env=NPM_TOKEN,required=true,mode=0400,uid=1000,gid=1000 npm ci",
        ["ssh"]="RUN --mount=type=ssh,id=default,target=/run/ssh,required=true git clone git@github.com:org/repo.git",
    };
    foreach(var (type,line) in mounts)
    {
        var text="# syntax=docker/dockerfile:1\n\nFROM alpine:3.21\n"+line+"\n";
        var parsed=MaterialDockerImage.UI.DockerfileModel.Parse(text).Document;
        var run=parsed.Stages[0].Instructions[0];
        Check(run.Kind==MaterialDockerImage.UI.InstructionKind.Run&&run.Mounts.Count==1&&run.Mounts[0].Type==type,$"{type} mount parses into the model");
        Check(RoundTrip(text)==text,$"{type} mount round trips exactly");
        Check(MaterialDockerImage.UI.DockerfileModel.Validate(run).Count==0,$"{type} mount validates");
        Check(MaterialDockerImage.UI.DockerfileModel.Render(MaterialDockerImage.UI.DockerfileModel.Deserialize(MaterialDockerImage.UI.DockerfileModel.Serialize(parsed)))==text,$"{type} mount survives draft JSON");
    }
    Check(RoundTrip("FROM alpine\nRUN --mount=type=cache,dst=/x,ro true\n")=="FROM alpine\nRUN --mount=type=cache,target=/x,readonly true\n","mount aliases are normalised");
    Check(!NoRaw("FROM alpine\nRUN --mount=type=cache,target=/x,bogus=1 true\n"),"unknown mount option stays raw");
    var flags="FROM alpine\nRUN --network=none --security=insecure make\n";
    Check(RoundTrip(flags)==flags&&NoRaw(flags),"RUN network and security flags round trip");
    var heredoc="# syntax=docker/dockerfile:1\n\nFROM debian:bookworm-slim\nRUN --mount=type=cache,target=/var/cache/apt,sharing=locked <<EOF bash\nset -ex\napt-get update\nEOF\nRUN <<'SCRIPT'\n#!/usr/bin/env python3\nprint(\"$HOME\")\nSCRIPT\nCOPY --chmod=755 <<-EOT /usr/local/bin/start.sh\n\techo hello\nEOT\n";
    var hd=MaterialDockerImage.UI.DockerfileModel.Parse(heredoc).Document.Stages[0].Instructions;
    Check(hd.Count==3&&hd.All(i=>i.Heredoc)&&hd[0].Interpreter=="bash"&&hd[0].HeredocBody=="set -ex\napt-get update","RUN heredoc with interpreter is structured");
    Check(hd[1].HeredocQuoted&&hd[1].HeredocDelimiter=="SCRIPT"&&hd[1].HeredocBody.StartsWith("#!/usr/bin/env python3"),"quoted heredoc with shebang is structured");
    Check(hd[2].Kind==MaterialDockerImage.UI.InstructionKind.Copy&&hd[2].HeredocStripTabs&&hd[2].Destination=="/usr/local/bin/start.sh","COPY heredoc is structured");
    Check(RoundTrip(heredoc)==heredoc.Replace("<<'SCRIPT'","<<\"SCRIPT\""),"heredocs round trip (quoted delimiter normalised to double quotes)");
    Check(RoundTrip(RoundTrip(heredoc))==RoundTrip(heredoc),"heredoc rendering is stable");
    var python=RoundTrip("FROM python\nRUN python3 <<EOF\nprint(1)\nEOF\n");
    Check(python=="FROM python\nRUN <<EOF python3\nprint(1)\nEOF\n","command before the heredoc is kept as the interpreter");
    Check(!NoRaw("FROM alpine\nRUN <<A cat > a && <<B cat > b\n1\nA\n2\nB\n"),"multiple heredocs stay raw");
    var copy="FROM alpine\nCOPY --from=build --chown=app:app --link --parents --exclude=*.md --exclude=**/*.test ./src/**/*.go /out/\n";
    var cp=MaterialDockerImage.UI.DockerfileModel.Parse(copy).Document.Stages[0].Instructions[0];
    Check(cp.Parents&&cp.Excludes.Count==2&&cp.Link&&cp.From=="build","COPY --parents and --exclude parse");
    Check(RoundTrip(copy)==copy,"COPY --parents and --exclude round trip");
    var add="FROM alpine\nADD --checksum=be1f38e --keep-git-dir=true git@github.com:org/repo.git#v1.2.3 /src\nADD --checksum=sha256:24454f830cdb571e2c4ad15481119c43b3cafd48dd869a9b2945d1036d1dc68d https://example.com/tool.tar.gz /tmp/\n";
    var adds=MaterialDockerImage.UI.DockerfileModel.Parse(add).Document.Stages[0].Instructions;
    Check(adds[0].KeepGitDir&&adds[0].Checksum=="be1f38e"&&MaterialDockerImage.UI.AddSourceKinds.Of(adds[0].Sources[0])==MaterialDockerImage.UI.AddSourceKind.Git,"ADD Git source with checksum and keep-git-dir parses");
    Check(MaterialDockerImage.UI.AddSourceKinds.Of(adds[1].Sources[0])==MaterialDockerImage.UI.AddSourceKind.Url&&adds.All(a=>MaterialDockerImage.UI.DockerfileModel.Validate(a).Count==0),"ADD URL with checksum validates");
    Check(RoundTrip(add)==add,"ADD Git and URL with checksum round trip");
    Check(!NoRaw("FROM alpine\nADD --unpack=true a.tar.gz /x\n"),"undocumented-by-the-Creator ADD --unpack stays raw");
    var global="# syntax=docker/dockerfile:1\n# check=skip=JSONArgsRecommended;error=true\nARG TARGETPLATFORM\nARG BUILDPLATFORM\nARG GO_VERSION=1.24\n\nFROM --platform=$BUILDPLATFORM golang:${GO_VERSION}-alpine AS build\nARG TARGETOS\nARG TARGETARCH\nRUN GOOS=$TARGETOS GOARCH=$TARGETARCH go build -o /out/app .\n";
    var g=MaterialDockerImage.UI.DockerfileModel.Parse(global).Document;
    Check(g.Preamble.Count==3&&g.Preamble.All(i=>i.Kind==MaterialDockerImage.UI.InstructionKind.Arg)&&g.CheckDirective=="skip=JSONArgsRecommended;error=true"&&g.Stages[0].Platform=="$BUILDPLATFORM","global ARG preamble, check directive and BUILDPLATFORM parse");
    Check(RoundTrip(global)==global,"global ARG preamble round trips");
    var fresh=MaterialDockerImage.UI.DockerfileStarters.Go();
    Check(MaterialDockerImage.UI.DockerfilePlatform.AddGlobalArgs(fresh,"TARGETPLATFORM","BUILDPLATFORM").Count==2&&MaterialDockerImage.UI.DockerfilePlatform.AddGlobalArgs(fresh,"TARGETPLATFORM").Count==0,"platform ARG helper adds each ARG once");
    MaterialDockerImage.UI.DockerfilePlatform.UseBuildPlatform(fresh,0);
    Check(fresh.Stages[0].Platform=="$BUILDPLATFORM"&&fresh.Stages[0].Instructions.Take(2).All(i=>i.Kind==MaterialDockerImage.UI.InstructionKind.Arg),"cross-compilation helper sets BUILDPLATFORM and target ARGs");
    var onbuild=MaterialDockerImage.UI.DockerfileModel.Parse("FROM alpine\nONBUILD RUN --mount=type=cache,target=/root/.npm npm ci\n").Document.Stages[0].Instructions[0];
    Check(onbuild.Inner?.Kind==MaterialDockerImage.UI.InstructionKind.Run&&onbuild.Inner.Mounts.Count==1,"ONBUILD wraps a structured instruction");
    var maint=MaterialDockerImage.UI.DockerfileModel.Parse("FROM alpine:3.21\nMAINTAINER Jane <jane@example.com>\n").Document;
    var deprecated=MaterialDockerImage.UI.DockerfileLint.Check(maint).First(r=>r.Id=="DF114");
    deprecated.Fix!.Apply!(maint);
    Check(maint.Stages[0].Instructions[0].Kind==MaterialDockerImage.UI.InstructionKind.Label&&maint.Stages[0].Instructions[0].Pairs[0].Value=="Jane <jane@example.com>","MAINTAINER fix converts to an authors LABEL");
    var npm=MaterialDockerImage.UI.DockerfileModel.Parse("FROM node:22-alpine\nWORKDIR /app\nCOPY .npmrc package.json ./\nRUN npm ci\nRUN pip install flask\nRUN curl -fsSL https://example.com/install.sh | sh\nENV PATH=/opt/bin\n").Document;
    var advanced=MaterialDockerImage.UI.DockerfileLint.Check(npm).Select(r=>r.Id).ToHashSet();
    Check(new[]{"DF100","DF101","DF102","DF104","DF106","DF111"}.All(advanced.Contains),"advanced lint rules report cache, secret, pipe, pip and PATH findings");
    var cacheFix=MaterialDockerImage.UI.DockerfileLint.Check(npm).First(r=>r.Id=="DF101"&&r.Title.StartsWith("npm"));
    cacheFix.Fix!.Apply!(npm);
    Check(npm.Stages[0].Instructions.Any(i=>i.Mounts.Any(m=>m.Target=="/root/.npm")),"cache mount fix adds the npm cache");
    var command=MaterialDockerImage.UI.DockerfilePlatform.BuildCommand(MaterialDockerImage.UI.DockerfileModel.Parse("FROM alpine\nRUN --mount=type=secret,id=tok --security=insecure true\n").Document,"Dockerfile",".","demo:dev");
    Check(command.Command=="docker buildx build -f Dockerfile -t demo:dev --allow security.insecure --secret id=tok .","build command lists entitlements and secrets without values");
    var tokens=MaterialDockerImage.UI.DockerfileHighlighter.Tokenize(heredoc);
    Check(tokens.Sum(t=>t.Length)==heredoc.Length&&tokens.Any(t=>t.Kind==MaterialDockerImage.UI.DockerfileTokenKind.HeredocBody)&&tokens.Any(t=>t.Kind==MaterialDockerImage.UI.DockerfileTokenKind.Flag),"highlighter covers every character and finds flags and heredoc bodies");
}

// Render and parse line maps used for two-way selection between the preview and the cards.
{
    var doc=MaterialDockerImage.UI.DockerfileStarters.Node();
    doc.Preamble.Add(new MaterialDockerImage.UI.DockerInstruction{Kind=MaterialDockerImage.UI.InstructionKind.Arg,Name="NODE_VERSION",HasValue=true,Value="22"});
    var rendered=MaterialDockerImage.UI.DockerfileModel.RenderWithMap(doc);
    var lines=rendered.Text.Split('\n');
    Check(rendered.Text==MaterialDockerImage.UI.DockerfileModel.Render(doc),"render with map produces the same text");
    Check(rendered.Lines.All(s=>s.InstructionIndex<0?lines[s.StartLine].StartsWith("FROM "):s.StageIndex<0?lines[s.StartLine].StartsWith("ARG"):lines[s.StartLine].StartsWith(MaterialDockerImage.UI.DockerfileModel.RenderInstruction(doc.Stages[s.StageIndex].Instructions[s.InstructionIndex]).Split('\n')[0])),"every mapped span starts on its own rendered line");
    Check(MaterialDockerImage.UI.DockerfileModel.SpanAt(rendered.Lines,3)?.StageIndex==0&&MaterialDockerImage.UI.DockerfileModel.SpanAt(rendered.Lines,3)?.InstructionIndex==-1&&MaterialDockerImage.UI.DockerfileModel.SpanAt(rendered.Lines,2)==null,"line of the first FROM maps to stage 0");
    var reparsed=MaterialDockerImage.UI.DockerfileModel.Parse(rendered.Text);
    Check(reparsed.LineMap.Count==rendered.Lines.Count&&reparsed.LineMap.Zip(rendered.Lines).All(p=>p.First==p.Second),"parse line map equals the render line map");
    var multi=MaterialDockerImage.UI.DockerfileModel.Parse("FROM alpine\n\n# two\n# lines\nRUN a && \\\n    b\nRUN <<EOF\nx\nEOF\nBOGUS thing\n");
    Check(multi.LineMap.Select(s=>(s.StartLine,s.EndLine)).SequenceEqual(new[]{(0,0),(2,3),(4,5),(6,8),(9,9)}),"comments, continuations and heredocs map to their full line ranges");
    Check(multi.Warnings.Any(w=>MaterialDockerImage.UI.DockerfileParseResult.WarningLine(w)==10),"parse warnings carry their line number");
}

// .dockerignore matching follows Docker's pattern rules.
{
    var rules=MaterialDockerImage.UI.Dockerignore.Compile(new[]{"# comment","**/*.log","!keep.log","node_modules/","/build","docs/*.md","!docs/README.md","te?t.txt"});
    bool Ex(string p)=>MaterialDockerImage.UI.Dockerignore.IsExcluded(rules,p);
    Check(Ex("a.log")&&Ex("deep/nested/b.log"),"**/*.log matches at any depth including the root");
    Check(!Ex("keep.log")&&Ex("sub/keep.log"),"!keep.log re-includes only the root file");
    Check(Ex("node_modules")&&Ex("node_modules/pkg/index.js")&&!Ex("src/node_modules/x.js"),"node_modules/ matches the root folder and its contents only");
    Check(Ex("build")&&Ex("build/out.bin")&&!Ex("src/build"),"/build is anchored to the context root");
    Check(Ex("docs/a.md")&&!Ex("docs/README.md")&&!Ex("docs/sub/a.md"),"single star stays within one segment and exceptions win when later");
    Check(Ex("test.txt")&&Ex("text.txt")&&!Ex("teest.txt"),"? matches exactly one character");
    Check(MaterialDockerImage.UI.Dockerignore.Clean("./a//b/../c/")=="a/c","paths are cleaned like Docker");
    var root=Path.Combine(Path.GetTempPath(),"mdi-ignore-"+Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(root,"node_modules","pkg")); Directory.CreateDirectory(Path.Combine(root,"src"));
        File.WriteAllText(Path.Combine(root,"node_modules","pkg","index.js"),"x"); File.WriteAllText(Path.Combine(root,"src","app.js"),"x"); File.WriteAllText(Path.Combine(root,"debug.log"),"x"); File.WriteAllText(Path.Combine(root,"keep.log"),"x");
        var check=MaterialDockerImage.UI.Dockerignore.Evaluate(root,new[]{"**/*.log","!keep.log","node_modules/"});
        Check(check.Entries.Any(e=>e.Path=="node_modules"&&e.Excluded)&&check.Entries.Any(e=>e.Path=="src/app.js"&&!e.Excluded)&&check.Entries.Any(e=>e.Path=="debug.log"&&e.Excluded)&&check.Entries.Any(e=>e.Path=="keep.log"&&!e.Excluded),"context check reports included and excluded entries");
        Check(MaterialDockerImage.UI.Dockerignore.Evaluate(root,new string[0],2).Truncated,"context check is bounded");
    }
    finally { try { Directory.Delete(root,true); } catch(IOException) { } }
}

// Project detection on temporary folders.
{
    var node=Path.Combine(Path.GetTempPath(),"mdi-node-"+Guid.NewGuid().ToString("N"));
    var go=Path.Combine(Path.GetTempPath(),"mdi-go-"+Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(node);
        File.WriteAllText(Path.Combine(node,"package.json"),"{\"name\":\"@acme/Web-App\",\"main\":\"server.js\",\"scripts\":{\"start\":\"node server.js\",\"build\":\"tsc\"},\"engines\":{\"node\":\">=20.10\"},\"dependencies\":{\"express\":\"^4\"}}");
        File.WriteAllText(Path.Combine(node,"package-lock.json"),"{}");
        var d=MaterialDockerImage.UI.ProjectDetector.Detect(node);
        Check(d.Language=="node"&&d.PackageManager=="npm"&&d.Version=="20"&&d.HasStartScript&&d.HasBuildScript&&d.Framework=="Express"&&d.Name=="acme-web-app","Node.js project detected with npm lockfile, engines and scripts");
        Check(d.Findings.Any(f=>f.Evidence.Contains("package-lock.json")),"findings name their evidence files");
        var suggestion=MaterialDockerImage.UI.ProjectDetector.Suggest(d);
        var text=MaterialDockerImage.UI.DockerfileModel.Render(suggestion);
        Check(text.Contains("npm ci --omit=dev")&&text.Contains("source=package-lock.json")&&text.Contains("ARG NODE_VERSION=20")&&MaterialDockerImage.UI.DockerfileModel.Render(MaterialDockerImage.UI.DockerfileModel.Parse(text).Document)==text,"Node.js suggestion uses the lockfile and round trips");
        Check(DockerfileGraph.Parse(text).Stages.Count==suggestion.Stages.Count,"Node.js suggestion is readable by the canvas parser");
        Check(MaterialDockerImage.UI.ProjectDetector.ComposeFor(d,suggestion).Contains("\"3000:3000\"")&&suggestion.IgnorePatterns.Contains("node_modules"),"compose and .dockerignore are generated for the detection");
        Directory.CreateDirectory(Path.Combine(go,"cmd","api"));
        File.WriteAllText(Path.Combine(go,"go.mod"),"module github.com/acme/service\n\ngo 1.23.4\n");
        File.WriteAllText(Path.Combine(go,"cmd","api","main.go"),"package main");
        var g=MaterialDockerImage.UI.ProjectDetector.Detect(go);
        Check(g.Language=="go"&&g.Version=="1.23"&&g.BuildPath=="./cmd/api"&&g.Entry=="api","Go module detected with version and command package");
        var goText=MaterialDockerImage.UI.DockerfileModel.Render(MaterialDockerImage.UI.ProjectDetector.Suggest(g));
        Check(goText.Contains("golang:1.23-alpine")&&goText.Contains("./cmd/api")&&!goText.Contains("go.sum"),"Go suggestion uses the module version and skips a missing go.sum");
    }
    finally { foreach(var f in new[]{node,go}) try { Directory.Delete(f,true); } catch(IOException) { } }
}

// Minimal text patches keep the user's formatting when a card changes in place.
{
    var typed="# syntax=docker/dockerfile:1\r\n\r\n# Build stage\r\nFROM   golang:1.24-alpine   AS build\r\n\r\nWORKDIR /src\r\nRUN go mod download\r\n\r\n# Runtime\r\nFROM alpine:3.21\r\nCMD [\"/app\"]\r\n";
    var parsed=MaterialDockerImage.UI.DockerfileModel.Parse(typed);
    var state=MaterialDockerImage.UI.DockerfilePatch.FromParse(typed,parsed);
    var doc=parsed.Document;
    Check(MaterialDockerImage.UI.DockerfilePatch.TryPatch(state,doc)!.Text==typed,"an unchanged model leaves the typed text exactly as it was");
    doc.Stages[0].Instructions[1].Mounts.Add(new MaterialDockerImage.UI.RunMount{Type="cache",Target="/go/pkg/mod"});
    var patched=MaterialDockerImage.UI.DockerfilePatch.TryPatch(state,doc)!;
    Check(patched.Text==typed.Replace("RUN go mod download","RUN --mount=type=cache,target=/go/pkg/mod go mod download"),"a card edit replaces only its own line and keeps comments, spacing and CRLF");
    doc.Stages[0].Instructions[1].Command="go mod download && \\\n    go mod verify";
    var multi=MaterialDockerImage.UI.DockerfilePatch.TryPatch(patched,doc)!;
    Check(multi.Lines.Last().StartLine==patched.Lines.Last().StartLine+1&&multi.Text.Contains("# Runtime\r\nFROM alpine:3.21"),"a patch that adds lines shifts the following spans");
    doc.Stages[1].Instructions.Add(new MaterialDockerImage.UI.DockerInstruction{Kind=MaterialDockerImage.UI.InstructionKind.User,Value="10001"});
    Check(MaterialDockerImage.UI.DockerfilePatch.TryPatch(multi,doc)==null,"structural changes ask for a full render");
}

// BuildKit check names, skip directive, stage name checks, symbolic chmod and cache presets.
{
    var d=MaterialDockerImage.UI.DockerfileModel.Parse("# check=skip=JSONArgsRecommended\nFROM alpine:3.21 AS Build\nCMD echo hi\nFROM Build\nCOPY --from=Build --chmod=u=rwx,go=rx /a /b\n").Document;
    var results=MaterialDockerImage.UI.DockerfileLint.Check(d);
    Check(results.All(r=>r.Id!="DF031")&&MaterialDockerImage.UI.DockerfileLint.BuildKitName("DF031")=="JSONArgsRecommended","# check=skip hides the matching BuildKit check");
    Check(d.Stages[1].Instructions[0].Chmod=="u=rwx,go=rx"&&MaterialDockerImage.UI.DockerfileModel.Validate(d.Stages[1].Instructions[0]).Count==0,"symbolic chmod parses and validates");
    results.First(r=>r.Id=="DF121").Fix!.Apply!(d);
    Check(d.Stages[0].Name=="build"&&d.Stages[1].BaseImage=="build"&&d.Stages[1].Instructions[0].From=="build","StageNameCasing fix renames every reference");
    var mounts=new List<MaterialDockerImage.UI.RunMount>();
    Check(MaterialDockerImage.UI.RunMounts.AddPreset(mounts,"apt")==2&&mounts.All(m=>m.Sharing=="locked")&&MaterialDockerImage.UI.RunMounts.AddPreset(mounts,"apt")==0,"apt cache preset adds locked caches once");
    Check(MaterialDockerImage.UI.RunMounts.CachePresets.All(p=>p.Mounts.All(m=>MaterialDockerImage.UI.RunMounts.Validate(m).Count==0)),"every cache preset is valid");
}
Console.WriteLine($"UI state checks: {count} passed.");
