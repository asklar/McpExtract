# MCPB Schema Code Generator

Generates C# POCO models from the upstream MCPB Zod schema definitions.

## Usage

```pwsh
cd codegen
npm install
npm run generate -- --out ../src --namespace McpExtract.McpbSchema
```

Options:
- `--schema-url` Override schema source (default: official anthropics repo `schemas.ts` main branch)
- `--out` Output directory for generated C# file (default: `../src`)
- `--namespace` Root C# namespace (default: `McpExtract.McpbSchema`)
- `--force` Overwrite existing file even if unchanged

Generated file: `McpbSchemaModels.cs`.
