using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace DreamEchoMod;

/// <summary>
/// 探针：只观察不修改。Patch 掉落/词缀关键方法，打印参数，用于确定：
/// 1) 碎片/记忆/梦境币的掉落路径与基础比率（构成平衡基准）
/// 2) 词缀生成时实际传入的 level（T1 强制值）
/// 3) HarmonyX 对 IL2CPP 静态/实例方法的兼容性（冒烟测试）
/// </summary>
public static class ProbePatches
{
    private static ManualLogSource _log = null!;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static void Install(ManualLogSource log)
    {
        _log = log;
        var harmony = new Harmony("com.dreamecho.probe");
        var t = typeof(Echoes.Core.Utility.DropHelper);

        // 1a. 掉落物生成（dropRatio=数量倍率）
        Patch(harmony, AccessTools.Method(t, "CreateDrop",
            new[] { typeof(List<int>), typeof(Vector3), typeof(float) }), "DROP-RATIO");
        // 1b. 掉落物生成（stage/dropLevel 路径）
        Patch(harmony, AccessTools.Method(t, "CreateDrop",
            new[] { typeof(List<int>), typeof(Vector3), typeof(int), typeof(int), typeof(HashSet<int>) }), "DROP-STAGE");
        // 2. 材料比率（memory/coin/shard）
        Patch(harmony, AccessTools.Method(t, "GetExtraDropRatioByLuckType",
            new[] { typeof(Echoes.GenralDrop), typeof(float), typeof(float), typeof(float) }), "RATIO");
        // 3. 掉落解析（rarity 输出）
        Patch(harmony, AccessTools.Method(t, "GetDrop",
            new[] { typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(int), typeof(HashSet<int>), typeof(Dictionary<int, List<int>>) }), "GETDROP");
        // 4. 词缀按等级查询（level → T 档）
        Patch(harmony, AccessTools.Method(typeof(Echoes.Config.TConceptMemoryAffix), "Get",
            new[] { typeof(int), typeof(int) }), "AFFIX");
        // 5. 词缀属性生成（max 标志）
        Patch(harmony, AccessTools.Method(t, "BuildMemoryAttr",
            new[] { typeof(Echoes.ConceptMemoryAffix), typeof(Echoes.Core.Managers.EAffixType), typeof(bool) }), "ATTR");
        // 6. 随机词缀包选择（权重掷骰）
        Patch(harmony, AccessTools.Method(t, "BuildMemoryRandom",
            new[] { typeof(List<Echoes.ConceptMemoryAffixPack>), typeof(List<int>), typeof(int), typeof(int), typeof(HashSet<int>), typeof(HashSet<int>) }), "RANDOM");
        // 7. 装备构建（mustAddAffix=必加词缀）
        Patch(harmony, AccessTools.Method(t, "BuildMemory",
            new[] { typeof(Echoes.Drop), typeof(int), typeof(List<Echoes.HomeShopTargetedBuyOrder>), typeof(HashSet<int>) }), "BUILD");

        log.LogInfo("[Probe] Harmony patches installed");
    }

    private static void Patch(Harmony harmony, MethodBase? original, string tag)
    {
        if (original == null)
        {
            _log.LogError($"[Probe] FAILED to find {tag} method!");
            return;
        }
        var prefix = new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(GenericPrefix), All)!);
        harmony.Patch(original, prefix: prefix);
        _log.LogInfo($"[Probe] patched {tag}: {original.DeclaringType?.Name}.{original.Name}");
    }

    // 通用 Prefix：打印所有参数（不修改任何行为）
    private static void GenericPrefix(object[] __args, MethodBase __originalMethod)
    {
        var argStr = string.Join(" | ", __args.Select((a, i) => $"[{i}]={Format(a)}"));
        _log.LogInfo($"[Probe] {__originalMethod.Name}({argStr})");
    }

    private static string Format(object? o)
    {
        if (o == null) return "null";
        if (o is Vector3 v) return $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";
        if (o is List<int> li)
        {
            var sb = new System.Text.StringBuilder($"List<int>[{li.Count}] {{");
            for (var i = 0; i < Math.Min(li.Count, 12); i++) { if (i > 0) sb.Append(','); sb.Append(li[i]); }
            return sb.Append('}').ToString();
        }
        if (o is HashSet<int> hs)
        {
            var sb = new System.Text.StringBuilder($"HashSet<int>[{hs.Count}] {{");
            var n = 0;
            foreach (var x in hs) { if (n > 0) sb.Append(','); sb.Append(x); if (++n >= 12) break; }
            return sb.Append('}').ToString();
        }
        var s = o.ToString() ?? "";
        return s.Length > 200 ? s[..200] + "..." : s;
    }
}
