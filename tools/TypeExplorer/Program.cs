using System.Reflection;
using System.Runtime.InteropServices;

// Usage: TypeExplorer <keyword1,keyword2,...> [--methods] [--fields] [--full] [--interop <dir>] [--core <dir>]
// Searches types in interop/Assembly-CSharp.dll by keywords.
// If --interop/--core are omitted, they are resolved from the game's doorstop_config.ini
// (target_assembly -> profile BepInEx root), so this works with the isolated BepInEx-Manager layout.
var defaultGameDir = @"E:\steam\steamapps\common\DreamEcho";
var keywords = new List<string>();
string? interopDir = null;
string? coreDir = null;
var showMethods = false;
var showFields = false;
var full = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--methods": showMethods = true; break;
        case "--fields": showFields = true; break;
        case "--full": full = true; break;
        case "--interop" when i + 1 < args.Length: interopDir = args[++i]; break;
        case "--core" when i + 1 < args.Length: coreDir = args[++i]; break;
        default: keywords.Add(args[i]); break;
    }
}

// Resolve profile BepInEx root from doorstop target_assembly if not given explicitly
if (interopDir == null || coreDir == null)
{
    var doorstop = Path.Combine(defaultGameDir, "doorstop_config.ini");
    string? bepRoot = null;
    if (File.Exists(doorstop))
    {
        foreach (var line in File.ReadAllLines(doorstop))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("target_assembly", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            var target = trimmed[(eq + 1)..].Trim();
            // <profile>/BepInEx/core/BepInEx.Unity.IL2CPP.dll -> BepInEx root = one level up from core
            bepRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, ".."));
            break;
        }
    }
    if (bepRoot == null || !Directory.Exists(Path.Combine(bepRoot, "core")))
    {
        Console.Error.WriteLine("Cannot resolve profile BepInEx root. Pass --interop <dir> --core <dir> explicitly.");
        return 1;
    }
    interopDir ??= Path.Combine(bepRoot, "interop");
    coreDir ??= Path.Combine(bepRoot, "core");
}

if (keywords.Count == 0) keywords.Add("affix");

var paths = Directory.GetFiles(interopDir, "*.dll")
    .Concat(Directory.GetFiles(coreDir, "*.dll"))
    .Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")).ToArray();
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));
var asm = mlc.LoadFromAssemblyPath(Path.Combine(interopDir, "Assembly-CSharp.dll"));
var types = asm.GetTypes();
Console.WriteLine($"Assembly: {asm.GetName().Name}  Types: {types.Length}");

foreach (var kw in keywords)
{
    Console.WriteLine($"\n===== keyword: {kw} =====");
    IEnumerable<Type> matched;
    if (showMethods || showFields)
    {
        // match type full name OR member name (member hits listed under their owning type)
        matched = types.Where(t =>
        {
            if (t.FullName != null && t.FullName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (showMethods && t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => m.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (showFields && t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(f => f.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        });
    }
    else
    {
        matched = types.Where(t => t.FullName != null && t.FullName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    var hits = matched.OrderBy(t => t.FullName).ToList();
    Console.WriteLine($"hits: {hits.Count}");
    foreach (var t in hits.Take(full ? 200 : 40))
    {
        var kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsAbstract && t.IsSealed ? "static" : "class";
        var baseInfo = t.BaseType != null && t.BaseType.FullName != "System.Object" && t.BaseType.FullName != null
            ? $" : {t.BaseType.FullName}" : "";
        Console.WriteLine($"[{kind}] {t.FullName}{baseInfo}");
        if (showFields)
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine($"    F {f.FieldType.Name} {f.Name}{(f.IsStatic ? " [static]" : "")}");
        if (showMethods)
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                Console.WriteLine($"    M {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
    }
    if (hits.Count > (full ? 200 : 40))
        Console.WriteLine($"... (+{hits.Count - (full ? 200 : 40)} more)");
}
return 0;
