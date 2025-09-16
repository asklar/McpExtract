// This is a minimal test implementation to demonstrate the expected structure for .NET 9
// In a real scenario, you would reference the actual MCP SDK

using System;

namespace TestMcp9;

[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerToolAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class McpToolParameterAttribute : Attribute
{
    public string? Description { get; set; }
}

public class TestTools
{
    [McpServerTool(Name = "net9_feature", Description = "A feature that only works in .NET 9")]
    public Task<string> Net9Feature(
        [McpToolParameter(Description = "Input for the .NET 9 feature")] string input)
    {
        return Task.FromResult($"Processed in .NET 9: {input}");
    }

    [McpServerTool(Name = "modern_async", Description = "Uses modern async patterns")]
    public async Task<int> ModernAsyncOperation(
        [McpToolParameter(Description = "The number to process")] int number,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return number * 2;
    }
}