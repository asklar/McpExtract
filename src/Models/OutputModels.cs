using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpExtract.Models;

[JsonSerializable(typeof(McpToolsOutput))]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(McpTool))]
[JsonSerializable(typeof(McpParameter))]
[JsonSerializable(typeof(McpType))]
[JsonSerializable(typeof(DxtManifest))]
[JsonSerializable(typeof(DxtTool))]
[JsonSerializable(typeof(DxtAuthor))]
[JsonSerializable(typeof(DxtServer))]
[JsonSerializable(typeof(DxtMcpConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class JsonContext : JsonSerializerContext
{
}

public sealed class McpToolsOutput
{
    public required List<McpTool> Tools { get; init; }
}

public sealed class AnalysisResult
{
    public required List<McpTool> Tools { get; init; }
    public string? AssemblyName { get; init; }
    public string? AssemblyVersion { get; init; }
    public string? AssemblyDescription { get; init; }
    public string? AssemblyCompany { get; init; }
    public string? AssemblyProduct { get; init; }
}

public sealed class McpTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required List<McpParameter> Parameters { get; init; }
    public required McpType ReturnType { get; init; }
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
}

public sealed class McpParameter
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required McpType Type { get; init; }
    public required bool IsRequired { get; init; }
}

public sealed class McpType
{
    public required string TypeName { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsArray { get; init; }
    public McpType? ElementType { get; init; }
}

// DXT Manifest Models
public sealed class DxtManifest
{
    [JsonPropertyName("dxt_version")]
    public required string DxtVersion { get; init; }
    
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required DxtAuthor Author { get; init; }
    public required DxtServer Server { get; init; }
    public List<DxtTool>? Tools { get; init; }
}

public sealed class DxtTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public sealed class DxtAuthor
{
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? Url { get; init; }
}

public sealed class DxtServer
{
    public required string Type { get; init; }
    
    [JsonPropertyName("entry_point")]
    public required string EntryPoint { get; init; }
    
    [JsonPropertyName("mcp_config")]
    public required DxtMcpConfig McpConfig { get; init; }
}

public sealed class DxtMcpConfig
{
    public required string Command { get; init; }
    public required List<string> Args { get; init; }
}
