using System.Reflection;
using System.Runtime.InteropServices;

var interopDir = @"E:\steam\steamapps\common\DreamEcho\BepInEx\interop";
var paths = Directory.GetFiles(interopDir, "*.dll").Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")).ToArray();
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));
var asm = mlc.LoadFromAssemblyPath(Path.Combine(interopDir, "Assembly-CSharp.dll"));
var t = asm.GetType("Echoes.Core.Utility.DropHelper")!;
foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
{
    if (m.Name is not ("CreateDrop" or "GetDrop" or "GetExtraDropRatioByLuckType" or "BuildMemoryRandom" or "BuildMemory" or "BuildMemoryAttr"))
        continue;
    var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));
    Console.WriteLine($"{m.Name}({ps})");
}
var t2 = asm.GetType("Echoes.Config.TConceptMemoryAffix")!;
foreach (var m in t2.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.Name == "Get"))
    Console.WriteLine($"TConceptMemoryAffix.Get({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"))})");
var t3 = asm.GetType("Echoes.Core.Managers.HomeShopTargetedBuyOrder");
Console.WriteLine($"HomeShopTargetedBuyOrder: {(t3 == null ? "NOT FOUND" : t3.FullName)}");
var t4 = asm.GetType("Echoes.Drop");
Console.WriteLine($"Echoes.Drop: {t4?.FullName}");
