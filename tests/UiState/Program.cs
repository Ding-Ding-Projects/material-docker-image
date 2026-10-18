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
Console.WriteLine($"UI state checks: {count} passed.");
