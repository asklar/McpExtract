# McpExtract

A command line tool that extracts Model Context Protocol (MCP) tool metadata from .NET assemblies and outputs it as JSON, Python function definitions, or MCPB manifests.

## Installation

### Install as a .NET Global Tool

McpExtract is available as a .NET global tool and can be installed from NuGet:

```bash
dotnet tool install -g McpExtract.Tool
```

Once installed, you can run the tool from anywhere using:

```bash
mcp-extract --help
```

### Update to Latest Version

To update to the latest version:

```bash
dotnet tool update -g McpExtract.Tool
```

### Uninstall

To uninstall the tool:

```bash
dotnet tool uninstall -g McpExtract.Tool
```

### Local Development Installation

For local development, you can install from source:

```bash
git clone https://github.com/asklar/McpExtract.git
cd McpExtract
dotnet pack
dotnet tool install -g McpExtract.Tool --add-source ./bin/Release
```

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

It outputs structured JSON, Python, or MCPB manifest formats, containing details about each tool including:

- Tool name and description
- Parameter information with types and descriptions
- Return type information
- Method and class names

## Features

- **Cross-TFM Compatibility**: Uses `MetadataLoadContext` for isolated assembly analysis, enabling analysis of assemblies targeting any .NET version (net6.0, net8.0, net9.0, etc.) without version conflicts
- **AOT-Compatible**: Built with .NET 8 and native AOT support
- **No LINQ**: Uses only loops and direct method calls for maximum compatibility
- **System.Text.Json Source Generators**: Uses compile-time JSON serialization for performance
- **Isolated Assembly Analysis**: Analyzes target assemblies without loading them into the current runtime context
- **Multiple Output Formats**: Supports JSON, Python function definitions, and MCPB manifests
- **Command Line Interface**: Built with System.CommandLine for robust argument parsing
- **Parameter Descriptions**: Extracts descriptions from `[Description]` attributes on methods and parameters

## Workflow Integration

### Typical Cross-Team Workflow

1. **MCP Development Team** builds and compiles MCP servers with rich tool annotations
2. **CI/CD Pipeline** runs McpExtract against the compiled assemblies to extract tool metadata
3. **Training Data Pipeline** consumes the JSON/Python output to generate model training data
4. **MCPB Integration** uses the MCPB manifest output to package MCP servers for distribution
4. **ML Team** uses the extracted metadata to retrain tool-calling models with current tool definitions

### Example Integration

```bash
# In your CI/CD pipeline
dotnet build MyMcpServer.csproj
mcp-extract bin/Release/net8.0/MyMcpServer.dll --output tools.json
mcp-extract bin/Release/net8.0/MyMcpServer.dll --output training_functions.py --format python
mcp-extract bin/Release/net8.0/MyMcpServer.dll --output manifest.json --format MCPB

# Training pipeline can now use tools.json for metadata and training_functions.py as reference
# MCPB systems can use manifest.json for MCP server distribution and integration
```

## Usage

### Quick Start

After installing the global tool:

```bash
# Show help and version information
mcp-extract --help
mcp-extract --version

# Basic usage - analyze a .NET assembly and output JSON to console
mcp-extract MyMcpServer.dll

# Output JSON to file
mcp-extract MyMcpServer.dll --output tools.json

# Generate Python function definitions
mcp-extract MyMcpServer.dll --format python

# Generate MCPB manifest
mcp-extract MyMcpServer.dll --format mcpb
```

### Detailed Usage

```bash
# Basic usage - output JSON to console
mcp-extract <assembly-path>

# Output JSON to file
mcp-extract <assembly-path> --output <output-file>

# Output Python function definitions
mcp-extract <assembly-path> --format python

# Output Python to file
mcp-extract <assembly-path> --output tools.py --format python

# Output MCPB manifest
mcp-extract <assembly-path> --format mcpb

# Output MCPB manifest to file
mcp-extract <assembly-path> --output manifest.json --format mcpb
```

### Command Line Options

- `<assembly-path>` - Path to the .NET assembly (.dll) to analyze (required)
- `-o, --output <file>` - Path for the output file. If not specified, outputs to console
- `-f, --format <json|python|mcpb>` - Output format: `json` (default), `python`, or `mcpb`
- `--help` - Show help and usage information

### Examples

```bash
# Analyze an MCP server assembly and output JSON to console
mcp-extract MyMcpServer.dll

# Analyze and save JSON to file
mcp-extract MyMcpServer.dll --output tools.json

# Generate Python function definitions
mcp-extract MyMcpServer.dll --format python

# Generate Python and save to file
mcp-extract MyMcpServer.dll --output tools.py --format python
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

### MCPB Format

When using `--format=mcpb`, the tool generates a MCPB manifest that follows the [MCPB specification](https://github.com/anthropics/mcpb/blob/main/MANIFEST.md):

The MCPB manifest includes:
- Assembly metadata (name, version, description, company)
- Server configuration for running the MCP server
- Tools array with names and descriptions extracted from MCP attributes
- Standard MCPB format for integration with MCPB-compatible systems

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

## Requirements

- .NET 8.0 or later runtime for running the tool
- Target assemblies can be built with any .NET version (.NET Framework 4.6.1+, .NET Core/5/6/7/8/9+, etc.)
- The tool uses `MetadataLoadContext` for isolated analysis, eliminating version conflicts between the tool and target assemblies

## Building from Source

```bash
git clone https://github.com/asklar/McpExtract.git
cd McpExtract
dotnet build
```

To create a release package:

```bash
dotnet pack
```

## Notes

- The tool uses `MetadataLoadContext` for isolated assembly analysis, enabling cross-version compatibility without loading assemblies into the current runtime
- Reference assemblies are automatically discovered for the target framework, with fallback to current runtime assemblies if needed
- CancellationToken parameters are automatically excluded from the analysis
- Task and Task<T> return types are unwrapped to show the actual return type
- Sibling dependencies (DLLs in the same directory) are included for proper type resolution
