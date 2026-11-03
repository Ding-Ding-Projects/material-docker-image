using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The advanced BuildKit sections of the Build configuration: named contexts and SSH, attestations and annotations,
/// and the RUN sandbox (network, shared memory, ulimits, extra hosts). Every edit raises the change callback so the
/// command preview stays current.
/// </summary>
public sealed class BuildAdvancedOptions
{
    private static readonly string[] ProvenanceNames = ["Default", "Off", "Min", "Max"];
    private static readonly string[] NetworkNames = ["Default", "None", "Host"];

    private readonly BuildKeyValueRows contexts, sshKeys, annotations, ulimits, hosts;
    private readonly CheckBox sshDefault, sbom;
    private readonly TextBox shmSize = Ui.Input("", "Shared memory size", true);
    private readonly TextBlock networkWarning = Ui.Text("", 12, null, "Warning");
    public BuildKitProvenance Provenance { get; private set; }
    public BuildKitNetwork Network { get; private set; }

    public Expander ContextsSection { get; }
    public Expander AttestationsSection { get; }
    public Expander SandboxSection { get; }

    public BuildAdvancedOptions(Action changed)
    {
        contexts = new BuildKeyValueRows(new List<NameValue>(), "Context name", "Folder, docker-image:// or URL", "Add named context", BuildKitCommand.ValidateContextName, changed);
        sshDefault = Ui.Switch("Forward the default SSH agent", false, _ => changed(), "Lets RUN --mount=type=ssh use the SSH agent of this computer (--ssh default). Keys never leave the agent.");
        sshKeys = new BuildKeyValueRows(new List<NameValue>(), "SSH id", "Key or socket path", "Add SSH key", BuildKitCommand.ValidateSecretId, changed);
        ContextsSection = Section("Named contexts and SSH", Ui.VStack(14,
            Ui.Field("Named build contexts", contexts,
                "Adds extra contexts a Dockerfile can use with COPY --from=name or FROM name (--build-context). The value is a local folder, docker-image://reference, oci-layout://path, an https:// URL or a git@ address. A name that matches an image replaces that image for this build.",
                "base = docker-image://alpine:3.20\ndocs = C:\\Projects\\docs\nCOPY --from=docs . /usr/share/docs"),
            sshDefault,
            Ui.Field("SSH keys", sshKeys,
                "Forwards a private key file or agent socket under an id (--ssh id=path). Only the path is passed; the key is never read by this application.",
                "github = C:\\Users\\me\\.ssh\\id_ed25519\nRUN --mount=type=ssh,id=github git clone git@github.com:org/repo.git")));

        sbom = Ui.Switch("Generate an SBOM attestation", false, _ => changed(), "Scans the result and attaches a software bill of materials (--sbom=true).");
        var provenance = Ui.Segmented(ProvenanceNames, ProvenanceNames[0], option => { Provenance = (BuildKitProvenance)Array.IndexOf(ProvenanceNames, option); changed(); });
        annotations = new BuildKeyValueRows(new List<NameValue>(), "Annotation key", "Annotation value", "Add annotation", BuildKitCommand.ValidateAnnotationKey, changed);
        AttestationsSection = Section("Attestations and annotations", Ui.VStack(14,
            sbom,
            Ui.Field("Provenance", provenance,
                "Default leaves the builder's choice. Off disables provenance. Min records basic build facts; Max adds the full build definition and arguments, so avoid Max when build arguments hold anything sensitive. Attestations need a destination that stores them (push, OCI or the containerd image store); otherwise the build reports an error.",
                "--provenance=mode=max"),
            Ui.Field("Annotations", annotations,
                "OCI annotations written to the image manifest or index (--annotation). Prefix a key with manifest:, index: or a descriptor type to choose where it goes.",
                "org.opencontainers.image.source = https://example.com/repo\nindex:org.opencontainers.image.title = hello-api")));

        var network = Ui.Segmented(NetworkNames, NetworkNames[0], option =>
        {
            Network = (BuildKitNetwork)Array.IndexOf(NetworkNames, option);
            networkWarning.Text = Network == BuildKitNetwork.Host ? Loc.T("Host networking gives RUN steps the builder host's network and needs the network.host entitlement (added as --allow network.host). Builders that do not allow it refuse the build.") : "";
            networkWarning.Visibility = Network == BuildKitNetwork.Host ? Visibility.Visible : Visibility.Collapsed;
            changed();
        });
        networkWarning.Visibility = Visibility.Collapsed;
        shmSize.TextChanged += (_, _) => changed();
        ulimits = new BuildKeyValueRows(new List<NameValue>(), "Limit", "soft:hard", "Add ulimit", BuildKitCommand.ValidateUlimitName, changed);
        hosts = new BuildKeyValueRows(new List<NameValue>(), "Host name", "IP address", "Add host", BuildKitCommand.ValidateHostName, changed);
        SandboxSection = Section("RUN sandbox", Ui.VStack(14,
            Ui.VStack(6, Ui.Field("Network for RUN", network, "Default uses the builder's sandbox network. None disables networking for RUN steps. Host shares the builder host's network (--network).", "--network none"), networkWarning),
            Ui.Field("Shared memory size", shmSize, "Size of /dev/shm for RUN steps (--shm-size). Leave empty for the builder default.", "256m"),
            Ui.Field("Ulimits", ulimits, "Resource limits for RUN steps (--ulimit name=soft:hard). Names: " + string.Join(", ", BuildKitCommand.UlimitNames) + ".", "nofile = 1024:65536"),
            Ui.Field("Extra hosts", hosts, "Adds entries to /etc/hosts during RUN steps (--add-host). Use an IP address or host-gateway.", "db.internal = 10.0.0.12\nhost.docker.internal = host-gateway")));
    }

    public IReadOnlyList<KeyValuePair<string, string>> NamedContexts => contexts.Pairs();
    public bool SshDefault => sshDefault.IsChecked == true;
    public IReadOnlyList<KeyValuePair<string, string>> SshKeys => sshKeys.Pairs();
    public bool Sbom => sbom.IsChecked == true;
    public IReadOnlyList<KeyValuePair<string, string>> Annotations => annotations.Pairs();
    public string? ShmSize => string.IsNullOrWhiteSpace(shmSize.Text) ? null : shmSize.Text.Trim();
    public IReadOnlyList<KeyValuePair<string, string>> Ulimits => ulimits.Pairs();
    public IReadOnlyList<KeyValuePair<string, string>> ExtraHosts => hosts.Pairs();

    private static Expander Section(string title, UIElement body)
    {
        var expander = new Expander { Header = Loc.T(title), Content = body };
        System.Windows.Automation.AutomationProperties.SetName(expander, Loc.T(title));
        return expander;
    }
}
