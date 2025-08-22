using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using McpExtract.Models;

namespace McpExtract.Analysis;

public sealed class McpToolAnalyzer
{
    private const string McpServerToolAttributeName = "McpServerToolAttribute";
    private const string McpToolParameterAttributeName = "McpToolParameterAttribute";

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Assembly loading is required for analysis")]
    [UnconditionalSuppressMessage("Trimming", "IL2062", Justification = "Types analysis is required for tool discovery")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Dynamic access to types is required for analysis")]
    [RequiresUnreferencedCode("Assembly analysis requires unreferenced code access")]
    public AnalysisResult AnalyzeAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");
        }

        var tools = new List<McpTool>();

        // Load the assembly for reflection
        var assembly = Assembly.LoadFrom(assemblyPath);
        
        // Extract assembly metadata
        var assemblyName = assembly.GetName();
        var assemblyVersion = assemblyName.Version?.ToString() ?? "1.0.0";
        
        string? assemblyDescription = null;
        string? assemblyCompany = null;
        string? assemblyProduct = null;
        
        try
        {
            var descriptionAttribute = assembly.GetCustomAttribute<System.Reflection.AssemblyDescriptionAttribute>();
            assemblyDescription = descriptionAttribute?.Description;
            
            var companyAttribute = assembly.GetCustomAttribute<System.Reflection.AssemblyCompanyAttribute>();
            assemblyCompany = companyAttribute?.Company;
            
            var productAttribute = assembly.GetCustomAttribute<System.Reflection.AssemblyProductAttribute>();
            assemblyProduct = productAttribute?.Product;
        }
        catch
        {
            // Ignore errors when reading attributes
        }
        
        // Also use metadata reader for additional analysis
        using var fileStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        var types = assembly.GetTypes();
        
        foreach (var type in types)
        {
            var methods = GetPublicMethods(type);
            
            foreach (var method in methods)
            {
                var mcpTool = AnalyzeMethod(method);
                if (mcpTool != null)
                {
                    tools.Add(mcpTool);
                }
            }
        }

        return new AnalysisResult 
        { 
            Tools = tools,
            AssemblyName = assemblyName.Name,
            AssemblyVersion = assemblyVersion,
            AssemblyDescription = assemblyDescription,
            AssemblyCompany = assemblyCompany,
            AssemblyProduct = assemblyProduct
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Dynamic access to public methods is required for MCP tool discovery")]
    private static MethodInfo[] GetPublicMethods([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
        return type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    private McpTool? AnalyzeMethod(MethodInfo method)
    {
        // Look for McpServerTool attribute
        object? mcpAttribute = null;
        var attributes = method.GetCustomAttributes(false);
        
        for (var i = 0; i < attributes.Length; i++)
        {
            if (attributes[i].GetType().Name == McpServerToolAttributeName)
            {
                mcpAttribute = attributes[i];
                break;
            }
        }

        if (mcpAttribute == null)
        {
            return null;
        }

        // Extract tool name and description from attribute
        var toolName = GetAttributeProperty<string>(mcpAttribute, "Name") ?? method.Name;
        var description = GetAttributeProperty<string>(mcpAttribute, "Description") ?? string.Empty;
        
        // Try alternative property names if description is empty
        if (string.IsNullOrEmpty(description))
        {
            description = GetAttributeProperty<string>(mcpAttribute, "description") ?? string.Empty;
        }
        
        // Look for separate Description attribute (common in MCP SDK)
        if (string.IsNullOrEmpty(description))
        {
            for (var i = 0; i < attributes.Length; i++)
            {
                var attrType = attributes[i].GetType();
                if (attrType.Name == "DescriptionAttribute" || attrType.FullName == "System.ComponentModel.DescriptionAttribute")
                {
                    description = GetAttributeProperty<string>(attributes[i], "Description") ?? string.Empty;
                    break;
                }
            }
        }

        // Analyze parameters
        var parameters = new List<McpParameter>();
        var methodParams = method.GetParameters();

        foreach (var param in methodParams)
        {
            // Skip special parameters like CancellationToken
            if (param.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            var mcpParam = AnalyzeParameter(param);
            parameters.Add(mcpParam);
        }

        // Analyze return type
        var returnType = AnalyzeType(method.ReturnType);

        return new McpTool
        {
            Name = toolName,
            Description = description,
            Parameters = parameters,
            ReturnType = returnType,
            MethodName = method.Name,
            ClassName = method.DeclaringType?.FullName ?? "Unknown",
        };
    }

    private McpParameter AnalyzeParameter(ParameterInfo parameter)
    {
        // Look for McpToolParameter attribute for description
        object? paramAttribute = null;
        var attributes = parameter.GetCustomAttributes(false);
        
        for (var i = 0; i < attributes.Length; i++)
        {
            if (attributes[i].GetType().Name == McpToolParameterAttributeName)
            {
                paramAttribute = attributes[i];
                break;
            }
        }

        var description = string.Empty;
        if (paramAttribute != null)
        {
            description = GetAttributeProperty<string>(paramAttribute, "Description") 
                ?? string.Empty;
        }
        
        // Look for separate Description attribute if still empty
        if (string.IsNullOrEmpty(description))
        {
            for (var i = 0; i < attributes.Length; i++)
            {
                var attrType = attributes[i].GetType();
                if (attrType.Name == "DescriptionAttribute" || attrType.FullName == "System.ComponentModel.DescriptionAttribute")
                {
                    description = GetAttributeProperty<string>(attributes[i], "Description") ?? string.Empty;
                    break;
                }
            }
        }

        var type = AnalyzeType(parameter.ParameterType);
        var isRequired = !parameter.HasDefaultValue && !type.IsNullable;

        return new McpParameter
        {
            Name = parameter.Name ?? "unknown",
            Description = description,
            Type = type,
            IsRequired = isRequired,
        };
    }

    private McpType AnalyzeType(Type type)
    {
        var isNullable = false;
        var actualType = type;

        // Handle nullable types
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            isNullable = true;
            actualType = type.GetGenericArguments()[0];
        }

        // Handle reference types (can be null in nullable context)
        if (!actualType.IsValueType)
        {
            isNullable = true; // Reference types are nullable by default
        }

        // Handle arrays and collections
        var isArray = false;
        McpType? elementType = null;

        if (actualType.IsArray)
        {
            isArray = true;
            elementType = AnalyzeType(actualType.GetElementType()!);
        }
        else if (actualType.IsGenericType)
        {
            var genericDef = actualType.GetGenericTypeDefinition();
            if (
                genericDef == typeof(List<>)
                || genericDef == typeof(IList<>)
                || genericDef == typeof(ICollection<>)
                || genericDef == typeof(IEnumerable<>))
            {
                isArray = true;
                elementType = AnalyzeType(actualType.GetGenericArguments()[0]);
            }
        }

        // Handle Task<T> return types
        if (actualType.IsGenericType && actualType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return AnalyzeType(actualType.GetGenericArguments()[0]);
        }

        // Handle Task return type
        if (actualType == typeof(Task))
        {
            return new McpType
            {
                TypeName = "void",
                IsNullable = false,
                IsArray = false,
            };
        }

        return new McpType
        {
            TypeName = GetTypeName(actualType),
            IsNullable = isNullable,
            IsArray = isArray,
            ElementType = elementType,
        };
    }

    private static string GetTypeName(Type type)
    {
        if (type == typeof(string))
        {
            return "string";
        }
        if (type == typeof(int))
        {
            return "int";
        }
        if (type == typeof(long))
        {
            return "long";
        }
        if (type == typeof(double))
        {
            return "double";
        }
        if (type == typeof(float))
        {
            return "float";
        }
        if (type == typeof(bool))
        {
            return "bool";
        }
        if (type == typeof(DateTime))
        {
            return "DateTime";
        }
        if (type == typeof(Guid))
        {
            return "Guid";
        }
        if (type == typeof(object))
        {
            return "object";
        }
        if (type == typeof(void))
        {
            return "void";
        }

        // Handle generic types properly
        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            var genericArguments = type.GetGenericArguments();
            
            // Get the base name without the `1, `2 etc.
            var baseName = genericDefinition.Name;
            var tickIndex = baseName.IndexOf('`');
            if (tickIndex > 0)
            {
                baseName = baseName.Substring(0, tickIndex);
            }
            
            // Build generic type string like "List<string>" instead of "List`1"
            var argumentNames = new string[genericArguments.Length];
            for (var i = 0; i < genericArguments.Length; i++)
            {
                argumentNames[i] = GetTypeName(genericArguments[i]);
            }
            
            return $"{baseName}<{string.Join(", ", argumentNames)}>";
        }

        return type.Name;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Attribute property access is expected to be preserved")]
    private static T? GetAttributeProperty<T>(object attribute, string propertyName)
    {
        var attributeType = attribute.GetType();
        
        // Try exact property name first
        var property = attributeType.GetProperty(propertyName);
        if (property?.GetValue(attribute) is T value)
        {
            return value;
        }
        
        // Try case-insensitive search
        var properties = attributeType.GetProperties();
        for (var i = 0; i < properties.Length; i++)
        {
            if (string.Equals(properties[i].Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (properties[i].GetValue(attribute) is T caseInsensitiveValue)
                {
                    return caseInsensitiveValue;
                }
                break;
            }
        }
        
        return default;
    }
}