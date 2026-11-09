namespace MaterialDockerImage.UI;

/// <summary>
/// One catalogue entry: what the app needs and how its Compose project is built. Images are pinned to exact tags that
/// were checked against their registries; a service list is produced per set of options by <see cref="Services"/>.
/// </summary>
public sealed class SelfHostedApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SelfHostedCategory Category { get; init; }
    public required string Description { get; init; }
    public required int MinMemoryMb { get; init; }
    public required int MinDiskGb { get; init; }
    public required IReadOnlyList<SelfHostedPort> Ports { get; init; }
    public required IReadOnlyList<SelfHostedVolume> Volumes { get; init; }
    public IReadOnlyList<SelfHostedSecret> Secrets { get; init; } = [];
    /// <summary>Port key and path probed after deployment; any response below 500 counts as up.</summary>
    public required string HealthPort { get; init; }
    public required string HealthPath { get; init; }
    /// <summary>Longest wait for the first healthy response (GitLab needs several minutes on first start).</summary>
    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromMinutes(4);
    public required string FirstLogin { get; init; }
    public required string DocsUrl { get; init; }
    public required string Licence { get; init; }
    /// <summary>Default administrator name when the app creates its administrator from the setup values; null when the app asks in its own first-run page.</summary>
    public string? AdminUser { get; init; }
    public bool AdminIsEmail { get; init; }
    /// <summary>Supported databases, first is the default. Empty when the app has no database choice.</summary>
    public IReadOnlyList<SelfHostedDatabase> Databases { get; init; } = [];
    /// <summary>A sentence about limits of running this app on Docker Desktop, or null.</summary>
    public string? Note { get; init; }
    public required Func<SelfHostedOptions, IReadOnlyList<SelfHostedServiceSpec>> Services { get; init; }

    public SelfHostedPort WebPort => Ports.FirstOrDefault(p => p.Web) ?? Ports[0];
    public IEnumerable<SelfHostedVolume> VolumesFor(SelfHostedDatabase database) => Volumes.Where(v => v.Only == SelfHostedDatabase.None || v.Only == database);
    public IEnumerable<SelfHostedSecret> SecretsFor(SelfHostedDatabase database) => Secrets.Where(s => s.Only == SelfHostedDatabase.None || s.Only == database);

    /// <summary>Every image the project uses for the given database choice.</summary>
    public IReadOnlyList<string> ImagesFor(SelfHostedDatabase database)
    {
        var options = new SelfHostedOptions { Database = database };
        return Services(options).Select(s => s.Image).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every image in every database variant.</summary>
    public IReadOnlyList<string> AllImages() =>
        (Databases.Count == 0 ? [SelfHostedDatabase.None] : Databases).SelectMany(ImagesFor).Distinct(StringComparer.Ordinal).ToList();

    public string CategoryText => SelfHostedCatalogue.CategoryName(Category);
}
