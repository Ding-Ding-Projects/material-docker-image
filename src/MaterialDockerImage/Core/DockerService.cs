using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.Core;

public sealed class DockerService(string executable = "docker", IDockerProcessRunner? runner = null)
{
    private readonly IDockerProcessRunner processRunner = runner ?? new DockerProcessRunner();
    public Task<CommandResult> ExecuteAsync(IReadOnlyList<string> arguments, string? context = null, IProgress<DockerOutput>? output = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count >= 2 && arguments[0] == "context" && arguments[1] == "use")
            throw new ArgumentException("Global context changes are not supported. Select a context for this operation instead.", nameof(arguments));
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(context)) { ValidateOperand(context, nameof(context)); args.AddRange(["--context", context]); }
        foreach (var arg in arguments) { ValidateToken(arg); args.Add(arg); }
        return processRunner.RunAsync(executable, args, null, output, cancellationToken);
    }
    public Task<CommandResult> VersionAsync(string? context = null, CancellationToken cancellationToken = default) => ExecuteAsync(["version", "--format", "{{json .}}"], context, cancellationToken: cancellationToken);
    public Task<CommandResult> InfoAsync(string? context = null, CancellationToken cancellationToken = default) => ExecuteAsync(["info", "--format", "{{json .}}"], context, cancellationToken: cancellationToken);
    private async Task<IReadOnlyList<T>> ListAsync<T>(string[] arguments, Func<JsonElement,T> convert, string? context, CancellationToken ct)
    {
        var result = await ExecuteAsync(arguments, context, cancellationToken: ct); result.EnsureSuccess();
        if (result.Truncated) throw new InvalidOperationException("The response exceeded the capture limit. Narrow the selection and retry.");
        return DockerJson.ParseRows(result.StandardOutput).Select(convert).ToArray();
    }
    public Task<IReadOnlyList<DockerImage>> ListImagesAsync(string? context = null, CancellationToken cancellationToken = default) => ListAsync(["image","ls","--format","{{json .}}"], x => new DockerImage(T(x,"ID"),T(x,"Repository"),T(x,"Tag"),T(x,"Size"),T(x,"CreatedSince")), context,cancellationToken);
    public Task<IReadOnlyList<DockerContainer>> ListContainersAsync(string? context = null, CancellationToken cancellationToken = default) => ListAsync(["container","ls","--all","--format","{{json .}}"], x => new DockerContainer(T(x,"ID"),T(x,"Names"),T(x,"Image"),T(x,"State"),T(x,"Status"),T(x,"Ports")),context,cancellationToken);
    public Task<IReadOnlyList<DockerContext>> ListContextsAsync(CancellationToken cancellationToken = default) => ListAsync(["context","ls","--format","{{json .}}"], x => new DockerContext(T(x,"Name"),T(x,"Description"),T(x,"DockerEndpoint"),string.Equals(T(x,"Current"),"true",StringComparison.OrdinalIgnoreCase)),null,cancellationToken);
    public Task<IReadOnlyList<DockerNetwork>> ListNetworksAsync(string? context = null, CancellationToken cancellationToken = default) => ListAsync(["network","ls","--format","{{json .}}"],x=>new DockerNetwork(T(x,"ID"),T(x,"Name"),T(x,"Driver"),T(x,"Scope")),context,cancellationToken);
    public Task<IReadOnlyList<DockerVolume>> ListVolumesAsync(string? context = null, CancellationToken cancellationToken = default) => ListAsync(["volume","ls","--format","{{json .}}"],x=>new DockerVolume(T(x,"Name"),T(x,"Driver"),T(x,"Mountpoint")),context,cancellationToken);
    private static string T(JsonElement row, string key) => DockerJson.Text(row,key);
    public Task<CommandResult> InspectAsync(string kind, string reference, string? context = null, CancellationToken cancellationToken = default)
    {
        if (kind is not ("image" or "container" or "network" or "volume" or "context")) throw new ArgumentException("Unsupported resource kind.",nameof(kind));
        ValidateOperand(reference,nameof(reference)); return ExecuteAsync([kind,"inspect",reference],context,cancellationToken:cancellationToken);
    }
    public Task<CommandResult> PullAsync(string image, string? context = null, IProgress<DockerOutput>? output = null, CancellationToken cancellationToken = default) => OperandCommand(["image","pull"],image,context,output,cancellationToken);
    public Task<CommandResult> TagAsync(string image, string target, string? context = null, CancellationToken cancellationToken = default) { ValidateOperand(target,nameof(target)); ValidateOperand(image,nameof(image)); return ExecuteAsync(["image","tag",image,target],context,cancellationToken:cancellationToken); }
    public Task<CommandResult> SaveAsync(string image, string path, string? context = null, IProgress<DockerOutput>? output = null, CancellationToken cancellationToken = default) { ValidateOperand(image,nameof(image)); return ExecuteAsync(["image","save","--output",FilePath(path,false),image],context,output,cancellationToken); }
    public Task<CommandResult> LoadAsync(string path, string? context = null, IProgress<DockerOutput>? output = null, CancellationToken cancellationToken = default) => ExecuteAsync(["image","load","--input",FilePath(path,true)],context,output,cancellationToken);
    public Task<CommandResult> RemoveImageAsync(string image, string? context = null, CancellationToken cancellationToken = default) => OperandCommand(["image","rm"],image,context,null,cancellationToken);
    public Task<CommandResult> RemoveNetworkAsync(string network, string? context = null, CancellationToken cancellationToken = default) => OperandCommand(["network","rm"],network,context,null,cancellationToken);
    public Task<CommandResult> RemoveVolumeAsync(string volume, string? context = null, CancellationToken cancellationToken = default) => OperandCommand(["volume","rm"],volume,context,null,cancellationToken);
    public Task<CommandResult> ContainerActionAsync(string action, string container, string? context = null, CancellationToken cancellationToken = default)
    {
        if (action is not ("start" or "stop" or "restart" or "rm" or "pause" or "unpause" or "top" or "diff")) throw new ArgumentException("Unsupported container action.",nameof(action));
        return OperandCommand(["container",action],container,context,null,cancellationToken);
    }
    public Task<CommandResult> LogsAsync(string container, bool follow = false, int tail = 500, string? context = null, IProgress<DockerOutput>? output = null, CancellationToken cancellationToken = default)
    {
        ValidateOperand(container,nameof(container)); if (tail < 0 || tail > 100000) throw new ArgumentOutOfRangeException(nameof(tail));
        var args = new List<string>{"container","logs","--tail",tail.ToString(CultureInfo.InvariantCulture)}; if(follow) args.Add("--follow"); args.Add(container); return ExecuteAsync(args,context,output,cancellationToken);
    }
    public Task<CommandResult> StatsAsync(string container,string? context=null,CancellationToken cancellationToken=default) { ValidateOperand(container,nameof(container)); return ExecuteAsync(["container","stats","--no-stream","--format","{{json .}}",container],context,cancellationToken:cancellationToken); }
    public Task<CommandResult> ExecAsync(string container,IReadOnlyList<string> command,string? context=null,IProgress<DockerOutput>? output=null,CancellationToken cancellationToken=default)
    {
        ValidateOperand(container,nameof(container)); if(command.Count==0) throw new ArgumentException("An executable is required.",nameof(command)); ValidateOperand(command[0],nameof(command));
        var args=new List<string>{"container","exec",container}; foreach(var token in command){ValidateToken(token);args.Add(token);} return ExecuteAsync(args,context,output,cancellationToken);
    }
    private Task<CommandResult> OperandCommand(string[] prefix,string operand,string? context,IProgress<DockerOutput>? output,CancellationToken ct) { ValidateOperand(operand,nameof(operand)); return ExecuteAsync(prefix.Append(operand).ToArray(),context,output,ct); }
    public Task<CommandResult> BuildAsync(BuildOptions options,string? context=null,IProgress<DockerOutput>? output=null,CancellationToken cancellationToken=default) => ExecuteAsync(CreateBuildArguments(options),context,output,cancellationToken);
    public static IReadOnlyList<string> CreateBuildArguments(BuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options); if(options.Load&&options.Push) throw new ArgumentException("Choose load or push, not both.");
        var args=new List<string>{"buildx","build","--progress","plain"};
        foreach(var tag in options.Tags){ValidateOperand(tag,"tag");args.AddRange(["--tag",tag]);}
        foreach(var platform in options.Platforms){ValidateOperand(platform,"platform");args.AddRange(["--platform",platform]);}
        AddPairs(args,"--build-arg",options.BuildArguments); AddPairs(args,"--label",options.Labels);
        AddValue(args,"--target",options.Target);AddValue(args,"--output",options.Output);
        if(options.Dockerfile is not null)args.AddRange(["--file",FilePath(options.Dockerfile,true)]);
        if(options.NoCache)args.Add("--no-cache");if(options.Pull)args.Add("--pull");if(options.Load)args.Add("--load");if(options.Push)args.Add("--push");
        args.Add(DirectoryPath(options.ContextPath));return args;
    }
    public Task<CommandResult> RunAsync(RunOptions options,string? context=null,IProgress<DockerOutput>? output=null,CancellationToken cancellationToken=default) => ExecuteAsync(CreateRunArguments(options),context,output,cancellationToken);
    public static IReadOnlyList<string> CreateRunArguments(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);ValidateOperand(options.Image,nameof(options.Image));var args=new List<string>{"container","run"};
        if(options.Detached)args.Add("--detach");if(options.RemoveOnExit)args.Add("--rm");if(options.ReadOnly)args.Add("--read-only");
        AddValue(args,"--name",options.Name);AddValue(args,"--network",options.Network);AddValue(args,"--workdir",options.WorkingDirectory);AddValue(args,"--user",options.User);AddValue(args,"--entrypoint",options.EntryPoint);AddValue(args,"--memory",options.Memory);
        if(options.RestartPolicy is not null){if(!Regex.IsMatch(options.RestartPolicy,@"^(no|always|unless-stopped|on-failure(:[0-9]+)?)$"))throw new ArgumentException("Invalid restart policy.");AddValue(args,"--restart",options.RestartPolicy);}
        if(options.Cpus is { } cpus){if(!double.IsFinite(cpus)||cpus<=0)throw new ArgumentOutOfRangeException(nameof(options.Cpus));args.AddRange(["--cpus",cpus.ToString(CultureInfo.InvariantCulture)]);}
        AddPairs(args,"--env",options.Environment);AddPairs(args,"--label",options.Labels);
        foreach(var port in options.Ports){if(port.HostPort is <1 or >65535||port.ContainerPort is <1 or >65535||port.Protocol is not ("tcp" or "udp" or "sctp"))throw new ArgumentException("Invalid port mapping.");if(port.HostAddress is not null&&!System.Net.IPAddress.TryParse(port.HostAddress,out _))throw new ArgumentException("Host address must be an IP address.");var host=port.HostAddress is null?"":port.HostAddress.Contains(':')?$"[{port.HostAddress}]:":port.HostAddress+":";args.AddRange(["--publish",$"{host}{port.HostPort}:{port.ContainerPort}/{port.Protocol}"]);}
        foreach(var volume in options.Volumes){ValidateOperand(volume.Source,"volume source");ValidateOperand(volume.Destination,"volume destination");if(volume.Source.Contains(',')||volume.Destination.Contains(','))throw new ArgumentException("Mount paths cannot contain commas.");var bind=Path.IsPathFullyQualified(volume.Source);var source=bind?DirectoryPath(volume.Source):volume.Source;args.AddRange(["--mount",$"type={(bind?"bind":"volume")},source={source},target={volume.Destination}"+(volume.ReadOnly?",readonly":"")]);}
        args.Add(options.Image);foreach(var token in options.Command){ValidateToken(token);args.Add(token);}return args;
    }
    public Task<CommandResult> ComposeAsync(ComposeOptions options,string? context=null,IProgress<DockerOutput>? output=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(options);if(!Enum.IsDefined(options.Operation))throw new ArgumentException("Unsupported Compose operation.");var directory=DirectoryPath(options.ProjectDirectory);var args=new List<string>{"compose","--project-directory",directory};
        foreach(var file in options.Files){ValidateOperand(file,"Compose file");args.AddRange(["--file",FilePath(Path.GetFullPath(file,directory),true)]);}AddValue(args,"--project-name",options.ProjectName);foreach(var profile in options.Profiles)AddValue(args,"--profile",profile);
        args.Add(options.Operation.ToString().ToLowerInvariant());
        if(options.Operation==ComposeOperation.Up){if(options.Detached)args.Add("--detach");if(options.Build)args.Add("--build");if(options.RemoveOrphans)args.Add("--remove-orphans");}
        if(options.Operation==ComposeOperation.Down){if(options.RemoveVolumes)args.Add("--volumes");if(options.RemoveOrphans)args.Add("--remove-orphans");}
        if(options.Operation==ComposeOperation.Ps)args.AddRange(["--all","--format","json"]);
        if(options.Operation==ComposeOperation.Logs){if(options.Tail is <0 or >100000)throw new ArgumentOutOfRangeException(nameof(options.Tail));args.AddRange(["--tail",options.Tail.ToString(CultureInfo.InvariantCulture)]);if(options.Follow)args.Add("--follow");}
        foreach(var service in options.Services){ValidateOperand(service,"service");args.Add(service);}return ExecuteAsync(args,context,output,cancellationToken);
    }
    public static void ValidateOperand(string value,string name)
    {
        if(string.IsNullOrWhiteSpace(value)||value.StartsWith('-'))throw new ArgumentException("A non-option value is required.",name);ValidateToken(value);
    }
    private static void ValidateToken(string value){ArgumentNullException.ThrowIfNull(value);if(value.IndexOfAny(['\0','\r','\n'])>=0)throw new ArgumentException("Control characters are not allowed.");}
    private static void AddValue(List<string> args,string flag,string? value){if(value is null)return;ValidateOperand(value,flag);args.AddRange([flag,value]);}
    private static void AddPairs(List<string> args,string flag,IReadOnlyDictionary<string,string> values)
    {
        foreach(var pair in values)
        {
            ValidateOperand(pair.Key,"key");
            var valid = flag == "--label" ? !pair.Key.Any(c => char.IsWhiteSpace(c) || c == '=') : Regex.IsMatch(pair.Key,@"^[A-Za-z_][A-Za-z0-9_]*$");
            if(!valid)throw new ArgumentException("Invalid key.");
            ValidateToken(pair.Value);args.AddRange([flag,pair.Key+"="+pair.Value]);
        }
    }
    private static string DirectoryPath(string path){ValidateOperand(path,nameof(path));var full=Path.GetFullPath(path);if(!Directory.Exists(full))throw new DirectoryNotFoundException(full);return full;}
    private static string FilePath(string path,bool mustExist){ValidateOperand(path,nameof(path));var full=Path.GetFullPath(path);if(mustExist&&!File.Exists(full))throw new FileNotFoundException("File does not exist.",full);if(!mustExist&&!Directory.Exists(Path.GetDirectoryName(full)))throw new DirectoryNotFoundException(Path.GetDirectoryName(full));return full;}
}
