using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using McpExtract.Models;
using System.Runtime.InteropServices;

namespace McpExtract.Analysis;

public sealed class McpToolAnalyzer
{
    private const string McpServerToolAttributeName = "McpServerToolAttribute";
    private const string McpToolParameterAttributeName = "McpToolParameterAttribute";

    /// <summary>
    /// Detects the target framework from an assembly's metadata.
    /// </summary>
    private string DetectTargetFramework(string assemblyPath)
    {
        using var fileStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        // Look for TargetFrameworkAttribute
        foreach (var handle in metadataReader.CustomAttributes)
        {
            var customAttribute = metadataReader.GetCustomAttribute(handle);
            var constructor = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
            var type = metadataReader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            var typeName = metadataReader.GetString(type.Name);

            if (typeName == "TargetFrameworkAttribute")
            {
                var value = customAttribute.DecodeValue(new CustomAttributeTypeProvider());
                if (value.FixedArguments.Length > 0 && value.FixedArguments[0].Value is string frameworkName)
                {
                    return frameworkName;
                }
            }
        }

        // Fallback: try to detect from assembly references
        foreach (var handle in metadataReader.AssemblyReferences)
        {
            var assemblyRef = metadataReader.GetAssemblyReference(handle);
            var name = metadataReader.GetString(assemblyRef.Name);
            if (name == "System.Runtime")
            {
                var version = assemblyRef.Version;
                // Map version to framework moniker
                if (version.Major >= 9)
                    return ".NETCoreApp,Version=v9.0";
                if (version.Major >= 8)
                    return ".NETCoreApp,Version=v8.0";
                if (version.Major >= 7)
                    return ".NETCoreApp,Version=v7.0";
                if (version.Major >= 6)
                    return ".NETCoreApp,Version=v6.0";
            }
        }

        // Default fallback
        return ".NETCoreApp,Version=v8.0";
    }

    /// <summary>
    /// Finds the best matching reference assemblies for the given target framework.
    /// </summary>
    private string[] FindReferenceAssemblies(string targetFramework)
    {
        var referenceAssemblies = new List<string>();

        // Extract major version from target framework
        var tfm = ExtractTfmFromFrameworkName(targetFramework);
        var targetVersion = ExtractVersionFromTfm(tfm);

        // Get platform-specific .NET reference assembly locations
        var possiblePaths = GetDotNetReferenceAssemblyPaths();

        // Try to find the exact version first, then fallback to lower versions
        for (var version = targetVersion; version >= 6; version--)
        {
            var versionString = $"{version}.0";
            var found = TryFindReferenceAssembliesForVersion(possiblePaths, versionString, referenceAssemblies);
            if (found)
            {
                if (version < targetVersion)
                {
                    Console.WriteLine($"Warning: Using .NET {version}.0 reference assemblies for target .NET {targetVersion}.0 assembly. Some newer APIs may not be available for analysis.");
                }
                break;
            }
        }

        // If no reference assemblies found, try to use the current runtime assemblies as fallback
        if (referenceAssemblies.Count == 0)
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
            {
                // Only include core assemblies that we need, not all runtime assemblies
                var coreAssemblyNames = new[] { "System.Runtime", "System.Private.CoreLib", "mscorlib", 
                    "System.Text.Json", "System.Threading.Tasks", "System.Collections", "System.ComponentModel" };
                
                foreach (var assemblyName in coreAssemblyNames)
                {
                    var assemblyPath = Path.Combine(runtimeDir, assemblyName + ".dll");
                    if (File.Exists(assemblyPath))
                    {
                        referenceAssemblies.Add(assemblyPath);
                    }
                }
                
                if (referenceAssemblies.Count > 0)
                {
                    Console.WriteLine("Warning: Using current runtime assemblies as fallback. Analysis may include runtime-specific details.");
                }
            }
            
            if (referenceAssemblies.Count == 0)
            {
                Console.WriteLine($"Warning: Could not find reference assemblies for .NET {targetVersion}.0. Analysis may be incomplete.");
            }
        }

        return referenceAssemblies.ToArray();
    }

    /// <summary>
    /// Gets platform-specific paths where .NET reference assemblies might be located.
    /// </summary>
    private string[] GetDotNetReferenceAssemblyPaths()
    {
        var paths = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows-specific paths
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            
            if (!string.IsNullOrEmpty(programFiles))
            {
                paths.Add(Path.Combine(programFiles, "dotnet", "packs"));
            }
            
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                paths.Add(Path.Combine(programFilesX86, "dotnet", "packs"));
            }
            
            // Additional Windows paths where .NET might be installed
            paths.Add(@"C:\Program Files\dotnet\packs");
            paths.Add(@"C:\Program Files (x86)\dotnet\packs");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux-specific paths
            paths.Add("/usr/lib/dotnet/packs");
            paths.Add("/usr/share/dotnet/packs");
            paths.Add("/usr/local/share/dotnet/packs");
            paths.Add("/opt/dotnet/packs");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS-specific paths
            paths.Add("/usr/local/share/dotnet/packs");
            paths.Add("/usr/share/dotnet/packs");
            paths.Add("/opt/dotnet/packs");
        }

        // Try to detect from current .NET installation
        var currentDotNetPath = GetCurrentDotNetInstallationPath();
        if (!string.IsNullOrEmpty(currentDotNetPath))
        {
            var packsPath = Path.Combine(currentDotNetPath, "packs");
            if (!paths.Contains(packsPath))
            {
                paths.Insert(0, packsPath); // Prioritize current installation
            }
        }

        return paths.ToArray();
    }

    /// <summary>
    /// Attempts to determine the current .NET installation path.
    /// </summary>
    private string? GetCurrentDotNetInstallationPath()
    {
        try
        {
            // Get the path from the current runtime location
            var runtimeLocation = typeof(object).Assembly.Location;
            if (!string.IsNullOrEmpty(runtimeLocation))
            {
                // Navigate up from runtime location to find dotnet root
                // Typical structure: /dotnet/shared/Microsoft.NETCore.App/version/...
                var dir = new DirectoryInfo(runtimeLocation);
                while (dir != null && dir.Parent != null)
                {
                    if (string.Equals(dir.Name, "dotnet", StringComparison.OrdinalIgnoreCase))
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }

            // Try using DOTNET_ROOT environment variable
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
            {
                return dotnetRoot;
            }
        }
        catch
        {
            // Ignore errors in path detection
        }

        return null;
    }

    private bool TryFindReferenceAssembliesForVersion(string[] basePaths, string version, List<string> referenceAssemblies)
    {
        foreach (var basePath in basePaths)
        {
            if (!Directory.Exists(basePath))
                continue;

            var refPackPath = Path.Combine(basePath, "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(refPackPath))
                continue;

            // Look for the specific version directories (manual filter + sort descending)
            var allDirs = Directory.GetDirectories(refPackPath);
            var matched = new List<string>();
            for (int i = 0; i < allDirs.Length; i++)
            {
                var dirName = Path.GetFileName(allDirs[i]);
                if (dirName.StartsWith(version, StringComparison.Ordinal))
                {
                    matched.Add(allDirs[i]);
                }
            }
            if (matched.Count == 0)
                continue;
            // Sort descending by directory name (simple selection sort)
            for (int i = 0; i < matched.Count - 1; i++)
            {
                int maxIndex = i;
                for (int j = i + 1; j < matched.Count; j++)
                {
                    var nameJ = Path.GetFileName(matched[j]);
                    var nameMax = Path.GetFileName(matched[maxIndex]);
                    if (string.CompareOrdinal(nameJ, nameMax) > 0)
                    {
                        maxIndex = j;
                    }
                }
                if (maxIndex != i)
                {
                    var tmp = matched[i];
                    matched[i] = matched[maxIndex];
                    matched[maxIndex] = tmp;
                }
            }
            var chosen = matched[0];
            var refDir = Path.Combine(chosen, "ref", $"net{version.Split('.')[0]}.0");
            if (Directory.Exists(refDir))
            {
                var files = Directory.GetFiles(refDir, "*.dll");
                referenceAssemblies.AddRange(files);
                return true;
            }
        }

        return false;
    }

    private string ExtractTfmFromFrameworkName(string frameworkName)
    {
        // Extract TFM from ".NETCoreApp,Version=v8.0" format
        if (frameworkName.Contains("Version=v"))
        {
            var versionPart = frameworkName.Split("Version=v")[1];
            var majorMinor = versionPart.Split('.');
            return $"net{majorMinor[0]}.{majorMinor[1]}";
        }
        
        // Default fallback
        return "net8.0";
    }

    private int ExtractVersionFromTfm(string tfm)
    {
        // Extract major version from "net8.0" format
        var versionPart = tfm.Replace("net", "").Split('.')[0];
        return int.TryParse(versionPart, out var version) ? version : 8;
    }

    /// <summary>
    /// Gets sibling dependencies (DLLs in the same directory as the target assembly).
    /// </summary>
    private string[] GetSiblingDependencies(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();

        var all = Directory.GetFiles(directory, "*.dll");
        var list = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            var dll = all[i];
            if (!string.Equals(dll, assemblyPath, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(dll);
            }
        }
        return list.ToArray();
    }

    /// <summary>
    /// Simple implementation of ICustomAttributeTypeProvider for decoding custom attributes.
    /// </summary>
    private class CustomAttributeTypeProvider : ICustomAttributeTypeProvider<Type>
    {
        public Type GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.String => typeof(string),
            PrimitiveTypeCode.Int32 => typeof(int),
            PrimitiveTypeCode.Boolean => typeof(bool),
            _ => typeof(object)
        };

        public Type GetSystemType() => typeof(Type);
        public Type GetSZArrayType(Type elementType) => elementType.MakeArrayType();
        public Type GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => typeof(object);
        public Type GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => typeof(object);
        public Type GetTypeFromSerializedName(string name) => typeof(object);
        public PrimitiveTypeCode GetUnderlyingEnumType(Type type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(Type type) => type == typeof(Type);
    }

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

        // Detect target framework and find reference assemblies
        var targetFramework = DetectTargetFramework(assemblyPath);
        LogDebug("Detected target framework: {0}", targetFramework);
        var referenceAssemblies = FindReferenceAssemblies(targetFramework);
        // Build reference assemblies list text without LINQ
        {
            var names = new List<string>();
            for (int i = 0; i < referenceAssemblies.Length; i++)
            {
                names.Add(Path.GetFileName(referenceAssemblies[i]));
            }
            LogDebug("Reference assemblies ({0}): {1}", referenceAssemblies.Length, string.Join(", ", names.ToArray()));
        }
        var siblingDependencies = GetSiblingDependencies(assemblyPath);
        {
            var depNames = new List<string>();
            for (int i = 0; i < siblingDependencies.Length; i++)
            {
                depNames.Add(Path.GetFileName(siblingDependencies[i]));
            }
            LogDebug("Sibling dependencies ({0}): {1}", siblingDependencies.Length, string.Join(", ", depNames.ToArray()));
        }

        // Combine reference assemblies with sibling dependencies for resolution
        // Manually concatenate arrays (referenceAssemblies + siblingDependencies + assemblyPath)
        var allAssemblies = new string[referenceAssemblies.Length + siblingDependencies.Length + 1];
        int offset = 0;
        for (int i = 0; i < referenceAssemblies.Length; i++)
        {
            allAssemblies[offset++] = referenceAssemblies[i];
        }
        for (int i = 0; i < siblingDependencies.Length; i++)
        {
            allAssemblies[offset++] = siblingDependencies[i];
        }
        allAssemblies[offset] = assemblyPath;
        LogDebug("Total assemblies provided to resolver: {0}", allAssemblies.Length);
        
        // Create resolver and load context
        var resolver = new PathAssemblyResolver(allAssemblies);
        
        // Find the core assembly (System.Runtime, System.Private.CoreLib, or mscorlib)
        string? coreAssembly = null;
        for (int i = 0; i < referenceAssemblies.Length; i++)
        {
            var fileName = Path.GetFileNameWithoutExtension(referenceAssemblies[i]);
            if (fileName == "System.Runtime" || fileName == "System.Private.CoreLib" || fileName == "mscorlib")
            {
                coreAssembly = referenceAssemblies[i];
                break;
            }
        }
        LogDebug("Core assembly selected for MetadataLoadContext: {0}", coreAssembly ?? "(none)");
            
        using var loadContext = coreAssembly != null 
            ? new MetadataLoadContext(resolver, Path.GetFileNameWithoutExtension(coreAssembly))
            : new MetadataLoadContext(resolver);
        LogDebug("MetadataLoadContext created (CoreName: {0})", coreAssembly != null ? Path.GetFileNameWithoutExtension(coreAssembly) : "default");
        
        try
        {
            // Load the target assembly in isolation
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        
            // Extract assembly metadata
            var assemblyName = assembly.GetName();
            var assemblyVersion = assemblyName.Version?.ToString() ?? "1.0.0";
            
            string? assemblyDescription = null;
            string? assemblyCompany = null;
            string? assemblyProduct = null;
            
            try
            {
                var customAttributesData = assembly.GetCustomAttributesData();
                
                foreach (var attrData in customAttributesData)
                {
                    var attrTypeName = attrData.AttributeType.Name;
                    if (attrTypeName == "AssemblyDescriptionAttribute" && attrData.ConstructorArguments.Count > 0)
                    {
                        assemblyDescription = attrData.ConstructorArguments[0].Value?.ToString();
                    }
                    else if (attrTypeName == "AssemblyCompanyAttribute" && attrData.ConstructorArguments.Count > 0)
                    {
                        assemblyCompany = attrData.ConstructorArguments[0].Value?.ToString();
                    }
                    else if (attrTypeName == "AssemblyProductAttribute" && attrData.ConstructorArguments.Count > 0)
                    {
                        assemblyProduct = attrData.ConstructorArguments[0].Value?.ToString();
                    }
                }
            }
            catch
            {
                // Ignore errors when reading attributes
            }

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
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze assembly '{assemblyPath}': {ex.Message}", ex);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Dynamic access to public methods is required for MCP tool discovery")]
    private static MethodInfo[] GetPublicMethods([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
        return type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    /// <summary>
    /// Analyzes a method to determine if it's an MCP tool and extracts its metadata.
    /// Uses flexible attribute matching to support different MCP SDK versions and patterns.
    /// </summary>
    private McpTool? AnalyzeMethod(MethodInfo method)
    {
        // Look for MCP tool attributes using more flexible matching
        var attributesData = method.GetCustomAttributesData();
        CustomAttributeData? mcpAttributeData = null;

        for (var i = 0; i < attributesData.Count; i++)
        {
            var attrData = attributesData[i];
            var attrTypeName = attrData.AttributeType.Name;
            var attrFullName = attrData.AttributeType.FullName;

            if (attrTypeName == McpServerToolAttributeName ||
                attrTypeName.Contains("McpTool") ||
                attrTypeName.Contains("McpServerTool") ||
                (attrFullName?.Contains("McpTool") ?? false) ||
                (attrFullName?.Contains("McpServerTool") ?? false))
            {
                DebugAttributeData(attrData, $"method {method.DeclaringType?.FullName}.{method.Name}");
                mcpAttributeData = attrData;
                break;
            }
        }

        if (mcpAttributeData == null)
            return null;

        var actualAttributeName = mcpAttributeData.AttributeType.Name;
        var toolName = GetAttributePropertyFromData(attributesData, actualAttributeName, "Name")
            ?? GetAttributePropertyFromData(attributesData, McpServerToolAttributeName, "Name")
            ?? method.Name;
        
        // Prefer an explicit Description attribute if present. Some MCP SDKs use a separate
        // attribute for description instead of a Description property on the tool attribute.
        var description = GetAttributePropertyFromData(attributesData, "DescriptionAttribute", "Description")
            ?? GetAttributePropertyFromData(attributesData, "McpServerToolDescriptionAttribute", "Description")
            ?? GetAttributePropertyFromData(attributesData, "McpToolDescriptionAttribute", "Description")
            ?? GetAttributePropertyFromData(attributesData, "ToolDescriptionAttribute", "Description")
            // Fall back to Description property/ctor arg on the McpServerToolAttribute
            ?? GetAttributePropertyFromData(attributesData, McpServerToolAttributeName, "Description")
            ?? GetAttributePropertyFromData(attributesData, McpServerToolAttributeName, "description")
            ?? string.Empty;

        if (string.IsNullOrEmpty(description))
        {
            LogDebug("No description found for {0}.{1} (looked in separate Description attributes and McpServerTool attribute)", method.DeclaringType?.FullName, method.Name);
            LogDebug("Dumping available attribute data for method {0}.{1} to assist diagnosis", method.DeclaringType?.FullName, method.Name);
            foreach (var ad in attributesData)
            {
                DebugAttributeData(ad, $"method {method.DeclaringType?.FullName}.{method.Name} - final dump");
            }
            // Last-chance lookup on actual attribute name variants
            description = GetAttributePropertyFromData(attributesData, actualAttributeName, "Description")
                ?? GetAttributePropertyFromData(attributesData, actualAttributeName, "description")
                ?? description;
        }

        // Analyze parameters
        var parameters = new List<McpParameter>();
        var methodParams = method.GetParameters();

        foreach (var param in methodParams)
        {
            // Skip special parameters like CancellationToken
            if (param.ParameterType.FullName == "System.Threading.CancellationToken")
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

    /// <summary>
    /// Analyzes a parameter to extract MCP metadata.
    /// Uses flexible attribute matching to support different MCP SDK versions and patterns.
    /// </summary>
    private McpParameter AnalyzeParameter(ParameterInfo parameter)
    {
        // Look for MCP parameter attributes using more flexible matching
        var attributesData = parameter.GetCustomAttributesData();
        string? foundAttributeName = null;
        
        // Find any MCP parameter attribute
        foreach (var attrData in attributesData)
        {
            var attrTypeName = attrData.AttributeType.Name;
            var attrFullName = attrData.AttributeType.FullName;
            
            if (attrTypeName == McpToolParameterAttributeName ||
                attrTypeName.Contains("McpParameter") ||
                attrTypeName.Contains("McpToolParameter") ||
                attrFullName?.Contains("McpParameter") == true ||
                attrFullName?.Contains("McpToolParameter") == true)
            {
                foundAttributeName = attrTypeName;
                break;
            }
        }
        
        var description = string.Empty;
        if (foundAttributeName != null)
        {
            description = GetAttributePropertyFromData(attributesData, foundAttributeName, "Description") ?? string.Empty;
        }
        
        // Look for separate Description attribute if still empty
        if (string.IsNullOrEmpty(description))
        {
            description = GetAttributePropertyFromData(attributesData, "DescriptionAttribute", "Description") ?? string.Empty;
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
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
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
            var genericDefName = genericDef.FullName;
            if (
                genericDefName == "System.Collections.Generic.List`1"
                || genericDefName == "System.Collections.Generic.IList`1"
                || genericDefName == "System.Collections.Generic.ICollection`1"
                || genericDefName == "System.Collections.Generic.IEnumerable`1")
            {
                isArray = true;
                elementType = AnalyzeType(actualType.GetGenericArguments()[0]);
            }
        }

        // Handle Task<T> return types
        if (actualType.IsGenericType && actualType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1")
        {
            return AnalyzeType(actualType.GetGenericArguments()[0]);
        }

        // Handle Task return type
        if (actualType.FullName == "System.Threading.Tasks.Task")
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
        // Use FullName for reliable type comparison in MetadataLoadContext
        var fullName = type.FullName;
        
        if (fullName == "System.String")
        {
            return "string";
        }
        if (fullName == "System.Int32")
        {
            return "int";
        }
        if (fullName == "System.Int64")
        {
            return "long";
        }
        if (fullName == "System.Double")
        {
            return "double";
        }
        if (fullName == "System.Single")
        {
            return "float";
        }
        if (fullName == "System.Boolean")
        {
            return "bool";
        }
        if (fullName == "System.DateTime")
        {
            return "DateTime";
        }
        if (fullName == "System.Guid")
        {
            return "Guid";
        }
        if (fullName == "System.Object")
        {
            return "object";
        }
        if (fullName == "System.Void")
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

    /// <summary>
    /// Gets an attribute property value from CustomAttributeData (MetadataLoadContext-compatible).
    /// </summary>
    private string? GetAttributePropertyFromData(IList<CustomAttributeData> attributesData, string attributeName, string propertyName)
    {
        foreach (var attrData in attributesData)
        {
            if (attrData.AttributeType.Name == attributeName || 
                attrData.AttributeType.FullName?.EndsWith("." + attributeName) == true)
            {
                // Check named arguments (properties) first - these are most reliable
                foreach (var namedArg in attrData.NamedArguments)
                {
                    if (string.Equals(namedArg.MemberName, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        var namedValue = namedArg.TypedValue.Value?.ToString();
                        LogDebug("Found {0} on attribute {1} via named argument '{2}': {3}", propertyName, attributeName, namedArg.MemberName, namedValue ?? "<null>");
                        return namedArg.TypedValue.Value?.ToString();
                    }
                }
                
                // If not found in named arguments, try to infer from constructor arguments
                // Note: This is less reliable as the order depends on the constructor signature
                if (attrData.ConstructorArguments.Count > 0)
                {
                    // Try to get constructor parameter names if available
                    try
                    {
                        var constructor = attrData.Constructor;
                        if (constructor is MethodBase methodBase)
                        {
                            var parameters = methodBase.GetParameters();
                            for (int i = 0; i < Math.Min(parameters.Length, attrData.ConstructorArguments.Count); i++)
                            {
                                if (string.Equals(parameters[i].Name, propertyName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return attrData.ConstructorArguments[i].Value?.ToString();
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore reflection issues here
                    }

                    // Fallback conventions
                    if (string.Equals(propertyName, "Name", StringComparison.OrdinalIgnoreCase) && attrData.ConstructorArguments.Count > 0)
                    {
                        return attrData.ConstructorArguments[0].Value?.ToString();
                    }
                    if (string.Equals(propertyName, "Description", StringComparison.OrdinalIgnoreCase))
                    {
                        if (attrData.ConstructorArguments.Count > 1)
                        {
                            return attrData.ConstructorArguments[1].Value?.ToString();
                        }
                        if (attrData.ConstructorArguments.Count == 1)
                        {
                            return attrData.ConstructorArguments[0].Value?.ToString();
                        }
                    }
                }
                // Nothing found — log constructor-argument summary to help diagnosis
                // Build sample values manually (max 5)
                string sample = "";
                int count = 0;
                foreach (var ca in attrData.ConstructorArguments)
                {
                    if (count > 0)
                        sample += ", ";
                    sample += ca.Value != null ? ca.Value.ToString() : "<null>";
                    count++;
                    if (count >= 5) break;
                }
                LogDebug("Attribute {0} found but property {1} not present. ConstructorArgCount={2}. Sample values: {3}",
                    attributeName,
                    propertyName,
                    attrData.ConstructorArguments.Count,
                    sample);
            }
        }
        
        return null;
    }

    /// <summary>
    /// Creates a proxy object for attributes (not used in current implementation but kept for compatibility).
    /// </summary>
    private object CreateAttributeProxy(CustomAttributeData attributeData)
    {
        // For our purposes, we don't need the actual proxy object since we're accessing data directly
        return new object();
    }

    private static bool DebugEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("MCP_EXTRACT_DEBUG"), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("MCP_EXTRACT_DEBUG"), "true", StringComparison.OrdinalIgnoreCase);

    private static void LogDebug(string format, params object[] args)
    {
        if (!DebugEnabled) return;
        try
        {
            Console.WriteLine("[McpToolAnalyzer] " + string.Format(format, args));
        }
        catch { /* ignore logging errors */ }
    }

    private void DebugAttributeData(CustomAttributeData data, string context)
    {
        if (!DebugEnabled) return;
        try
        {
            Console.WriteLine($"[McpToolAnalyzer] Attribute data for {context}: Type={data.AttributeType?.FullName}");
            Console.WriteLine($"  ConstructorArguments ({data.ConstructorArguments.Count}):");
            for (var i = 0; i < data.ConstructorArguments.Count; i++)
            {
                var a = data.ConstructorArguments[i];
                Console.WriteLine($"    [{i}] ArgType={(a.ArgumentType?.FullName ?? "unknown")} Value={(a.Value == null ? "<null>" : a.Value.ToString())} (rawType={(a.Value == null ? "null" : a.Value.GetType().FullName)})");
            }
            Console.WriteLine($"  NamedArguments ({data.NamedArguments.Count}):");
            for (var i = 0; i < data.NamedArguments.Count; i++)
            {
                var n = data.NamedArguments[i];
                Console.WriteLine($"    [{i}] MemberName={n.MemberName} Type={(n.TypedValue.ArgumentType?.FullName ?? "unknown")} Value={(n.TypedValue.Value == null ? "<null>" : n.TypedValue.Value.ToString())} (rawType={(n.TypedValue.Value == null ? "null" : n.TypedValue.Value.GetType().FullName)})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[McpToolAnalyzer] Failed to dump attribute data for {context}: {ex}");
        }
    }
}