using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpExtract.Models;

[JsonSerializable(typeof(McpToolsOutput))]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(McpTool))]
[JsonSerializable(typeof(McpParameter))]
[JsonSerializable(typeof(McpType))]
[JsonSerializable(typeof(McpbManifest))]
[JsonSerializable(typeof(McpbTool))]
[JsonSerializable(typeof(McpbServer))]
[JsonSerializable(typeof(McpbCompatibility))]
[JsonSerializable(typeof(McpbPrompt))]
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


// MCPB Manifest Models (successor to DXT)
public sealed class McpbManifest
{
    [JsonPropertyName("manifest_version")] public required string ManifestVersion { get; init; } // e.g. "0.2"
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    [JsonPropertyName("display_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisplayName { get; init; }
    [JsonPropertyName("long_description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LongDescription { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public McpbAuthor? Author { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public McpbRepository? Repository { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Homepage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Documentation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Support { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Icon { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Screenshots { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public McpbServer? Server { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<McpbTool>? Tools { get; init; }
    [JsonPropertyName("tools_generated"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ToolsGenerated { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<McpbPrompt>? Prompts { get; init; }
    [JsonPropertyName("prompts_generated"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? PromptsGenerated { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Keywords { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? License { get; init; }
    [JsonPropertyName("privacy_policies"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? PrivacyPolicies { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public McpbCompatibility? Compatibility { get; init; }
    [JsonPropertyName("user_config"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, McpbUserConfigField>? UserConfig { get; init; }
}

public sealed class McpbServer
{
    public required string Type { get; init; } // node, python, binary
    [JsonPropertyName("entry_point")] public required string EntryPoint { get; init; }
    [JsonPropertyName("mcp_config"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public McpbMcpConfig? McpConfig { get; init; }
}

public sealed class McpbMcpConfig
{
    public required string Command { get; init; }
    public required List<string> Args { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string,string>? Env { get; init; }
    [JsonPropertyName("platform_overrides"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, McpbPlatformOverride>? PlatformOverrides { get; init; }
}

public sealed class McpbPlatformOverride
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Command { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Args { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string,string>? Env { get; init; }
}

public sealed class McpbAuthor
{
    public required string Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; init; }
}

public sealed class McpbRepository
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; init; }
}

public sealed class McpbUserConfigField
{
    public required string Type { get; init; } // string, number, boolean, directory, file
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Required { get; init; }
    [JsonPropertyName("default"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public object? DefaultValue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Multiple { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Sensitive { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Min { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Max { get; init; }
}

public sealed class McpbTool
{
    public required string Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    // MCPB spec currently only needs name + description for static declaration
}

public sealed class McpbPrompt
{
    public required string Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Arguments { get; init; }
    public required string Text { get; init; }
}

public sealed class McpbCompatibility
{
    [JsonPropertyName("claude_desktop"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ClaudeDesktop { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Platforms { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string,string>? Runtimes { get; init; }
}
