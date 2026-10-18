using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

public static class StructuredInputs
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    public static string[] Lines(string text) => text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static void Token(string value)
    {
        if(value.Length > 32768 || value.Any(char.IsControl)) throw new ArgumentException("Values cannot contain control characters and must fit within 32 KiB.");
    }
    public static Dictionary<string,string> Pairs(string text, bool labels=false)
    {
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var row in text.Split(['\r','\n'],StringSplitOptions.None))
        {
            if(string.IsNullOrWhiteSpace(row))continue;
            var separator=row.IndexOf('='); if(separator<1)throw new ArgumentException("Use one NAME=value entry per line.");
            var key=row[..separator]; var value=row[(separator+1)..]; Token(value);
            var validKey=labels ? !key.StartsWith('-')&&!key.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)||c=='=') : Regex.IsMatch(key,@"^[A-Za-z_][A-Za-z0-9_]*$",RegexOptions.None,Timeout);
            if(!validKey)throw new ArgumentException("A key has invalid characters.");
            if(!values.TryAdd(key,value))throw new ArgumentException("Duplicate keys are not allowed.");
        }
        return values;
    }
    public static string[] Argv(string json)
    {
        if(string.IsNullOrWhiteSpace(json))return [];
        if(json.Length>32768)throw new ArgumentException("Command arguments exceed the 32 KiB limit.");
        try
        {
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=2 });
            if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Command argv must be a JSON array of strings.");
            var list=new List<string>(); foreach(var item in doc.RootElement.EnumerateArray()) { if(item.ValueKind!=JsonValueKind.String)throw new ArgumentException("Every command argument must be a string."); var value=item.GetString()!; Token(value); list.Add(value); }
            if(list.Count>256)throw new ArgumentException("At most 256 command arguments are supported.");
            return list.ToArray();
        }
        catch(JsonException) { throw new ArgumentException("Command argv is not valid JSON."); }
    }
    public static double? Cpus(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(!double.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<=0)throw new ArgumentException("CPU limit must be a positive decimal number, for example 1.5.");
        return value;
    }
    public static string? Memory(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(!Regex.IsMatch(text,@"^[1-9][0-9]*(?:[bkmg])?$",RegexOptions.IgnoreCase,Timeout))throw new ArgumentException("Memory uses positive bytes or b/k/m/g units, for example 512m.");return text;
    }
    public static string Port(string address,string host,string container,string protocol)
    {
        if(!int.TryParse(host,out var hp)||hp<1||hp>65535||!int.TryParse(container,out var cp)||cp<1||cp>65535)throw new ArgumentException("Ports must be whole numbers between 1 and 65535.");
        if(protocol is not ("tcp" or "udp"))throw new ArgumentException("Choose TCP or UDP.");
        if(!IPAddress.TryParse(address,out var ip))throw new ArgumentException("Use an explicit IPv4 or IPv6 bind address.");
        return (ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6?"["+address+"]":address)+":"+hp+":"+cp+"/"+protocol;
    }
    public static string Mount(string type,string source,string destination,bool readOnly)
    {
        if(type is not ("bind" or "volume"))throw new ArgumentException("Choose bind or volume mount type.");
        foreach(var value in new[]{source,destination}) { Token(value); if(string.IsNullOrWhiteSpace(value)||value.Contains(',')||value.StartsWith('-'))throw new ArgumentException("Mount source and destination are required and cannot contain commas or begin with a hyphen."); }
        if(type=="volume"&&!Regex.IsMatch(source,@"^[A-Za-z0-9][A-Za-z0-9_.-]*$",RegexOptions.None,Timeout))throw new ArgumentException("Named volume identifiers use letters, digits, underscores, periods and hyphens.");
        if(!IsEngineAbsolutePath(destination))throw new ArgumentException("Mount destination must be absolute on the container's operating system.");
        if(type=="bind"&&!IsEngineAbsolutePath(source))throw new ArgumentException("Bind source must be an absolute path on the engine host.");
        return $"type={type},source={source},target={destination}"+(readOnly?",readonly":"");
    }
    private static bool IsEngineAbsolutePath(string path) => path.StartsWith('/')||path.StartsWith("\\\\",StringComparison.Ordinal)||Regex.IsMatch(path,@"^[A-Za-z]:[\\/]",RegexOptions.None,Timeout);
    public static string Secret(string id,string path)
    {
        if(!Regex.IsMatch(id,@"^[A-Za-z_][A-Za-z0-9_.-]*$",RegexOptions.None,Timeout))throw new ArgumentException("Secret identifier must start with a letter or underscore.");
        Token(path); if(string.IsNullOrWhiteSpace(path)||path.Contains(','))throw new ArgumentException("Choose a secret source file with no comma in its path.");
        return "type=file,id="+id+",src="+path;
    }
    public static string Export(string path)
    {
        Token(path);if(string.IsNullOrWhiteSpace(path)||path.Contains(','))throw new ArgumentException("Choose an OCI archive path with no comma.");return "type=oci,dest="+path;
    }
    public static string Cache(string spec)
    {
        Token(spec); if(!Regex.IsMatch(spec,@"^type=(?:inline|local|registry|gha|s3|azblob)(?:,[A-Za-z0-9_-]+=[^,\r\n]+)*$",RegexOptions.None,Timeout))throw new ArgumentException("Cache specification must use type=inline/local/registry/gha/s3/azblob and comma-separated key=value options.");
        return spec;
    }
}
