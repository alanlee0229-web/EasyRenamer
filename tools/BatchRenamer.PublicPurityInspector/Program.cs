using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 2 || (args[1] != "public" && args[1] != "internal"))
{
    Console.Error.WriteLine("Usage: BatchRenamer.PublicPurityInspector <BatchRenamer.dll> <public|internal>");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var flavor = args[1];
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
var types = new List<string>();
var methods = new List<string>();
var fields = new List<string>();
var resources = new List<string>();
var dependencies = new List<string>();

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
        types.Add(fullName);

        foreach (var methodHandle in type.GetMethods())
        {
            var methodName = metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name);
            methods.Add(methodName);
        }

        foreach (var fieldHandle in type.GetFields())
        {
            var fieldName = metadata.GetString(metadata.GetFieldDefinition(fieldHandle).Name);
            fields.Add(fieldName);
        }
    }

    foreach (var handle in metadata.ManifestResources)
    {
        var resourceName = metadata.GetString(metadata.GetManifestResource(handle).Name);
        resources.Add(resourceName);
    }

    foreach (var handle in metadata.AssemblyReferences)
    {
        var referenceName = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
        dependencies.Add(referenceName);
    }
}

var versionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
var expectedProduct = flavor == "public" ? "easy重命名 / BatchRenamer" : "BatchRenamer Internal Test";
var expectedDescription = expectedProduct;
var expectedProductVersion = flavor == "public" ? "1.0.0" : "1.0.0-internal";

if (!string.Equals(versionInfo.ProductName, expectedProduct, StringComparison.Ordinal))
{
    violations.Add($"product:{versionInfo.ProductName ?? "<missing>"}");
}
if (!string.Equals(versionInfo.FileDescription, expectedDescription, StringComparison.Ordinal))
{
    violations.Add($"description:{versionInfo.FileDescription ?? "<missing>"}");
}
if (!string.Equals(versionInfo.FileVersion, "1.0.0.0", StringComparison.Ordinal))
{
    violations.Add($"file-version:{versionInfo.FileVersion ?? "<missing>"}");
}
if (!string.Equals(versionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
{
    violations.Add($"version:{versionInfo.ProductVersion ?? "<missing>"}");
}

var assemblyBytes = File.ReadAllBytes(assemblyPath);
var internalMarkers = new[] { "INTERNAL TEST", "BatchRenamer Internal Test", "1.0.0-internal" };
var internalMarkerPresent = internalMarkers.Any(marker =>
    ContainsBytes(assemblyBytes, Encoding.UTF8.GetBytes(marker)) ||
    ContainsBytes(assemblyBytes, Encoding.Unicode.GetBytes(marker)));

if (flavor == "public")
{
    violations.AddRange(types.Where(item => forbiddenMetadata.IsMatch(item)).Select(item => $"type:{item}"));
    violations.AddRange(methods.Where(item => forbiddenMetadata.IsMatch(item)).Select(item => $"method:{item}"));
    violations.AddRange(fields.Where(item => forbiddenMetadata.IsMatch(item)).Select(item => $"field:{item}"));
    violations.AddRange(resources.Where(item => forbiddenMetadata.IsMatch(item)).Select(item => $"resource:{item}"));
    violations.AddRange(dependencies.Where(item => forbiddenDependency.IsMatch(item)).Select(item => $"dependency:{item}"));
    if (internalMarkerPresent) violations.Add("identity:internal-marker-present");
}
else
{
    if (!types.Any(item => item.EndsWith(".InternalQaCenterWindow", StringComparison.Ordinal))) violations.Add("type:InternalQaCenterWindow-missing");
    if (!types.Any(item => item.EndsWith(".InternalQaWorkspace", StringComparison.Ordinal))) violations.Add("type:InternalQaWorkspace-missing");
    if (!methods.Contains("InternalTools_PreviewKeyDown", StringComparer.Ordinal)) violations.Add("method:InternalTools_PreviewKeyDown-missing");
    if (!methods.Contains("OpenInternalQaCenter", StringComparer.Ordinal)) violations.Add("method:OpenInternalQaCenter-missing");
    if (!internalMarkerPresent) violations.Add("identity:INTERNAL-TEST-marker-missing");
}

var result = new
{
    Assembly = assemblyPath,
    Flavor = flavor,
    ProductName = versionInfo.ProductName,
    FileDescription = versionInfo.FileDescription,
    FileVersion = versionInfo.FileVersion,
    ProductVersion = versionInfo.ProductVersion,
    InternalMarkerPresent = internalMarkerPresent,
    InternalQaCenterTypePresent = types.Any(item => item.EndsWith(".InternalQaCenterWindow", StringComparison.Ordinal)),
    InternalShortcutRoutingPresent = methods.Contains("InternalTools_PreviewKeyDown", StringComparer.Ordinal),
    Violations = violations,
};

Console.WriteLine(JsonSerializer.Serialize(result));
if (violations.Count != 0)
{
    Console.Error.WriteLine("RELEASE_ASSEMBLY_METADATA = FAIL");
    return 1;
}

Console.WriteLine("RELEASE_ASSEMBLY_METADATA = PASS");
return 0;

static bool ContainsBytes(byte[] source, byte[] value)
{
    if (value.Length == 0 || source.Length < value.Length) return false;
    for (var i = 0; i <= source.Length - value.Length; i++)
    {
        var matches = true;
        for (var j = 0; j < value.Length; j++)
        {
            if (source[i + j] == value[j]) continue;
            matches = false;
            break;
        }
        if (matches) return true;
    }
    return false;
}
