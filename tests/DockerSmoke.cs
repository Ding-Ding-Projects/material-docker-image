using System.Text.Json;
using MaterialDockerImage.Core;

internal static class DockerSmoke
{
    public static async Task<int> RunAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        var image = "material-docker-image-smoke:" + id;
        var container = "material-docker-image-smoke-" + id;
        var directory = Path.Combine(Path.GetTempPath(), "material-docker-image-smoke-" + id);
        const string context = "desktop-linux";
        const string label = "io.material-docker-image.smoke";
        const string expected = "material-docker-image-engine-smoke";
        var docker = new DockerService();
        var built = false;
        var verified = false;
        var cleanup = true;
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"Dockerfile"),"FROM debian:bookworm-slim\nCOPY fixture.txt /material-smoke.txt\n");
            await File.WriteAllTextAsync(Path.Combine(directory,"fixture.txt"),expected);
            var build = await docker.BuildAsync(new BuildOptions {ContextPath=directory,Tags=[image],Pull=false,Load=true,Labels=new Dictionary<string,string>{{label,id}}},context,cancellationToken:timeout.Token);
            build.EnsureSuccess(); built=true;
            var run = await docker.RunAsync(new RunOptions {Image=image,Name=container,Detached=false,RemoveOnExit=true,Network="none",ReadOnly=true,Labels=new Dictionary<string,string>{{label,id}},Command=["/bin/sh","-c","cat /material-smoke.txt"]},context,cancellationToken:timeout.Token);
            run.EnsureSuccess();
            if(run.Truncated||run.StandardOutput!=expected)throw new InvalidOperationException("The fixture output did not match the expected bytes.");
            verified=true;
        }
        catch(Exception exception)
        {
            // Do not persist daemon transcripts, which can contain local paths or configuration.
            Console.Error.WriteLine("Docker smoke did not complete: "+exception.GetType().Name);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var inspectContainer=await docker.InspectAsync("container",container,context,cleanupTimeout.Token);
                if(inspectContainer.Success)
                {
                    var ownedId=GetOwnedId(inspectContainer,label,id);
                    var stop=await docker.ContainerActionAsync("stop",ownedId,context,cleanupTimeout.Token);
                    var remove=await docker.ContainerActionAsync("rm",ownedId,context,cleanupTimeout.Token);
                    // Auto-removal can race the explicit removal after stop.
                    var remaining=await docker.InspectAsync("container",container,context,cleanupTimeout.Token);
                    if(remaining.Success)throw new InvalidOperationException("Owned container still exists.");
                }
                else if(!IsMissing(inspectContainer))throw new InvalidOperationException("Container absence could not be verified.");
                {
                    var inspectImage=await docker.InspectAsync("image",image,context,cleanupTimeout.Token);
                    if(inspectImage.Success)
                    {
                        _=GetOwnedId(inspectImage,label,id);
                        var removal=await docker.RemoveImageAsync(image,context,cleanupTimeout.Token);removal.EnsureSuccess();
                    }
                    else if(!IsMissing(inspectImage))throw new InvalidOperationException("Image absence could not be verified.");
                }
            }
            catch(Exception exception){cleanup=false;Console.Error.WriteLine("Owned resource cleanup incomplete: "+exception.GetType().Name);}
            try { Directory.Delete(directory,true); } catch { cleanup=false; }
        }
        var receipt=new {schemaVersion=1,context,baseImage="debian:bookworm-slim",buildVerified=built,runVerified=verified,network="none",readOnly=true,ownedResourcesRemoved=cleanup,completedUtc=DateTimeOffset.UtcNow};
        Console.WriteLine(JsonSerializer.Serialize(receipt));
        Console.WriteLine(verified&&cleanup?"PASS: Docker build/run smoke and owned cleanup":"FAIL: Docker build/run smoke or owned cleanup");
        return verified&&cleanup?0:1;
    }
    private static bool IsMissing(CommandResult result)=>!result.Cancelled&&result.StandardError.Contains("No such",StringComparison.OrdinalIgnoreCase);
    private static string GetOwnedId(CommandResult result,string key,string expected)
    {
        if(result.Truncated)throw new InvalidOperationException("Ownership response truncated.");
        using var document=JsonDocument.Parse(result.StandardOutput);
        var resource=document.RootElement[0];
        if(resource.GetProperty("Config").GetProperty("Labels").GetProperty(key).GetString()!=expected)throw new InvalidOperationException("Resource ownership mismatch.");
        return resource.GetProperty("Id").GetString()??throw new InvalidOperationException("Resource ID missing.");
    }
}
