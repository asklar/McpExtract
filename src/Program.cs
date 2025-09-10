using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using McpExtract.Analysis;
using McpExtract.Models;

namespace McpExtract;

public static class Program
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This tool requires reflection to analyze assemblies")]
    public static async Task<int> Main(string[] args)
    {
        var assemblyPathArgument = new Argument<FileInfo>(
            name: "assembly-path",
            description: "Path to the .NET assembly (.dll) to analyze");

        var outputPathOption = new Option<FileInfo?>(
            name: "--output",
            description: "Path for the output file. If not specified, outputs to console");
        outputPathOption.AddAlias("-o");

        var formatOption = new Option<string>(
            name: "--format",
            description: "Output format: json (default), python, or dxt",
            getDefaultValue: () => "json");
        formatOption.AddAlias("-f");
        formatOption.FromAmong("json", "python", "dxt");

        var rootCommand = new RootCommand("Extracts Model Context Protocol (MCP) tool metadata from .NET assemblies")
        {
            assemblyPathArgument,
            outputPathOption,
            formatOption
        };

        rootCommand.SetHandler(
            async (assemblyPath, outputPath, format) =>
            {
                await ProcessCommand(assemblyPath, outputPath, format);
            },
            assemblyPathArgument,
            outputPathOption,
            formatOption
        );

        return await rootCommand.InvokeAsync(args);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Assembly analysis requires unreferenced code access"
    )]
    private static async Task ProcessCommand(
        FileInfo assemblyPath,
        FileInfo? outputPath,
        string format
    )
    {
        try
        {
            if (!assemblyPath.Exists)
            {
                Console.WriteLine($"Error: Assembly file not found: {assemblyPath.FullName}");
                return;
            }

            var analyzer = new McpToolAnalyzer();
            var result = analyzer.AnalyzeAssembly(assemblyPath.FullName);

            string output;
            if (format == "python")
            {
                output = GeneratePythonOutput(result, assemblyPath.Name);
            }
            else if (format == "dxt")
            {
                output = GenerateDxtOutput(result, assemblyPath.Name);
            }
            else
            {
                // For JSON format, return the tools in the original format for backward compatibility
                var mcpOutput = new McpToolsOutput { Tools = result.Tools };
                output = JsonSerializer.Serialize(mcpOutput, JsonContext.Default.McpToolsOutput);
            }

            if (outputPath != null)
            {
                await File.WriteAllTextAsync(outputPath.FullName, output);
                Console.WriteLine($"Analysis complete. Output written to: {outputPath.FullName}");
                Console.WriteLine($"Found {result.Tools.Count} MCP tools.");
            }
            else
            {
                Console.WriteLine(output);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static string GeneratePythonOutput(AnalysisResult result, string assemblyFileName)
    {
        var python = new StringBuilder();
        python.AppendLine("# Auto-generated Python function definitions for MCP tools");
        python.AppendLine($"# Generated from .NET assembly: {assemblyFileName}");
        python.AppendLine();

        foreach (var tool in result.Tools)
        {
            // Annotate with server class
            if (!string.IsNullOrEmpty(tool.ClassName))
            {
                python.AppendLine($"# Server: {tool.ClassName}");
            }
            // Add function comment with description
            if (!string.IsNullOrEmpty(tool.Description))
            {
                python.AppendLine($"# {tool.Description}");
            }
            // Build function signature
            python.Append($"def {SanitizePythonName(tool.Name)}(");
            var paramStrings = new List<string>();
            foreach (var param in tool.Parameters)
            {
                var paramString = SanitizePythonName(param.Name);
                // Add type hint
                var pythonType = ConvertToPythonType(param.Type);
                paramString += $": {pythonType}";
                // Add default value if not required
                if (!param.IsRequired)
                {
                    paramString += " = None";
                }
                paramStrings.Add(paramString);
            }
            python.Append(string.Join(", ", paramStrings));
            python.Append(")");
            // Add return type hint
            var returnType = ConvertToPythonType(tool.ReturnType);
            python.AppendLine($" -> {returnType}:");
            // Add docstring
            python.AppendLine("    \"\"");
            if (!string.IsNullOrEmpty(tool.Description))
            {
                python.AppendLine($"    {tool.Description}");
                python.AppendLine();
            }
            if (!string.IsNullOrEmpty(tool.ClassName))
            {
                python.AppendLine($"    Server: {tool.ClassName}");
                python.AppendLine();
            }
            if (tool.Parameters.Count > 0)
            {
                python.AppendLine("    Args:");
                foreach (var param in tool.Parameters)
                {
                    var paramType = ConvertToPythonType(param.Type);
                    var desc = string.IsNullOrEmpty(param.Description)
                        ? "No description provided"
                        : param.Description;
                    python.AppendLine(
                        $"        {SanitizePythonName(param.Name)} ({paramType}): {desc}"
                    );
                }
                python.AppendLine();
            }
            python.AppendLine($"    Returns:");
            python.AppendLine($"        {returnType}: The result of the operation");
            python.AppendLine("    \"\"");
            python.AppendLine("    # Implementation would go here");
            python.AppendLine("    pass");
            python.AppendLine();
        }

        return python.ToString();
    }

    private static string GenerateDxtOutput(AnalysisResult result, string assemblyFileName)
    {
        // Convert MCP tools to DXT tools format
        var dxtTools = new List<DxtTool>();
        foreach (var tool in result.Tools)
        {
            dxtTools.Add(new DxtTool
            {
                Name = tool.Name,
                Description = tool.Description
            });
        }

        // Extract assembly name without extension for manifest name
        var manifestName = Path.GetFileNameWithoutExtension(assemblyFileName);

        // Create DXT manifest
        var manifest = new DxtManifest
        {
            DxtVersion = "0.1",
            Name = manifestName,
            Version = result.AssemblyVersion ?? "1.0.0",
            Description = result.AssemblyDescription ?? $"MCP server extracted from {assemblyFileName}",
            Author = new DxtAuthor
            {
                Name = result.AssemblyCompany ?? "Unknown",
                Email = null,
                Url = null
            },
            Server = new DxtServer
            {
                Type = "binary",
                EntryPoint = assemblyFileName,
                McpConfig = new DxtMcpConfig
                {
                    Command = "dotnet",
                    Args = new List<string> { $"${{__dirname}}/{assemblyFileName}" }
                }
            },
            Tools = dxtTools.Count > 0 ? dxtTools : null
        };

        return JsonSerializer.Serialize(manifest, JsonContext.Default.DxtManifest);
    }

    private static string SanitizePythonName(string name)
    {
        // Replace invalid characters and ensure it starts with letter or underscore
        var sanitized = new StringBuilder();
        
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sanitized.Append(c);
            }
            else
            {
                sanitized.Append('_');
            }
        }
        
        var result = sanitized.ToString();
        
        // Ensure it starts with letter or underscore
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result = "_" + result;
        }
        
        return string.IsNullOrEmpty(result) ? "_unnamed" : result;
    }

    private static string ConvertToPythonType(McpType mcpType)
    {
        var baseType = mcpType.TypeName.ToLowerInvariant() switch
        {
            "string" => "str",
            "int" => "int",
            "long" => "int",
            "double" => "float",
            "float" => "float",
            "bool" => "bool",
            "datetime" => "datetime",
            "guid" => "str",
            "object" => "Any",
            "void" => "None",
            _ => "Any",
        };

        if (mcpType.IsArray && mcpType.ElementType != null)
        {
            var elementType = ConvertToPythonType(mcpType.ElementType);
            baseType = $"List[{elementType}]";
        }

        if (mcpType.IsNullable && baseType != "None")
        {
            baseType = $"Optional[{baseType}]";
        }

        return baseType;
    }
}
