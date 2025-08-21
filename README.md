# McpExtract

A command line tool that extracts Model Context Protocol (MCP) tool metadata from .NET assemblies and outputs it as JSON, Python function definitions, or DXT manifests.

## Why This Tool Matters

This tool bridges a critical gap between MCP server development and AI model training workflows:

**The Challenge**: Organizations often have separate teams working on different parts of the AI ecosystem:
- **MCP Server Development Teams** build sophisticated MCP servers with rich tool sets
- **AI/ML Teams** develop and train local tool-calling models that need comprehensive training data

**The Solution**: McpExtract automates the extraction of tool metadata from compiled MCP servers, enabling:
- **Accelerated Model Retraining**: Instead of manually documenting tools for training data, extract comprehensive metadata automatically
- **Consistent Training Data**: Ensure training data accurately reflects the actual tool signatures and descriptions
- **Rapid Iteration**: As MCP servers evolve, quickly regenerate training data to keep models up-to-date
- **Cross-Team Collaboration**: Enable ML teams to work with the latest tool definitions without requiring deep knowledge of the MCP server codebase

The Python output format is particularly valuable for ML workflows, providing ready-to-use function signatures that can be directly incorporated into training pipelines or used as reference implementations.

## Overview

This tool scans .NET DLL files that use the [MCP SDK for C#](https://github.com/modelcontextprotocol/csharp-sdk/) and extracts information about methods annotated with `[McpServerTool(...)]` attributes. 

It outputs structured JSON, Python, or DXT manifest formats, containing details about each tool including:

- Tool name and description
- Parameter information with types and descriptions
- Return type information
- Method and class names

## Features

- **AOT-Compatible**: Built with .NET 9 and native AOT support
- **No LINQ**: Uses only loops and direct method calls for maximum compatibility
- **System.Text.Json Source Generators**: Uses compile-time JSON serialization for performance
- **Reflection-Based Analysis**: Analyzes target assemblies without requiring them as dependencies
- **Multiple Output Formats**: Supports JSON, Python function definitions, and DXT manifests
- **Command Line Interface**: Built with System.CommandLine for robust argument parsing
- **Parameter Descriptions**: Extracts descriptions from `[Description]` attributes on methods and parameters

## Workflow Integration

### Typical Cross-Team Workflow

1. **MCP Development Team** builds and compiles MCP servers with rich tool annotations
2. **CI/CD Pipeline** runs McpExtract against the compiled assemblies to extract tool metadata
3. **Training Data Pipeline** consumes the JSON/Python output to generate model training data
4. **DXT Integration** uses the DXT manifest output to package MCP servers for distribution
4. **ML Team** uses the extracted metadata to retrain tool-calling models with current tool definitions

### Example Integration

```bash
# In your CI/CD pipeline
dotnet build MyMcpServer.csproj
McpExtract bin/Release/net9.0/MyMcpServer.dll --output tools.json
McpExtract bin/Release/net9.0/MyMcpServer.dll --output training_functions.py --format python
McpExtract bin/Release/net9.0/MyMcpServer.dll --output manifest.json --format dxt

# Training pipeline can now use tools.json for metadata and training_functions.py as reference
# DXT systems can use manifest.json for MCP server distribution and integration
```

## Usage

```bash
# Basic usage - output JSON to console
McpExtract <assembly-path>

# Output JSON to file
McpExtract <assembly-path> --output <output-file>

# Output Python function definitions
McpExtract <assembly-path> --format python

# Output Python to file
McpExtract <assembly-path> --output tools.py --format python

# Output DXT manifest
McpExtract <assembly-path> --format dxt

# Output DXT manifest to file
McpExtract <assembly-path> --output manifest.json --format dxt
```

### Command Line Options

- `<assembly-path>` - Path to the .NET assembly (.dll) to analyze (required)
- `-o, --output <file>` - Path for the output file. If not specified, outputs to console
- `-f, --format <json|python|dxt>` - Output format: `json` (default), `python`, or `dxt`
- `--help` - Show help and usage information

### Examples

```bash
# Analyze an MCP server assembly and output JSON to console
McpExtract MyMcpServer.dll

# Analyze and save JSON to file
McpExtract MyMcpServer.dll --output tools.json

# Generate Python function definitions
McpExtract MyMcpServer.dll --format python

# Generate Python and save to file
McpExtract MyMcpServer.dll --output tools.py --format python
```

## Output Formats

### JSON Format

The tool outputs JSON in the following structure:

```json
{
  "tools": [
    {
      "name": "echo",
      "description": "Echo the input message",
      "parameters": [
        {
          "name": "message", 
          "description": "The message to echo",
          "type": {
            "typeName": "string",
            "isNullable": true,
            "isArray": false
          },
          "isRequired": true
        }
      ],
      "returnType": {
        "typeName": "string",
        "isNullable": true,
        "isArray": false
      },
      "methodName": "Echo",
      "className": "MyMcpServer.Tools.EchoTool"
    }
  ]
}
```

### Python Format

When using `--format=python`, the tool generates Python function definitions with type hints and docstrings:

```python
# Auto-generated Python function definitions for MCP tools
# Generated from .NET assembly analysis

# Echo the input message
def echo(message: str) -> Optional[str]:
    """
    Echo the input message

    Args:
        message (str): The message to echo

    Returns:
        Optional[str]: The result of the operation
    """
    # Implementation would go here
    pass
```

### DXT Format

When using `--format=dxt`, the tool generates a DXT manifest that follows the [DXT specification](https://github.com/anthropics/dxt/blob/main/MANIFEST.md):

```json
{
  "dxt_version": "0.1",
  "name": "MyMcpServer",
  "version": "1.0.0.0",
  "description": "MCP server extracted from MyMcpServer.dll",
  "author": {
    "name": "CompanyName",
    "email": null,
    "url": null
  },
  "server": {
    "type": "binary",
    "entry_point": "MyMcpServer.dll",
    "mcp_config": {
      "command": "dotnet",
      "args": ["${__dirname}/MyMcpServer.dll"]
    }
  },
  "tools": [
    {
      "name": "echo",
      "description": "Echo the input message"
    }
  ]
}
```

The DXT manifest includes:
- Assembly metadata (name, version, description, company)
- Server configuration for running the MCP server
- Tools array with names and descriptions extracted from MCP attributes
- Standard DXT format for integration with DXT-compatible systems

## Supported MCP Attributes

- `[McpServerTool(Name = "...", Description = "...")]` - Marks a method as an MCP tool
- `[McpToolParameter(Description = "...")]` - Provides parameter descriptions

## Type Support

The analyzer recognizes and properly handles:

- Primitive types (string, int, bool, etc.)
- Nullable types
- Arrays and collections (List<T>, IEnumerable<T>, etc.)
- Task and Task<T> return types
- Custom classes and structures

## Building

```bash
dotnet build
```

For AOT publication:

```bash
dotnet publish -c Release
```

## Requirements

- .NET 9.0 or later
- Target assembly must be built with .NET Framework 4.6.1+ or .NET Core/5+

## Notes

- The tool loads the target assembly using reflection, so it should be compatible with the current runtime
- CancellationToken parameters are automatically excluded from the analysis
- Task and Task<T> return types are unwrapped to show the actual return type
