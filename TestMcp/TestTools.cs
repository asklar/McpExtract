// This is a minimal test implementation to demonstrate the expected structure
// In a real scenario, you would reference the actual MCP SDK

using System;

namespace TestMcp;

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
    [McpServerTool(Name = "echo", Description = "Echoes the input message")]
    public Task<string> Echo(
        [McpToolParameter(Description = "The message to echo")] string message)
    {
        return Task.FromResult(message);
    }

    [McpServerTool(Name = "add", Description = "Adds two numbers")]
    public Task<int> Add(
        [McpToolParameter(Description = "First number")] int a,
        [McpToolParameter(Description = "Second number")] int b)
    {
        return Task.FromResult(a + b);
    }

    [McpServerTool(Name = "get_weather", Description = "Gets weather information")]
    public Task<object> GetWeather(
        [McpToolParameter(Description = "City name")] string city,
        [McpToolParameter(Description = "Include forecast")] bool includeForecast = false,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<object>(new { city, temperature = 22 });
    }
}
