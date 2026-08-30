using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: BatchRenamer.PublicPurityInspector <BatchRenamer.dll>");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Assembly does not exist: {assemblyPath}");
    return 3;
}

var forbiddenMetadata = new Regex(
    "InternalTools|InternalQa|OpenInternalQaCenter|InternalTools_PreviewKeyDown|_internalQaCenter|INTERNAL TEST|QA Center",
    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
var forbiddenDependency = new Regex(
    "InternalTools|Internal[._-]?Test|QA",
    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
var violations = new List<string>();

using (var stream = File.OpenRead(assemblyPath))
using (var peReader = new PEReader(stream))
{
    if (!peReader.HasMetadata)
    {
        Console.Error.WriteLine("Assembly has no readable .NET metadata.");
        return 4;
    }

    var metadata = peReader.GetMetadataReader();
    foreach (var handle in metadata.TypeDefinitions)
    {
        var type = metadata.GetTypeDefinition(handle);
        var fullName = $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
        if (forbiddenMetadata.IsMatch(fullName)) violations.Add($"type:{fullName}");

        foreach (var methodHandle in type.GetMethods())
        {
            var methodName = metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name);
            if (forbiddenMetadata.IsMatch(methodName)) violations.Add($"method:{fullName}.{methodName}");
        }

        foreach (var fieldHandle in type.GetFields())
        {
            var fieldName = metadata.GetString(metadata.GetFieldDefinition(fieldHandle).Name);
            if (forbiddenMetadata.IsMatch(fieldName)) violations.Add($"field:{fullName}.{fieldName}");
        }
    }

    foreach (var handle in metadata.ManifestResources)
    {
        var resourceName = metadata.GetString(metadata.GetManifestResource(handle).Name);
        if (forbiddenMetadata.IsMatch(resourceName)) violations.Add($"resource:{resourceName}");
    }

    foreach (var handle in metadata.AssemblyReferences)
    {
        var referenceName = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
        if (forbiddenDependency.IsMatch(referenceName)) violations.Add($"dependency:{referenceName}");
    }
}

var versionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
if (!string.Equals(versionInfo.ProductName, "easy重命名 / BatchRenamer", StringComparison.Ordinal))
{
    violations.Add($"product:{versionInfo.ProductName ?? "<missing>"}");
}
if (!string.Equals(versionInfo.ProductVersion, "1.0.0", StringComparison.Ordinal))
{
    violations.Add($"version:{versionInfo.ProductVersion ?? "<missing>"}");
}

var result = new
{
    Assembly = assemblyPath,
    ProductName = versionInfo.ProductName,
    ProductVersion = versionInfo.ProductVersion,
    InternalTypesAbsent = violations.All(item => !item.StartsWith("type:", StringComparison.Ordinal)),
    InternalCommandsAbsent = violations.All(item => !item.StartsWith("method:", StringComparison.Ordinal) && !item.StartsWith("field:", StringComparison.Ordinal)),
    InternalResourcesAbsent = violations.All(item => !item.StartsWith("resource:", StringComparison.Ordinal)),
    InternalDependenciesAbsent = violations.All(item => !item.StartsWith("dependency:", StringComparison.Ordinal)),
    Violations = violations,
};

Console.WriteLine(JsonSerializer.Serialize(result));
if (violations.Count != 0)
{
    Console.Error.WriteLine("PUBLIC_ASSEMBLY_METADATA = FAIL");
    return 1;
}

Console.WriteLine("PUBLIC_ASSEMBLY_METADATA = PASS");
return 0;
