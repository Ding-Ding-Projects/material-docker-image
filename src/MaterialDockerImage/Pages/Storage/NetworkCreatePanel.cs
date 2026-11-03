using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Side panel form for <c>docker network create</c> with validation and a live command preview.</summary>
public sealed class NetworkCreatePanel : Border
{
    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$");
    private readonly TextBox name = Ui.Input("", "Name", true);
    private readonly ComboBox driver = Ui.Combo(["bridge", "overlay", "macvlan", "ipvlan"], "bridge");
    private readonly TextBox subnet = Ui.Input("", "Subnet", true);
    private readonly TextBox gateway = Ui.Input("", "Gateway", true);
    private readonly TextBox ipRange = Ui.Input("", "IP range", true);
    private readonly TextBox parent = Ui.Input("", "Parent interface", true);
    private readonly CheckBox isInternal;
    private readonly CheckBox attachable;
    private readonly CheckBox ipv6;
    private readonly KeyValueRows labels = new("Label", "Value");
    private readonly TextBlock preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
    private readonly TextBlock hint = Ui.Caption("");
    private readonly TextBlock error = Ui.Text("", 13, null, "Error");
    private readonly FrameworkElement parentField;
    private Func<IReadOnlyList<string>> existingSubnets = () => [];
    public event Action? Closed;
    public Func<IReadOnlyList<string>, string, Task<bool>>? Create { get; set; }

    public NetworkCreatePanel()
    {
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(16);
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        isInternal = Ui.Switch("Internal", false, _ => Update(), "No route to the internet or the host");
        attachable = Ui.Switch("Attachable", false, _ => Update(), "Standalone containers can join an overlay later");
        ipv6 = Ui.Switch("IPv6", false, _ => Update(), "Assign IPv6 addresses (needs an IPv6 subnet or engine pool)");
        parentField = Ui.Field("Parent interface", parent, "macvlan and ipvlan attach to a host interface, passed as -o parent=NAME.", "eth0");
        var closeButton = Ui.IconButton(Icons.Close, "Close create network", () => Closed?.Invoke());
        var title = new DockPanel();
        title.Children.Add(closeButton.Dock(Dock.Right));
        title.Children.Add(Ui.Text("Create network", 18, FontWeights.SemiBold));
        var create = Ui.AsyncButton("Create", Icons.Add, ButtonKind.Filled, async () =>
        {
            var args = Args(out var problem);
            if (args == null) { error.Text = problem ?? ""; return; }
            if (Create != null && await Create(args, name.Text.Trim())) Reset();
        });
        Child = Ui.Scroll(Ui.VStack(14,
            title,
            Ui.Field("Name", name, "A unique network name on this engine. Containers on the same user-defined network resolve each other by name.", "hello-backend"),
            Ui.Field("Driver", driver, "bridge: single-host private network. overlay: spans Swarm nodes. macvlan: containers appear as physical devices on the host LAN. ipvlan: shares the parent interface's MAC address.", "bridge"),
            parentField,
            Ui.Columns(12, [1, 1], Ui.Field("Subnet", subnet, "CIDR block for container addresses. Leave empty to let the engine choose a free range.", "172.28.0.0/16"), Ui.Field("Gateway", gateway, "Gateway address inside the subnet. Requires a subnet.", "172.28.0.1")),
            Ui.Field("IP range", ipRange, "Allocate container addresses only from this CIDR sub-range of the subnet. Requires a subnet.", "172.28.5.0/24"),
            hint,
            isInternal, attachable, ipv6,
            Ui.Field("Labels", labels, "Metadata stored on the network for filtering and tooling.", "com.docker.compose.project=hello-stack"),
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12),
            error,
            Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, () => Closed?.Invoke()), create)).Margin(0, 0, 8, 0));
        name.TextChanged += (_, _) => Update(); subnet.TextChanged += (_, _) => Update(); gateway.TextChanged += (_, _) => Update(); ipRange.TextChanged += (_, _) => Update(); parent.TextChanged += (_, _) => Update();
        driver.SelectionChanged += (_, _) => Update();
        labels.Changed += Update;
        Update();
    }

    public void SetExistingSubnets(Func<IReadOnlyList<string>> subnets) { existingSubnets = subnets; Update(); }

    private void Reset()
    {
        name.Text = subnet.Text = gateway.Text = ipRange.Text = parent.Text = "";
        isInternal.IsChecked = attachable.IsChecked = ipv6.IsChecked = false;
        Update();
    }

    private static (IPAddress Address, int Prefix)? ParseCidr(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix)) return null;
        var max = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return prefix < 0 || prefix > max ? null : (ip, prefix);
    }

    private static bool Contains((IPAddress Address, int Prefix) net, IPAddress ip)
    {
        var a = net.Address.GetAddressBytes(); var b = ip.GetAddressBytes();
        if (a.Length != b.Length) return false;
        for (var bit = 0; bit < net.Prefix; bit++)
        {
            var mask = (byte)(0x80 >> (bit % 8));
            if ((a[bit / 8] & mask) != (b[bit / 8] & mask)) return false;
        }
        return true;
    }

    private static bool Overlaps((IPAddress Address, int Prefix) x, (IPAddress Address, int Prefix) y)
    {
        var shorter = x.Prefix <= y.Prefix ? x : y; var other = x.Prefix <= y.Prefix ? y : x;
        return Contains(shorter, other.Address);
    }

    private List<string>? Args(out string? problem)
    {
        problem = null;
        var n = name.Text.Trim();
        if (n.Length == 0) { problem = "Enter a network name."; return null; }
        if (!NamePattern.IsMatch(n)) { problem = "Names start with a letter or digit and use letters, digits, '_', '.' or '-'."; return null; }
        if (n is "bridge" or "host" or "none") { problem = $"\"{n}\" is a built-in network name."; return null; }
        var d = driver.SelectedItem as string ?? "bridge";
        var args = new List<string> { "network", "create", "--driver", d };
        var s = subnet.Text.Trim(); var g = gateway.Text.Trim(); var r = ipRange.Text.Trim();
        (IPAddress Address, int Prefix)? sn = null;
        if (s.Length > 0)
        {
            sn = ParseCidr(s);
            if (sn == null) { problem = "Subnet must be CIDR notation such as 172.28.0.0/16."; return null; }
            args.AddRange(["--subnet", s]);
        }
        if (g.Length > 0)
        {
            if (sn == null) { problem = "A gateway needs a subnet."; return null; }
            if (!IPAddress.TryParse(g, out var gw) || !Contains(sn.Value, gw)) { problem = "Gateway must be an address inside the subnet."; return null; }
            args.AddRange(["--gateway", g]);
        }
        if (r.Length > 0)
        {
            if (sn == null) { problem = "An IP range needs a subnet."; return null; }
            var range = ParseCidr(r);
            if (range == null || !Contains(sn.Value, range.Value.Address) || range.Value.Prefix < sn.Value.Prefix) { problem = "IP range must be a CIDR block inside the subnet."; return null; }
            args.AddRange(["--ip-range", r]);
        }
        if (d is "macvlan" or "ipvlan" && parent.Text.Trim() is { Length: > 0 } p)
        {
            if (p.StartsWith('-') || p.Any(char.IsWhiteSpace)) { problem = "Parent interface names cannot contain spaces or begin with '-'."; return null; }
            args.AddRange(["--opt", "parent=" + p]);
        }
        if (isInternal.IsChecked == true) args.Add("--internal");
        if (attachable.IsChecked == true) args.Add("--attachable");
        if (ipv6.IsChecked == true) args.Add("--ipv6");
        var (labs, e) = labels.TryValues();
        if (labs == null) { problem = e; return null; }
        foreach (var l in labs) args.AddRange(["--label", l.Key + "=" + l.Value]);
        args.Add(n);
        return args;
    }

    private void Update()
    {
        var d = driver.SelectedItem as string ?? "bridge";
        parentField.Visibility = d is "macvlan" or "ipvlan" ? Visibility.Visible : Visibility.Collapsed;
        var args = Args(out var problem);
        error.Text = name.Text.Length == 0 ? "" : problem ?? "";
        preview.Text = args == null ? Loc.T("Complete the form to see the exact command.") : "docker " + string.Join(' ', args.Select(VolumeCreateForm.Quote));
        hint.Text = "";
        if (ParseCidr(subnet.Text.Trim()) is { } sn)
        {
            var bits = (sn.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32) - sn.Prefix;
            var usable = bits >= 63 ? "a very large number of" : bits <= 1 ? "0" : (Math.Pow(2, bits) - 2).ToString("N0");
            var clash = existingSubnets().Select(x => (Text: x, Net: ParseCidr(x))).Where(x => x.Net is { } other && Overlaps(sn, other)).Select(x => x.Text).ToList();
            hint.Text = $"{usable} usable addresses. " + (clash.Count == 0 ? "Does not overlap the subnets of networks listed on this page." : "Overlaps " + string.Join(", ", clash) + "; the engine will refuse it.");
            hint.SetResourceReference(TextBlock.ForegroundProperty, clash.Count == 0 ? "OnSurfaceVariant" : "Error");
        }
    }
}
