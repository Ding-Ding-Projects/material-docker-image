using MaterialDockerImage.Core;

if (args.Length == 1 && args[0] == "--docker-smoke") return await DockerSmoke.RunAsync();

// This executable also supplies deterministic child processes for transport tests.
if (args.Length > 0)
{
    if (args[0] == "fixture-output") { Console.Write(new string('x', 24000)); Console.Error.Write(new string('e', 17000)); return 7; }
    if (args[0] == "fixture-wait") { Console.Write("ready"); Console.Out.Flush(); await Task.Delay(30000); return 0; }
    return 4;
}
var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
void Reject(Action action, string message) { try { action(); } catch (ArgumentException) { passed++; return; } throw new Exception(message); }
var fake = new RecordingRunner();
var service = new DockerService("docker", fake);
await service.PullAsync("registry.example/image:latest", "isolated");
Check(fake.Arguments.SequenceEqual(new[] {"--context","isolated","image","pull","registry.example/image:latest"}), "Context and operands must remain separate arguments.");
Reject(() => service.PullAsync("--all-tags"), "An image cannot inject an option.");
Reject(() => service.TagAsync("image", "-target"), "A target cannot inject an option.");
Reject(() => service.ExecAsync("container", ["--privileged"]), "An executable cannot inject an option.");
Reject(() => service.ExecuteAsync(["info\nversion"]), "Control characters must be rejected.");
Reject(() => service.ExecuteAsync(["context","use","other"]), "The service must never change the global context.");
Reject(() => DockerService.CreateRunArguments(new RunOptions { Image = "--privileged" }), "Run image option injection must fail.");
Reject(() => DockerService.CreateRunArguments(new RunOptions { Image = "image", Ports = [new(70000,80)] }), "Invalid ports must fail.");
Reject(() => DockerService.CreateRunArguments(new RunOptions { Image = "image", Cpus = double.NaN }), "Nonfinite CPU constraints must fail.");
Reject(() => DockerService.CreateBuildArguments(new BuildOptions { Push=true, Load=true }), "Conflicting outputs must fail.");
var run = DockerService.CreateRunArguments(new RunOptions { Image = "image", Name="name", Environment = new Dictionary<string,string>{{"TEXT","a b; $(anything)"}}, Command=["echo","--literal"] });
Check(run.Contains("TEXT=a b; $(anything)"), "Environment values must remain one argument without shell evaluation.");
Check(run.TakeLast(3).SequenceEqual(new[]{"image","echo","--literal"}), "Command operands must follow the image.");
Check(DockerService.CreateRunArguments(new RunOptions { Image="image",Labels=new Dictionary<string,string>{{"example.com/component:kind","worker"}} }).Contains("example.com/component:kind=worker"),"Label names support namespaced punctuation.");
Reject(()=>DockerService.CreateRunArguments(new RunOptions { Image="image",Environment=new Dictionary<string,string>{{"BAD=KEY","value"}} }),"Environment keys cannot inject assignments.");
fake.Result = new CommandResult(0,"{\"ID\":\"one\",\"Repository\":\"repo\"}\n{\"ID\":\"two\",\"Repository\":\"other\"}\n","");
var images = await service.ListImagesAsync();
Check(images.Count == 2 && images[1].ID == "two", "JSON line parsing must retain every row.");
Check(DockerJson.ParseRows("[{\"Name\":\"one\"},{\"Name\":\"two\"}]").Count==2,"Compose JSON arrays must parse.");
fake.Result = new CommandResult(0,"{\"Name\":\"active\",\"Current\":true}\n{\"Name\":\"other\",\"Current\":false}","");
var contexts=await service.ListContextsAsync();
Check(contexts[0].Current&&!contexts[1].Current,"Context booleans must parse actual JSON values.");
fake.Result = new CommandResult(1,"","daemon unavailable");
try { await service.ListContainersAsync(); throw new Exception("Nonzero exit must not become an empty successful list."); } catch(DockerCommandException) { passed++; }
fake.Result = new CommandResult(0,"{}","",Truncated:true);
try { await service.ListImagesAsync(); throw new Exception("Truncated structured responses must not be parsed."); } catch(InvalidOperationException) { passed++; }
var transport = new DockerProcessRunner(1024);
using(var beforeLaunch=new CancellationTokenSource())
{
    beforeLaunch.Cancel();
    try { await transport.RunAsync("missing-executable",[],null,null,beforeLaunch.Token);throw new Exception("Cancelled operations must not launch."); }catch(OperationCanceledException){passed++;}
}
try { await transport.RunAsync(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe"),[],null,null,default);throw new Exception("A missing executable must not look successful."); }catch(System.ComponentModel.Win32Exception){passed++;}
var executable = Environment.ProcessPath ?? throw new Exception("Missing executable path.");
var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase) ? new[]{typeof(RecordingRunner).Assembly.Location} : Array.Empty<string>();
var result = await transport.RunAsync(executable,prefix.Append("fixture-output").ToArray(),null,null,default);
Check(result.ExitCode==7 && result.Truncated && result.StandardOutput.Length==1024 && result.StandardError.Length==1024,"Transport must drain both streams, bound capture and retain exit status.");
using var cancellation = new CancellationTokenSource();
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var progress = new InlineProgress(output => { if(output.Text.Contains("ready")) ready.TrySetResult(); });
var pending = transport.RunAsync(executable,prefix.Append("fixture-wait").ToArray(),null,progress,cancellation.Token);
await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(10));
Check(cancelled.Cancelled && !cancelled.Success,"Cancellation must terminate and be reported separately from success.");
Console.WriteLine($"PASS: {passed} core checks");
return 0;

sealed class RecordingRunner : IDockerProcessRunner
{
    public string[] Arguments {get;private set;}=[];
    public CommandResult Result {get;set;}=new(0,"","");
    public Task<CommandResult> RunAsync(string executable,IReadOnlyList<string> arguments,string? directory,IProgress<DockerOutput>? output,CancellationToken cancellationToken)
    { Arguments=arguments.ToArray();return Task.FromResult(Result); }
}
sealed class InlineProgress(Action<DockerOutput> callback) : IProgress<DockerOutput>
{ public void Report(DockerOutput value)=>callback(value); }
