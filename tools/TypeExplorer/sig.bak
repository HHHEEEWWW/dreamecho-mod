using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// 用法: TypeExplorer <keyword1,keyword2,...> [--methods] [--fields] [--full]
// 在 interop/Assembly-CSharp.dll 中按关键词搜索类型，可列出方法与字段
var interopDir = @"E:\steam\steamapps\common\DreamEcho\BepInEx\interop";
var target = @"Assembly-CSharp.dll";
var keywords = args.Length > 0 ? args[0].Split(',') : new[] { "affix", "loot", "drop", "reward" };
var showMethods = args.Contains("--methods");
var showFields = args.Contains("--fields");
var full = args.Contains("--full");

var paths = Directory.GetFiles(interopDir, "*.dll")
    .Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
    .ToArray();
var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver);

var asm = mlc.LoadFromAssemblyPath(Path.Combine(interopDir, target));
var types = asm.GetTypes();
Console.WriteLine($"Assembly: {asm.GetName().Name}  Types: {types.Length}");

foreach (var kw in keywords)
{
    Console.WriteLine($"\n===== keyword: {kw} =====");
    var hits = types
        .Where(t => t.FullName != null && t.FullName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
        .OrderBy(t => t.FullName)
        .ToList();
    Console.WriteLine($"hits: {hits.Count}");
    foreach (var t in hits.Take(full ? 200 : 40))
    {
        var kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsAbstract && t.IsSealed ? "static" : "class";
        var baseInfo = t.BaseType != null && t.BaseType.FullName != "System.Object" && t.BaseType.FullName != null
            ? $" : {t.BaseType.FullName}" : "";
        Console.WriteLine($"[{kind}] {t.FullName}{baseInfo}");
        if (showFields)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine($"    F {f.FieldType.Name} {f.Name}{(f.IsStatic ? " [static]" : "")}");
        }
        if (showMethods)
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName))
                Console.WriteLine($"    M {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        }
    }
    if (hits.Count > (full ? 200 : 40))
        Console.WriteLine($"... (+{hits.Count - (full ? 200 : 40)} more)");
}
