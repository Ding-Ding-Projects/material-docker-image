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
// Window-wide drop: an invalid file keeps the previously applied wording, a valid one supplies the celebration headline.
{
    var dropDir = Path.Combine(Path.GetTempPath(), "mdi-drop-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dropDir);
    try
    {
        var dropGood = Path.Combine(dropDir, "good.json"); var dropBad = Path.Combine(dropDir, "bad.json");
        File.WriteAllText(dropGood, "{\"version\":1,\"entries\":{\"private vocabulary\":\"Custom headline\",\"Build\":\"Make\"}}");
        File.WriteAllText(dropBad, "{\"version\":1,\"entries\":{\"Build\":\"x\",\"Build\":\"y\"}}");
        Check(MaterialDockerImage.Services.Loc.LoadVocabulary(dropGood).Success && MaterialDockerImage.Services.Loc.TryPersonal(MaterialDockerImage.UI.VocabularyDropRules.HeadlineKey, out var dropHeadline) && dropHeadline=="Custom headline", "dropped file supplies its own celebration headline");
        Check(!MaterialDockerImage.Services.Loc.LoadVocabulary(dropBad).Success && MaterialDockerImage.Services.Loc.OverrideCount==2, "refused drop leaves the applied wording unchanged");
        Check(MaterialDockerImage.UI.VocabularyDropRules.SinglePath(new[] { dropGood })==dropGood && MaterialDockerImage.UI.VocabularyDropRules.SinglePath(new[] { dropGood, dropBad })==null && MaterialDockerImage.UI.VocabularyDropRules.SinglePath("text")==null && MaterialDockerImage.UI.VocabularyDropRules.IsJson(dropGood) && !MaterialDockerImage.UI.VocabularyDropRules.IsJson("notes.txt"), "only a single dropped .json file is accepted");
        Check(MaterialDockerImage.UI.VocabularyDropRules.Headline()=="Custom headline", "celebration headline comes from the imported file");
        MaterialDockerImage.Services.Loc.ClearVocabulary();
        Check(!MaterialDockerImage.Services.Loc.TryPersonal(MaterialDockerImage.UI.VocabularyDropRules.HeadlineKey, out _) && MaterialDockerImage.Services.Loc.OverrideCount==0 && MaterialDockerImage.UI.VocabularyDropRules.Headline()=="Personal wording applied", "clearing restores the original wording and the neutral headline");
    }
    finally { Directory.Delete(dropDir, true); }
}
MaterialDockerImage.Services.Loc.Language="Cantonese";
Check(MaterialDockerImage.Services.Loc.T("3 of 5")=="3 / 5" && MaterialDockerImage.Services.Loc.T("A miniature of the window drawn with the current appearance.")=="A miniature of the window drawn with the current appearance." && MaterialDockerImage.Services.Loc.T("Runs in 30 s")=="Runs in 30 s", "short templates translate counts but never split ordinary sentences");
Check(MaterialDockerImage.Services.Loc.T("Show details")=="顯示詳情" && MaterialDockerImage.Services.Loc.T("1 selected of 95 matching")=="已揀 1 項（95 項符合）" && MaterialDockerImage.Services.Loc.T("95 of 95 shown")=="顯示 95 / 95" && MaterialDockerImage.Services.Loc.T("Ready")=="準備就緒", "table status, selection and compact detail labels translate");
MaterialDockerImage.Services.Loc.Language="English";
Check(MaterialDockerImage.Services.Loc.T("1 selected of 95 matching")=="1 selected of 95 matching", "English table selection line is unchanged");
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
// Regex builder: plain text by default, bounded .NET evaluation, explanation tree, library and catalogue guard.
{
    var ci=new RegexBuilderFlags(); var cs=ci with{IgnoreCase=false};
    var plain=new RegexSearchQuery("a.b",false,ci);
    Check(plain.Matches("xA.By")&&!plain.Matches("axb")&&plain.Engine=="Plain text","plain text is the default and treats . literally");
    Check(!new RegexSearchQuery("A.B",false,cs).Matches("a.b"),"plain text honours case sensitivity");
    Check(new RegexSearchQuery("",true,ci).Matches("anything")&&new RegexSearchQuery("a.b",true,ci).Matches("axb"),"empty query matches all and regex mode is an explicit opt-in");
    var bad=RegexBuilderEngine.Compile("ab(cd",ci);
    Check(!bad.Valid&&bad.ErrorIndex==5&&bad.Error!.Contains("position 5"),"invalid pattern reports the error position: "+bad.Error);
    var bad2=RegexBuilderEngine.Compile("a[b",ci);
    Check(!bad2.Valid&&bad2.ErrorIndex>=1,"unterminated set is invalid with a position");
    var badQuery=new RegexSearchQuery("(",true,ci);
    Check(!badQuery.IsValid&&!badQuery.Matches("(")&&badQuery.Error!=null,"invalid pattern never throws and matches nothing");
    Check(RegexBuilderEngine.Compile(@"\d+",ci).Engine==RegexBuilderEngine.NonBacktrackingEngine,"NonBacktracking engine chosen when supported");
    var look=RegexBuilderEngine.Compile(@"(?<=a)b",ci);
    Check(look.Valid&&look.Engine==RegexBuilderEngine.BacktrackingEngine&&look.FallbackReason!=null&&look.Regex!.MatchTimeout==TimeSpan.FromMilliseconds(250),"lookbehind falls back to backtracking with a 250 ms timeout");
    Check(RegexBuilderEngine.Compile(@"(a)\1",ci).Engine==RegexBuilderEngine.BacktrackingEngine,"backreference falls back to backtracking");
    Check(new RegexSearchQuery("HELLO",true,ci).Matches("say hello")&&!new RegexSearchQuery("HELLO",true,cs).Matches("say hello"),"IgnoreCase flag");
    Check(RegexBuilderEngine.Evaluate("^b$",ci with{Multiline=true},"a\nb\nc").Matches.Count==1&&RegexBuilderEngine.Evaluate("^b$",ci,"a\nb\nc").Matches.Count==0,"Multiline flag changes ^ and $");
    Check(RegexBuilderEngine.Evaluate("a.c",ci with{Singleline=true},"a\nc").Matches.Count==1&&RegexBuilderEngine.Evaluate("a.c",ci,"a\nc").Matches.Count==0,"Singleline flag lets . match newline");
    Check(RegexBuilderEngine.Evaluate("a b # comment",ci with{IgnorePatternWhitespace=true},"ab").Matches.Count==1,"IgnorePatternWhitespace flag ignores spaces and comments");
    var explicitCap=RegexBuilderEngine.Evaluate("(a)(?<n>b)",ci with{ExplicitCapture=true},"ab");
    Check(explicitCap.Matches[0].Groups.Count==1&&explicitCap.Matches[0].Groups[0].Name=="n","ExplicitCapture flag captures only named groups");
    var prev=System.Globalization.CultureInfo.CurrentCulture;
    try{System.Globalization.CultureInfo.CurrentCulture=new("tr-TR");
        Check(new RegexSearchQuery("i",true,ci with{CultureInvariant=true}).Matches("I"),"CultureInvariant flag uses invariant case rules");}
    finally{System.Globalization.CultureInfo.CurrentCulture=prev;}
    var uni=RegexBuilderEngine.Evaluate(@"\p{Lu}\p{Ll}+",cs,"東京 Zürich Ωmega 😀x");
    Check(uni.Matches.Select(m=>m.Value).SequenceEqual(new[]{"Zürich","Ωmega"}),"Unicode categories match non-ASCII letters");
    Check(RegexBuilderEngine.Evaluate("x",ci,"😀x").Matches[0].Index==2,"indices are UTF-16 positions after a surrogate pair");
    var caps=RegexBuilderEngine.Evaluate(@"(?<year>\d{4})-(\d{2})",ci,"on 2026-11 and 2027-01");
    Check(caps.Matches.Count==2&&caps.Matches[0].Groups.Any(g=>g.Name=="year"&&g.Value=="2026"&&g.Index==3)&&caps.Matches[1].Groups.Any(g=>g.Number==1&&g.Value=="01"),"named and numbered captures with positions");
    var zero=RegexBuilderEngine.Evaluate(@"\b",ci,"ab cd");
    Check(zero.Matches.Count==4&&zero.Matches.All(m=>m.ZeroWidth)&&zero.Summary().Contains("zero-width"),"zero-width matches are counted and labelled");
    var none=RegexBuilderEngine.Evaluate("xyz",ci,"abc");
    Check(none.Valid&&none.Matches.Count==0&&none.Summary()=="No match in the sample.","no match state");
    var evil=new string('a',40)+"!";
    var sw=System.Diagnostics.Stopwatch.StartNew();
    var linear=RegexBuilderEngine.Evaluate("(a+)+$",ci,evil);
    Check(linear.Engine==RegexBuilderEngine.NonBacktrackingEngine&&linear.Matches.Count==0&&!linear.TimedOut&&sw.ElapsedMilliseconds<2000,"catastrophic pattern runs in linear time on NonBacktracking");
    sw.Restart();
    var timed=RegexBuilderEngine.Evaluate(@"^(a+)+\1$",ci,new string('a',36)+"!");
    Check(timed.Engine==RegexBuilderEngine.BacktrackingEngine&&timed.TimedOut&&sw.ElapsedMilliseconds<3000&&timed.Summary().Contains("250 ms"),"adversarial backtracking pattern stops at the timeout ("+sw.ElapsedMilliseconds+" ms)");
    Check(!new RegexSearchQuery(@"^(a+)+\1$",true,ci).Matches(new string('a',36)+"!"),"filter predicate treats a timeout as no match");
    var big=new string('x',70000)+"é";
    var bounded=RegexBuilderEngine.Evaluate("x",ci,big);
    Check(bounded.SampleTruncated&&System.Text.Encoding.UTF8.GetByteCount(bounded.Sample)<=65536&&bounded.Matches.Count==2000&&bounded.MatchesTruncated,"sample capped at 64 KiB and highlights capped at 2000");
    Check(RegexBuilderEngine.BoundSample(new string('é',40000),out var cut).Length==32768&&cut,"sample bound counts UTF-8 bytes");
    Check(!RegexBuilderEngine.Compile(new string('a',5000),ci).Valid,"overlong pattern refused");
    using(var cts=new CancellationTokenSource()){cts.Cancel();var c=RegexBuilderEngine.Evaluate("a",ci,"aaa",cts.Token);Check(c.Cancelled&&c.Matches.Count==0,"cancelled evaluation stops before matching");}
    using(var cts=new CancellationTokenSource()){var task=Task.Run(()=>RegexBuilderEngine.Evaluate(@"\w",ci,new string('a',60000),cts.Token,int.MaxValue));cts.Cancel();var r=task.Result;Check(r.Cancelled||r.Matches.Count==60000,"evaluation off the calling thread observes cancellation");}
    var rep=RegexBuilderEngine.Replace(@"(?<k>\w+)=(\w+)",ci,"a=1 b=2","$1:${k} $9");
    Check(rep.Valid&&rep.Output=="1:a $9 2:b $9"&&rep.Replacements==2&&rep.Warnings.Count==1,"replace preview with numbered and named substitutions warns about unknown groups");
    foreach(var literal in new[]{"a.b*c","C:\\path\\(x)","[tag]{1} $5 ^#","日本 😀 | ?"})
        Check(RegexBuilderLibrary.Unescape(RegexBuilderLibrary.Literal(literal))==literal&&new RegexSearchQuery(RegexBuilderLibrary.Literal(literal),true,cs with{IgnorePatternWhitespace=true}).Matches("<"+literal+">"),"escaping round trip: "+literal);
    Check(RegexBuilderEngine.ToCSharpVerbatim("a\"b\\d")=="@\"a\"\"b\\d\"","copy as C# verbatim string doubles quotes");
    using(var json=System.Text.Json.JsonDocument.Parse(RegexBuilderEngine.ExportJson(@"\d+",ci with{Multiline=true},"x1")))
        Check(json.RootElement.GetProperty("pattern").GetString()==@"\d+"&&json.RootElement.GetProperty("flags").GetProperty("multiline").GetBoolean()&&json.RootElement.GetProperty("sample").GetString()=="x1"&&json.RootElement.GetProperty("engine").GetString()=="NonBacktracking","export JSON has pattern, flags, engine and sample");
    Check(RegexBuilderLibrary.CharacterSet(new[]{"a-z","\\d","]","^","-"},true)=="[^a-z\\d\\]\\^\\-]"&&new RegexSearchQuery(RegexBuilderLibrary.CharacterSet(new[]{"a-z","]"},false),true,cs).Matches("]"),"character set editor escapes members");
    Reject(()=>RegexBuilderLibrary.CharacterSet(new[]{"z-a"},false),"reverse range refused");
    Check(RegexBuilderLibrary.Quantifier(0,1,false)=="?"&&RegexBuilderLibrary.Quantifier(0,null,true)=="*?"&&RegexBuilderLibrary.Quantifier(3,3,false)=="{3}"&&RegexBuilderLibrary.Quantifier(2,null,false)=="{2,}"&&RegexBuilderLibrary.Quantifier(2,5,true)=="{2,5}?","quantifier composer");
    Reject(()=>RegexBuilderLibrary.Quantifier(5,2,false),"quantifier max below min refused");
    Check(RegexBuilderLibrary.NamedGroup("year",@"\d{4}")==@"(?<year>\d{4})"&&RegexBuilderLibrary.Backreference(2)==@"\2"&&RegexBuilderLibrary.Backreference("year")==@"\k<year>"&&RegexBuilderLibrary.Alternation(new[]{"a.b","c"})==@"(?:a\.b|c)"&&RegexBuilderLibrary.UnicodeCategory("Lu",true)==@"\P{Lu}","group, backreference, alternation and category composers");
    Reject(()=>RegexBuilderLibrary.NamedGroup("1bad","x"),"invalid group name refused");
    foreach(var t in RegexBuilderLibrary.Tokens) Check(RegexBuilderEngine.Validate(t.Family=="Quantifiers"?"a"+t.Prefix:t.Prefix+"a"+t.Suffix,ci).Valid,"token inserts a valid pattern: "+t.Label);
    foreach(var s in RegexBuilderLibrary.Snippets)
    {
        var e=RegexBuilderEngine.Evaluate(s.Pattern,s.Flags??ci,s.Example);
        Check(e.Valid&&e.Matches.Count>0&&e.Matches[0].Value==s.ExpectedMatch,"library "+s.Id+" matches its example ("+(e.Matches.Count>0?e.Matches[0].Value:e.Error)+")");
        Check(RegexBuilderEngine.Evaluate(s.Pattern,s.Flags??ci,s.NonMatch).Matches.Count==0,"library "+s.Id+" rejects its counter-example");
    }
    Check(new RegexSearchQuery(RegexBuilderLibrary.Snippets.First(s=>s.Id=="ipv6").Pattern,true,ci).Matches("::1"),"IPv6 snippet matches loopback");
    // Explanation tree: every token family is described.
    var nodes=RegexExplainer.Flatten(RegexExplainer.Explain(@"^(?<y>\d{4})-(\d\d)?|[^a-c\s]+?\b(?=x)(?<!y)(?:z)\k<y>\1.\p{L}$",ci)).ToList();
    var families=nodes.Select(n=>n.Node.Family).ToHashSet();
    foreach(var fam in new[]{RegexTokenFamily.Anchor,RegexTokenFamily.Group,RegexTokenFamily.Quantifier,RegexTokenFamily.CharacterClass,RegexTokenFamily.Alternation,RegexTokenFamily.Branch,RegexTokenFamily.Lookaround,RegexTokenFamily.Backreference,RegexTokenFamily.Any,RegexTokenFamily.UnicodeCategory,RegexTokenFamily.Literal})
        Check(families.Contains(fam),"explanation covers "+fam);
    Check(nodes.All(n=>n.Node.Description.Length>0)&&!families.Contains(RegexTokenFamily.Error),"every explained token has a description and no false errors");
    Check(nodes.Any(n=>n.Node.Description.StartsWith("Named capturing group 'y'")&&n.Node.Description.Contains("group number 2")),"named group numbered after unnamed groups like .NET");
    Check(nodes.Any(n=>n.Node.Family==RegexTokenFamily.Quantifier&&n.Node.Description.Contains("lazy")),"lazy quantifier explained");
    var lit=RegexExplainer.Explain(@"abc\.d",ci);
    Check(lit.Count==1&&lit[0].Family==RegexTokenFamily.Literal&&lit[0].Description=="The exact text \"abc.d\".","adjacent literals and escaped characters merge");
    var q=RegexExplainer.Explain("ab+",ci);
    Check(q.Count==2&&q[1].Family==RegexTokenFamily.Quantifier&&q[1].Children[0].Text=="b","quantifier applies to the last character only");
    Check(RegexExplainer.Flatten(RegexExplainer.Explain("(?i)a(?#note)",ci)).Any(n=>n.Node.Family==RegexTokenFamily.InlineOptions)&&RegexExplainer.Flatten(RegexExplainer.Explain("(?#note)",ci)).Any(n=>n.Node.Family==RegexTokenFamily.Comment),"inline options and comments explained");
    foreach(var broken in new[]{"(ab","a)","[ab","*a",@"a\","a{3,1}",@"\q","(?<1x>a)"})
        Check(RegexExplainer.Flatten(RegexExplainer.Explain(broken,ci)).Any(n=>n.Node.Family==RegexTokenFamily.Error)&&!RegexBuilderEngine.Validate(broken,ci).Valid,"explanation flags malformed "+broken);
    var multi=RegexExplainer.Explain("^.$",ci with{Multiline=true,Singleline=true});
    Check(multi[0].Description.Contains("line")&&multi[1].Description.Contains("including a newline"),"explanation reflects Multiline and Singleline");
    // Saved patterns (explicit save only), recent list, independent field state.
    var regexStore=Path.Combine(Path.GetTempPath(),"mdi-regex-"+Guid.NewGuid().ToString("N")+".json");
    try
    {
        Check(RegexPatternStore.Load(regexStore).Count==0,"no saved patterns file is an empty library");
        var lib=RegexPatternStore.Add(new List<RegexSavedPattern>(),new("Digits",@"\d+",ci with{Multiline=true},DateTimeOffset.UnixEpoch));
        lib=RegexPatternStore.Add(lib,new("digits",@"\d{2}",ci,DateTimeOffset.UnixEpoch));
        RegexPatternStore.Save(regexStore,lib);
        var back=RegexPatternStore.Load(regexStore);
        Check(back.Count==1&&back[0].Pattern==@"\d{2}","saved pattern round trip replaces same name");
        Reject(()=>RegexPatternStore.Add(back,new("bad","(",ci,DateTimeOffset.UnixEpoch)),"invalid pattern cannot be saved");
        var full=Enumerable.Range(0,200).Select(i=>new RegexSavedPattern("p"+i,"a",ci,DateTimeOffset.UnixEpoch)).ToList();
        Reject(()=>RegexPatternStore.Add(full,new("p999","a",ci,DateTimeOffset.UnixEpoch)),"library bounded to 200 entries");
        Check(RegexPatternStore.Remove(back,"DIGITS").Count==0,"saved pattern removable");
        File.WriteAllText(regexStore,"{not json");
        try{RegexPatternStore.Load(regexStore);throw new Exception("Expected refusal: corrupt saved patterns");}catch(InvalidDataException){count++;Console.WriteLine("PASS corrupt saved patterns file refused");}
    }
    finally{File.Delete(regexStore);}
    var a=new RegexFieldState();var b=new RegexFieldState();
    a.Recent.Add(@"\d",ci);a.Recent.Add("(",ci);a.Sample="x";a.Flags=a.Flags with{Multiline=true};
    Check(a.Recent.Items.Count==1&&b.Recent.Items.Count==0&&b.Sample==""&&!b.Flags.Multiline,"field state and recent list are independent per field and skip invalid patterns");
    for(var i=0;i<20;i++)a.Recent.Add("p"+i,ci);
    Check(a.Recent.Items.Count==RegexRecentPatterns.Capacity&&a.Recent.Items[0]=="p19","recent list is bounded, newest first");
    // Bulk tab close: one predicate, preview, guards, pinned and unsaved protection.
    var tabList=new[]{new TabCloseCandidate(0,"Workspace · Build",false,true,false),new TabCloseCandidate(1,"Images",true,false,false),new TabCloseCandidate(2,"Image compare",false,false,false),new TabCloseCandidate(3,"Containers",false,false,false),new TabCloseCandidate(4,"Images draft",false,false,true)};
    var closeIn=TabCloseSelector.Select(tabList,new RegexSearchQuery("image",false,ci),true);
    Check(closeIn.Valid&&closeIn.ToClose.Select(t=>t.Index).SequenceEqual(new[]{2})&&closeIn.Kept.Count==2,"close containing keeps pinned and unsaved tabs");
    Check(TabCloseSelector.Select(tabList,new RegexSearchQuery("image",false,ci),true,true).ToClose.Count==2,"pinned tabs close only when included");
    var closeOut=TabCloseSelector.Select(tabList,new RegexSearchQuery("^Image",true,ci),false);
    Check(closeOut.ToClose.Select(t=>t.Index).SequenceEqual(new[]{3})&&closeOut.Kept.Any(k=>k.Tab.Index==0),"close not containing uses the same predicate and never closes the workspace tab");
    Check(!TabCloseSelector.Select(tabList,new RegexSearchQuery("",false,ci),true).Valid&&!TabCloseSelector.Select(tabList,new RegexSearchQuery("(",true,ci),false).Valid,"empty and invalid queries close nothing");
    // Completeness guard: every search field in the app source is catalogued and built through SearchFilter.
    var dir=new DirectoryInfo(AppContext.BaseDirectory);
    while(dir!=null&&!Directory.Exists(Path.Combine(dir.FullName,"src","MaterialDockerImage"))) dir=dir.Parent;
    var appSource=Path.Combine(dir!.FullName,"src","MaterialDockerImage");
    var scan=SearchSurfaceCatalogue.Scan(appSource);
    var problems=SearchSurfaceCatalogue.Verify(scan);
    foreach(var problem in problems) Console.WriteLine("CATALOGUE "+problem);
    Check(problems.Count==0&&scan.Count==SearchSurfaceCatalogue.All.Count,$"search surface catalogue complete ({SearchSurfaceCatalogue.All.Count} surfaces)");
    Check(scan.Any(s=>s.File.StartsWith("Pages/"))&&scan.Any(s=>s.File.StartsWith("Shell/")),"guard scans Pages and Shell");
    var fakeRoot=Path.Combine(Path.GetTempPath(),"mdi-guard-"+Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(fakeRoot,"Pages"));
        File.WriteAllText(Path.Combine(fakeRoot,"Pages","New.cs"),"var s = new SearchFilter(\"Search widgets\", 200);\nvar t = new TextBox { Style = (Style)FindResource(\"Field.Search\") };\nvar u = Ui.Input(\"\", \"Filter rows\");");
        var fake=SearchSurfaceCatalogue.Verify(SearchSurfaceCatalogue.Scan(fakeRoot));
        Check(fake.Count(p=>p.StartsWith("Pages/New.cs"))==3,"guard fails for an unregistered search field and raw search text boxes");
    }
    finally{Directory.Delete(fakeRoot,true);}
}

// ---------------------------------------------------------------- Suggestions (Dockerfile and Compose)
{
    SuggestionResult At(string marked, SuggestionSources? src = null) { var c = marked.IndexOf('|'); return DockerfileCompletion.Complete(marked.Remove(c, 1), c, src); }
    SuggestionResult ComposeAt(string marked, SuggestionSources? src = null) { var c = marked.IndexOf('|'); return ComposeCompletion.Complete(marked.Remove(c, 1), c, src); }
    EditorValueSpan? KindAt(string marked) { var c = marked.IndexOf('|'); return DockerfileValueKinds.Detect(marked.Remove(c, 1), c); }
    EditorValueSpan? ComposeKindAt(string marked) { var c = marked.IndexOf('|'); return ComposeValueKinds.Detect(marked.Remove(c, 1), c); }
    bool Has(SuggestionResult r, string label) => r.Items.Any(i => i.Label == label);
    IEnumerable<string> Labels(SuggestionResult r) => r.Items.Where(i => i.Kind != SuggestionKind.Picker).Select(i => i.Label);
    var images = new SuggestionSources(new[] { new SuggestionLocalImage("myapp", "1.2", "sha256:" + new string('a', 64), "sha256:" + new string('b', 64), "120MB"), new SuggestionLocalImage("myapp", "dev", "<none>", "sha256:c", "1MB") }, new[] { "src/", "src/main.go", "go.mod", "README.md" });

    var start = At("|");
    Check(DockerfileCompletionData.Instructions.Length == 18 && DockerfileCompletionData.Instructions.All(i => Has(start, i.Name)), "start of line offers all 18 instructions");
    Check(Has(start, "# syntax=") && Has(start, "RUN heredoc"), "start of file offers parser directives and snippets");
    Check(!Has(At("FROM alpine\n|"), "# syntax="), "directives are not offered after the first instruction");
    var fuzzy = SuggestionFuzzy.Filter(At("CO|").Items, "CO");
    Check(fuzzy[0].Item.Label == "COPY" && fuzzy[0].Positions.SequenceEqual(new[] { 0, 1 }), "fuzzy prefix ranks COPY first and reports bold positions");
    Check(SuggestionFuzzy.TryMatch("HEALTHCHECK", "hck", out var pos, out _) && pos.Length == 3 && !SuggestionFuzzy.TryMatch("RUN", "un", out _, out _), "fuzzy matching is subsequence anchored at the first character");
    Check(SuggestionFuzzy.Filter(At("CO|").Items, "CO").Select(m => m.Item.Label).Contains("COPY heredoc"), "snippets are filtered with instructions");
    var flags = At("COPY --|");
    Check(Has(flags, "--from=") && Has(flags, "--chmod=") && Has(flags, "--link") && Has(flags, "--parents") && Has(flags, "--exclude="), "COPY flags after the keyword");
    Check(!Has(At("COPY --link --|"), "--link") && Has(At("COPY --exclude=a --|"), "--exclude="), "used flags are hidden except repeatable ones");
    Check(Has(At("ADD --|"), "--checksum=") && Has(At("ADD --|"), "--keep-git-dir") && Has(At("HEALTHCHECK --|"), "--start-interval=") && Has(At("RUN --|"), "--network=") && Has(At("FROM --|"), "--platform="), "flags per instruction");
    var mount = At("RUN --mount=|");
    Check(mount.Items[0].Label == "type=" && Has(mount, "target="), "mount offers type= first then bind keys");
    Check(Labels(At("RUN --mount=type=|")).SequenceEqual(new[] { "bind", "cache", "tmpfs", "secret", "ssh" }), "mount type values");
    var cacheKeys = At("RUN --mount=type=cache,|");
    Check(Has(cacheKeys, "sharing=") && Has(cacheKeys, "uid=") && !Has(cacheKeys, "type=") && !Has(cacheKeys, "env="), "cache mount keys follow the type");
    Check(Has(At("RUN --mount=type=secret,|"), "env=") && Has(At("RUN --mount=type=secret,|"), "required="), "secret mount keys");
    Check(Labels(At("RUN --mount=type=cache,sharing=|")).SequenceEqual(new[] { "shared", "private", "locked" }), "sharing values");
    var stages = "FROM golang:1.23 AS build\nRUN go build\nFROM alpine AS test\nFROM scratch\nCOPY --from=|";
    var fromItems = At(stages, images);
    Check(Has(fromItems, "build") && Has(fromItems, "test") && Has(fromItems, "myapp:1.2"), "--from offers earlier stages and local images");
    Check(Has(At("FROM golang AS build\nFROM |"), "build") && Has(At("FROM |"), "alpine") && At("FROM |").Items.First(i => i.Label == "alpine").Detail == "Popular official images", "FROM offers stages and the labelled curated image list");
    var tags = At("FROM myapp:|", images);
    Check(Labels(tags).SequenceEqual(new[] { "1.2", "dev" }) && tags.Start == "FROM myapp:".Length, "tags after the colon come from local images");
    Check(Has(At("FROM alpine |"), "AS"), "AS is offered after the image");
    var scoped = "ARG VERSION=1\nARG BASE=alpine\nFROM ${|";
    Check(Has(At(scoped), "VERSION") && Has(At(scoped), "TARGETPLATFORM") && At(scoped).Items.First(i => i.Label == "VERSION").Insert == "VERSION}", "global ARGs and platform ARGs in FROM, closing brace added");
    var stageScope = "ARG GLOBAL=1\nFROM alpine AS one\nARG ONE\nENV HOME_DIR=/app\nRUN echo $|\nFROM alpine AS two\nARG TWO\nRUN echo $X";
    var vars = At(stageScope);
    Check(vars.Items.First(i => i.Label == "ONE").Detail == "ARG in this stage" && Has(vars, "HOME_DIR") && !Has(vars, "TWO") && vars.Items.First(i => i.Label == "GLOBAL").Detail.StartsWith("Declare ARG"), "stage scoping of ARG and ENV with global ARG redeclare hint");
    var inherit = "FROM alpine AS base\nENV BASE_ENV=1\nFROM base\nRUN echo ${|}";
    Check(Has(At(inherit), "BASE_ENV") && At(inherit).Items.First(i => i.Label == "BASE_ENV").Insert == "BASE_ENV", "ENV inherited from a parent stage; existing brace not duplicated");
    Check(At("FROM alpine\nRUN echo \\$|").Items.Count == 0 || !Has(At("FROM alpine\nRUN echo \\$|"), "HTTP_PROXY"), "escaped dollar does not complete variables");
    var paths = At("FROM alpine\nCOPY src/|", images);
    Check(Labels(paths).SequenceEqual(new[] { "main.go" }) && paths.Items.First(i => i.Kind != SuggestionKind.Picker).Insert == "src/main.go", "COPY sources come from the context listing, one folder level at a time");
    Check(Has(At("FROM alpine\nCOPY |", images), "src/") && Has(At("FROM alpine\nCOPY |", images), "--from="), "COPY offers folders and flags right after the keyword");
    Check(!At("FROM alpine\nCOPY --from=build |", images).Items.Any(i => i.Kind is SuggestionKind.Path or SuggestionKind.Picker), "no context paths when copying from a stage");
    Check(Has(At("EXPOSE |"), "8080") && Labels(At("EXPOSE 53/|")).SequenceEqual(new[] { "tcp", "udp" }), "EXPOSE ports and protocols");
    Check(Has(At("STOPSIGNAL |"), "SIGTERM") && Has(At("SHELL |"), "[\"/bin/bash\", \"-o\", \"pipefail\", \"-c\"]"), "STOPSIGNAL and SHELL values");
    Check(Has(At("FROM alpine\nRUN adduser -D appuser\nUSER |"), "appuser") && Has(At("USER |"), "root"), "USER offers users created in the stage");
    Check(At("COPY --from=x |").Signature.StartsWith("COPY [--from=") && DockerfileCompletion.SignatureAt("HEALTHCHECK --interval=5s ", 26).StartsWith("HEALTHCHECK"), "signature help shows the instruction form");
    Check(DockerfileCompletion.Hover("FROM alpine\nCOPY --chmod=755 a b", 19)?.Label == "--chmod" && DockerfileCompletion.Hover("FROM alpine", 1)?.Label == "FROM", "hover documents keywords and flags");
    Check(DockerfileCompletion.Hover("RUN --mount=type=cache,sharing=locked x", 25)?.Label == "sharing", "hover documents mount options");
    Check(At("# syntax=|").Items.Any(i => i.Label == "docker/dockerfile:1") && Has(At("# |"), "escape="), "directive names and values");

    var noHeader = At("FROM alpine\nCOPY --|");
    Check(noHeader.Items.First(i => i.Label == "--link").NeedsSyntaxUpgrade && noHeader.Items.First(i => i.Label == "--link").Badge == "1.4+" && !noHeader.Items.First(i => i.Label == "--from=").NeedsSyntaxUpgrade, "versioned flags carry a badge without a syntax header");
    var old = At("# syntax=docker/dockerfile:1.4\nFROM alpine\nCOPY --|");
    Check(!old.Items.First(i => i.Label == "--link").NeedsSyntaxUpgrade && old.Items.First(i => i.Label == "--parents").NeedsSyntaxUpgrade, "badge only when the declared syntax is older");
    Check(!At("# syntax=docker/dockerfile:1.7-labs\nFROM alpine\nCOPY --|").Items.First(i => i.Label == "--parents").NeedsSyntaxUpgrade && !At("# syntax=docker/dockerfile:1\nCOPY --|").Items.Any(i => i.NeedsSyntaxUpgrade), "labs and floating 1 satisfy newer flags");
    var insertEdit = DockerfileCompletion.SyntaxUpgradeEdit("FROM alpine\n")!.Value;
    Check(insertEdit.Start == 0 && insertEdit.Length == 0 && insertEdit.Insert == "# syntax=docker/dockerfile:1\n", "syntax header is inserted when missing");
    var upgradeText = "# syntax=docker/dockerfile:1.4\r\nFROM alpine\r\n";
    var up = DockerfileCompletion.SyntaxUpgradeEdit(upgradeText)!.Value;
    Check(upgradeText.Remove(up.Start, up.Length).Insert(up.Start, up.Insert) == "# syntax=docker/dockerfile:1\r\nFROM alpine\r\n" && DockerfileCompletion.SyntaxUpgradeEdit("# syntax=docker/dockerfile:1\nFROM a") == null, "old syntax header is upgraded in place, current one left alone");
    var snippet = At("|").Items.First(i => i.Label == "RUN heredoc");
    var applied = SuggestionText.Apply("  ", new SuggestionResult(2, 0, "", new[] { snippet }), snippet);
    Check(applied.Insert.Contains("\n  set -eux") && !applied.Insert.Contains(SuggestionText.CaretMark) && applied.CaretAfter > 2, "snippets re-indent and place the caret");

    Check(KindAt("FROM alpine\nCOPY --chmod=7|55 a b")?.Kind == EditorValueKind.Chmod && KindAt("COPY --chmod=755 a b".Insert(13, "|"))!.SymbolicAllowed, "chmod value kind");
    Check(KindAt("FROM alpine\nCOPY sr|c/ /app/")?.Kind == EditorValueKind.Path && KindAt("FROM alpine\nCOPY src/ /ap|p/") == null, "COPY source is a path, destination is not");
    Check(KindAt("RUN --mount=type=bind,source=./x|,target=/x make")?.Kind == EditorValueKind.Path && KindAt("RUN --mount=type=cache,sharing=lo|cked x")?.Kind == EditorValueKind.Choice, "mount source path and sharing choice");
    Check(KindAt("RUN --mount=type=cache,mode=07|55,target=/c x")?.Kind == EditorValueKind.Chmod && KindAt("RUN --mount=type=cache,uid=10|00,target=/c x")?.Kind == EditorValueKind.User, "mount mode and uid kinds");
    Check(KindAt("EXPOSE 80|80")?.Kind == EditorValueKind.Port && KindAt("HEALTHCHECK --interval=3|0s CMD true")?.Kind == EditorValueKind.Duration, "port and duration kinds");
    Check(KindAt("USER ap|p")?.Kind == EditorValueKind.User && KindAt("FROM --platform=linux/am|d64 alpine")?.Kind == EditorValueKind.Platform && KindAt("FROM alp|ine")?.Kind == EditorValueKind.Image, "user, platform and image kinds");
    Check(KindAt("STOPSIGNAL SIG|TERM")?.Options.Contains("SIGQUIT") == true && KindAt("LABEL brand.colour=\"#1A2b3|C\"")?.Kind == EditorValueKind.Colour && KindAt("# accent rgb(10, 20, 3|0)")?.Kind == EditorValueKind.Colour, "signal choice and colour kinds");
    var secret = KindAt("FROM alpine\nARG NPM_TO|KEN=abc123\nRUN npm ci");
    Check(secret?.Kind == EditorValueKind.Secret && secret.SecretName == "NPM_TOKEN" && KindAt("ARG NODE_VER|SION=22") == null, "secret-looking ARG names are detected");
    var secretText = "FROM alpine\nENV API_KEY=hunter2 PORT=80\nRUN npm ci";
    var rewrite = EditorValueFormats.SecretRewrite(secretText, DockerfileValueKinds.Detect(secretText, 18)!);
    var rewritten = secretText.Remove(rewrite.Start, rewrite.Length).Insert(rewrite.Start, rewrite.Insert);
    Check(!rewritten.Contains("hunter2") && rewritten.Contains("ENV PORT=80") && rewritten.Contains("RUN --mount=type=secret,id=api-key,env=API_KEY"), "secret rewrite drops the value and keeps other variables");
    Check(At("FROM alpine\nCOPY s|").Items[0].Kind == SuggestionKind.Picker && SuggestionFuzzy.Filter(At("FROM alpine\nCOPY s|").Items, "s")[0].Item.Label == "Browse file…", "picker items stay on top of filtered results");

    var bits = EditorValueFormats.ParsePermissions("750")!;
    Check(EditorValueFormats.ToOctal(bits, false) == "750" && EditorValueFormats.ToSymbolic(bits) == "u=rwx,g=rx,o=" && EditorValueFormats.ToOctal(EditorValueFormats.ParsePermissions("u=rw,go=r")!, true) == "0644", "permission grid converts between octal and symbolic");
    Check(EditorValueFormats.ParseDuration("1m30s") == TimeSpan.FromSeconds(90) && EditorValueFormats.FormatDuration(TimeSpan.FromSeconds(90)) == "1m30s" && EditorValueFormats.ParseDuration("abc") == null, "durations parse and format like Go");
    var port = EditorValueFormats.ParsePort("127.0.0.1:8080:80/udp")!;
    Check(port.Ip == "127.0.0.1" && port.Host == "8080" && port.Container == "80" && port.Protocol == "udp" && port.ToCompose() == "127.0.0.1:8080:80/udp" && EditorValueFormats.ParsePort("99999") == null, "Compose port specs round trip and are validated");
    Check(EditorValueFormats.PublishedHostPorts("0.0.0.0:8080->80/tcp, [::]:8080->80/tcp, 0.0.0.0:9000-9001->9000-9001/udp").Distinct().Count() == 3, "published host ports are read from docker ps");
    Check(EditorValueFormats.TryParseColour("#abc", out var cr, out var cg, out var cb) && cr == 0xAA && EditorValueFormats.FormatColour("#a1b2c3", 1, 2, 255) == "#0102ff" && EditorValueFormats.FormatColour("rgb(1,2,3)", 4, 5, 6) == "rgb(4, 5, 6)", "colours parse and keep their notation");
    var ctxRoot = Path.Combine(Path.GetTempPath(), "mdi-ctx-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(ctxRoot, "src")); Directory.CreateDirectory(Path.Combine(ctxRoot, "node_modules", "x"));
        File.WriteAllText(Path.Combine(ctxRoot, "src", "app.js"), ""); File.WriteAllText(Path.Combine(ctxRoot, ".env"), "SECRET=1"); File.WriteAllText(Path.Combine(ctxRoot, "id_rsa"), "");
        File.WriteAllText(Path.Combine(ctxRoot, "notes.log"), ""); File.WriteAllText(Path.Combine(ctxRoot, ".dockerignore"), "*.log\n");
        var listed = DockerfileCompletionPaths.List(ctxRoot);
        Check(listed.Contains("src/") && listed.Contains("src/app.js") && listed.Contains("node_modules/") && !listed.Any(l => l.StartsWith("node_modules/x")) && !listed.Contains(".env") && !listed.Contains("id_rsa") && !listed.Contains("notes.log"), "context listing skips secrets, ignored files and heavy folders");
        Check(EditorValueFormats.TryRelativePath(ctxRoot, Path.Combine(ctxRoot, "src", "app.js"), false, out var rel, out _) && rel == "src/app.js" && !EditorValueFormats.TryRelativePath(ctxRoot, Path.GetTempPath(), false, out _, out var outsideError) && outsideError.Contains("outside the build context"), "browse paths are context relative and outside paths are refused");
    }
    finally { Directory.Delete(ctxRoot, true); }

    var compose = "services:\n  web:\n    image: nginx\n    depends_on:\n      - |\n  db:\n    image: postgres\nvolumes:\n  data:\nnetworks:\n  front:\n";
    Check(Labels(ComposeAt(compose)).SequenceEqual(new[] { "db" }), "depends_on list offers other services");
    Check(Has(ComposeAt("|"), "services") && Has(ComposeAt("services:\n  web:\n    |"), "healthcheck") && Has(ComposeAt("services:\n  web:\n    |"), "develop"), "top-level and service keys by indentation");
    Check(Has(ComposeAt("services:\n  web:\n    build:\n      |"), "dockerfile") && Has(ComposeAt("services:\n  web:\n    healthcheck:\n      |"), "start_period"), "build and healthcheck keys");
    Check(Labels(ComposeAt("services:\n  web:\n    depends_on:\n      db:\n        condition: |")).SequenceEqual(new[] { "service_started", "service_healthy", "service_completed_successfully" }), "depends_on condition values");
    Check(Has(ComposeAt("services:\n  web:\n    develop:\n      watch:\n        - action: sync\n          |"), "target") && Has(ComposeAt("services:\n  web:\n    develop:\n      watch:\n        - action: |"), "sync+restart"), "develop.watch rule keys and actions");
    Check(Has(ComposeAt("services:\n  web:\n    volumes:\n      - |\nvolumes:\n  data:\n"), "data:") && Has(ComposeAt("services:\n  web:\n    networks:\n      - |\nnetworks:\n  front:\n"), "front"), "named volumes and networks for services");
    Check(Has(ComposeAt("services:\n  web:\n    restart: |"), "unless-stopped") && Has(ComposeAt("services:\n  web:\n    deploy:\n      resources:\n        limits:\n          |"), "memory"), "restart values and nested deploy keys");
    Check(Has(ComposeAt("services:\n  web:\n    depends_on:\n    - |\n  api:\n    image: x\n"), "api"), "compact list indentation is understood");
    Check(Has(ComposeAt("services:\n  web:\n    image: my|", images), "myapp:1.2"), "Compose image offers local images");
    Check(ComposeKindAt("services:\n  web:\n    ports:\n      - \"80|80:80\"")?.Kind == EditorValueKind.Port && ComposeKindAt("services:\n  web:\n    ports:\n      - \"8080:80\"".Insert(37, "|"))!.Text == "8080:80", "Compose port kind with the unquoted span");
    Check(ComposeKindAt("services:\n  web:\n    build:\n      context: .|/app")?.FolderOnly == true && ComposeKindAt("services:\n  web:\n    env_file: .e|nv")?.Kind == EditorValueKind.Path, "Compose path kinds");
    Check(ComposeKindAt("services:\n  web:\n    healthcheck:\n      interval: 3|0s")?.Kind == EditorValueKind.Duration && ComposeKindAt("services:\n  web:\n    tty: tr|ue")?.Kind == EditorValueKind.Choice && ComposeKindAt("services:\n  web:\n    labels:\n      color: \"#ff00|00\"")?.Kind == EditorValueKind.Colour, "Compose duration, boolean and colour kinds");
    Check(ComposeKindAt("services:\n  web:\n    volumes:\n      - ./da|ta:/data")?.Text == "./data" && ComposeKindAt("services:\n  web:\n    volumes:\n      - data:/da|ta") == null, "Compose bind host path kind only on the host side");
    Check(ComposeCompletion.Hover("services:\n  web:\n    restart: always\n", 22)?.Label == "restart" && ComposeCompletion.Hover("services:\n  web:\n", 3)?.Label == "services", "Compose hover documents keys by context");
    Check(new UserSettings().SuggestionsWhileTyping, "suggestions while typing default on");
    Check(DockerfileSyntaxVersion.Parse("docker/dockerfile:1.4")!.Satisfies("1.4") && !DockerfileSyntaxVersion.Parse("docker/dockerfile:1.3")!.Satisfies("1.4") && DockerfileSyntaxVersion.Parse("example.com/custom:1")!.Satisfies("1.20"), "syntax version comparison");
}
{
    // Page-specific copy: representative strings from every destination must follow the language mode.
    var pageLocPrior = MaterialDockerImage.Services.Loc.Language;
    var pageLocSchool = MaterialDockerImage.Services.Loc.SchoolMode;
    MaterialDockerImage.Services.Loc.SchoolMode = false;
    bool HasCjk(string text) => text.Any(c => c >= '一' && c <= '鿿');
    var pageSamples = new[]
    {
        "Search history", "Refresh builders", "No containers", "No local images", "Create volume", "Pull image…", "Choose a folder",
        "Prune dangling images", "Remove container", "Restart policy", "Port mapping", "Environment variables", "Build cache", "Compose projects",
        "Swarm nodes", "Swarm secrets", "Join swarm", "Leave swarm", "Create Minecraft server…", "Chunky commands", "Backup folder", "Delete preset",
        "Reset appearance", "Export settings", "Import settings", "Show local history", "Scan all images", "Select an image in Images first.",
        "Sign in to a registry…", "Run all diagnostic checks", "Stream engine events", "Detect Docker", "Setup finished", "Set as build target",
        "Open Dockerfile…", "Copy as build command", "Generate SBOM", "Remove unused data", "Engine disk usage", "No networks", "No secrets",
        "No services", "Take one sample now", "Live monitor", "Choose a build context folder first: COPY and ADD paths are relative to it.",
        "The server is not running. Start it first.", "Enter a volume name.", "Docker CLI not found", "Waiting for events", "Not analyzed"
    };
    Check(pageSamples.Length >= 40, "at least forty page strings are sampled");
    foreach (var language in new[] { "Cantonese", "Bilingual" })
    {
        MaterialDockerImage.Services.Loc.Language = language;
        var untranslated = pageSamples.Where(x => { var t = MaterialDockerImage.Services.Loc.T(x); return t == x || !HasCjk(t) || (language == "Bilingual" && !t.StartsWith(x + " · ", StringComparison.Ordinal)); }).ToList();
        Check(untranslated.Count == 0, language + " mode translates every sampled page string" + (untranslated.Count == 0 ? "" : ": " + string.Join(" | ", untranslated)));
    }
    MaterialDockerImage.Services.Loc.Language = "English";
    Check(pageSamples.All(x => MaterialDockerImage.Services.Loc.T(x) == x), "English mode leaves page strings unchanged");
    MaterialDockerImage.Services.Loc.Language = "Cantonese";
    var restored = MaterialDockerImage.Services.Loc.T("Restored abc123 as a new history revision.");
    Check(HasCjk(restored) && restored.Contains("abc123") && !restored.Contains("history revision"), "sentence template keeps the revision identifier and translates the wording");
    var copied = MaterialDockerImage.Services.Loc.T("Copied 12 characters.");
    Check(HasCjk(copied) && copied.Contains("12"), "sentence template keeps counts");
    var volumeName = "my_data-01";
    var backupPath = "C:/backups/x.tar.gz";
    var backedUp = MaterialDockerImage.Services.Loc.T("Backed up volume " + volumeName + " to " + backupPath);
    Check(HasCjk(backedUp) && backedUp.Contains(volumeName) && backedUp.Contains(backupPath), "sentence template keeps volume names and paths literal");
    var statusLine = MaterialDockerImage.Services.Loc.Line("3 images · 2 unique IDs · context default · refreshed 10:20:30");
    Check(HasCjk(statusLine) && statusLine.Contains("3") && statusLine.Contains("default") && statusLine.Contains("10:20:30") && !statusLine.Contains("images"), "status line translates each phrase and keeps counts and context names");
    Check(MaterialDockerImage.Services.Loc.T("nginx:1.27") == "nginx:1.27" && MaterialDockerImage.Services.Loc.T("registry-1.docker.io/library/redis") == "registry-1.docker.io/library/redis", "Docker identifiers are never translated");
    MaterialDockerImage.Services.Loc.Language = "Bilingual";
    var bilingualRestored = MaterialDockerImage.Services.Loc.T("Copied 12 characters.");
    Check(bilingualRestored.StartsWith("Copied 12 characters. · ", StringComparison.Ordinal) && HasCjk(bilingualRestored), "bilingual mode shows the English sentence first and the Cantonese sentence second");
    MaterialDockerImage.Services.Loc.Language = pageLocPrior;
    MaterialDockerImage.Services.Loc.SchoolMode = pageLocSchool;
}
count += BuildCompanionChecks.Run();
Console.WriteLine($"UI state checks: {count} passed.");
