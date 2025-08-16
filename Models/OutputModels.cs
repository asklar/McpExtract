using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpExtract.Models;

[JsonSerializable(typeof(McpToolsOutput))]
[JsonSerializable(typeof(McpTool))]
[JsonSerializable(typeof(McpParameter))]
[JsonSerializable(typeof(McpType))]
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
