using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace DreamEchoMod;

/// <summary>
/// 探针 v2：只观察不修改。改进点：
/// 1) RATIO：打印 GenralDrop 的 Id/PackName/PackType/LuckType（建立掉落包映射）
/// 2) GetDrop：Postfix 输出 rarity 最终值（out 参数）
/// 3) Get：Postfix 输出返回词缀的 Id/Level/MaxLevel（确认 T 档语义）
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

        Patch(harmony, AccessTools.Method(t, "CreateDrop",
            new[] { typeof(List<int>), typeof(Vector3), typeof(float) }), "DROP-RATIO");
        Patch(harmony, AccessTools.Method(t, "CreateDrop",
            new[] { typeof(List<int>), typeof(Vector3), typeof(int), typeof(int), typeof(HashSet<int>) }), "DROP-STAGE");
        Patch(harmony, AccessTools.Method(t, "GetExtraDropRatioByLuckType",
            new[] { typeof(Echoes.GenralDrop), typeof(float), typeof(float), typeof(float) }), "RATIO",
            postfix: nameof(PostfixRatio));
        Patch(harmony, AccessTools.Method(t, "GetDrop",
            new[] { typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(int), typeof(HashSet<int>), typeof(Dictionary<int, List<int>>) }), "GETDROP",
            postfix: nameof(PostfixGetDrop));
        Patch(harmony, AccessTools.Method(typeof(Echoes.Config.TConceptMemoryAffix), "Get",
            new[] { typeof(int), typeof(int) }), "AFFIX",
            postfix: nameof(PostfixAffixGet));
        Patch(harmony, AccessTools.Method(t, "BuildMemoryAttr",
            new[] { typeof(Echoes.ConceptMemoryAffix), typeof(Echoes.Core.Managers.EAffixType), typeof(bool) }), "ATTR");
        Patch(harmony, AccessTools.Method(t, "BuildMemoryRandom",
            new[] { typeof(List<Echoes.ConceptMemoryAffixPack>), typeof(List<int>), typeof(int), typeof(int), typeof(HashSet<int>), typeof(HashSet<int>) }), "RANDOM");
        Patch(harmony, AccessTools.Method(t, "BuildMemory",
            new[] { typeof(Echoes.Drop), typeof(int), typeof(List<Echoes.HomeShopTargetedBuyOrder>), typeof(HashSet<int>) }), "BUILD");

        log.LogInfo("[Probe] Harmony patches installed");
    }

    private static void Patch(Harmony harmony, MethodBase? original, string tag, string? postfix = null)
    {
        if (original == null)
        {
            _log.LogError($"[Probe] FAILED to find {tag} method!");
            return;
        }
        var prefix = new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(GenericPrefix), All)!);
        var post = postfix != null ? new HarmonyMethod(typeof(ProbePatches).GetMethod(postfix, All)!) : null;
        harmony.Patch(original, prefix: prefix, postfix: post);
        _log.LogInfo($"[Probe] patched {tag}: {original.DeclaringType?.Name}.{original.Name}");
    }

    // 通用 Prefix：打印所有参数（不修改任何行为）
    private static void GenericPrefix(object[] __args, MethodBase __originalMethod)
    {
        var argStr = string.Join(" | ", __args.Select((a, i) => $"[{i}]={Format(a)}"));
        _log.LogInfo($"[Probe] {__originalMethod.Name}({argStr})");
    }

    // RATIO Postfix：打印返回值（应用幸运后的比率）
    private static void PostfixRatio(float __result, object[] __args)
    {
        _log.LogInfo($"[Probe] RATIO => {__result:0.###}  (memory={__args[1]}, coin={__args[2]}, shard={__args[3]})");
    }

    // GETDROP Postfix：rarity 最终值 + 材料/记忆名称识别
    private static void PostfixGetDrop(object __result, object[] __args)
    {
        var rarity = __args.Length > 2 ? __args[2] : null;
        var content = 0;
        if (__result is Echoes.Drop d)
        {
            content = d.DropContentId;
            // 识别碎片（ConceptShards）
            try
            {
                var shard = Echoes.ConfigManager.Tables.TConceptShards.GetOrDefault(content);
                if (shard != null)
                {
                    _log.LogInfo($"[Probe] SHARD content={content} Name={shard.Name} Type={shard.ShardsType} Rarity={shard.Rarity}");
                    return;
                }
            }
            catch { /* 表未加载时忽略 */ }
            try
            {
                var mb = Echoes.ConfigManager.Tables.TConceptMemoryBase.GetOrDefault(content);
                if (mb != null)
                    _log.LogInfo($"[Probe] MEMBASE content={content} Name={mb.BaseName} MaxPrefix={mb.MaxPrefixNum} MaxSuffix={mb.MaxSuffixNum}");
            }
            catch { /* 表未加载时忽略 */ }
        }
        _log.LogInfo($"[Probe] GETDROP => rarity={rarity}, drop={Format(__result)}");
    }

    // AFFIX Postfix：返回词缀的档位信息
    private static void PostfixAffixGet(object __result, object[] __args)
    {
        var id = __args.Length > 0 ? __args[0] : null;
        var level = __args.Length > 1 ? __args[1] : null;
        if (__result is Echoes.ConceptMemoryAffix a)
            _log.LogInfo($"[Probe] AFFIX id={id} reqLevel={level} => got Id={a.Id} Level={a.Level} MaxLevel={a.MaxLevel} Type={a.AffixType}");
        else
            _log.LogInfo($"[Probe] AFFIX id={id} reqLevel={level} => {(__result == null ? "NULL" : __result.GetType().Name)}");
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
        if (o is Echoes.GenralDrop g)
            return $"GenralDrop(Id={g.Id},Name={g.PackName},PackType={g.PackType},Rank={g.PackRank},Luck={g.LuckType})";
        if (o is Echoes.Drop d)
            return $"Drop(pack={d.DropPack},type={d.DropType},content={d.DropContentId},rarity={d.Rarity},weight={d.Weight})";
        var s = o.ToString() ?? "";
        return s.Length > 200 ? s[..200] + "..." : s;
    }
}
