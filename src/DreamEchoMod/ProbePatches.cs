using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace DreamEchoMod;

/// <summary>
/// 临时诊断探针：只观察 RollDropWeightIndex / RandomDrop 的权重参数（稀有度倒挂实现依据）。
/// </summary>
public static class ProbePatches
{
    private static ManualLogSource _log = null!;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static DateTime _lastLog = DateTime.MinValue;

    public static void Install(ManualLogSource log)
    {
        _log = log;
        var harmony = new Harmony("com.dreamecho.probe");
        var t = typeof(Echoes.Core.Utility.DropHelper);

        Patch(harmony, AccessTools.Method(t, "RandomDrop",
            new[] { typeof(List<int>), typeof(Echoes.Core.Utility.DropHelper.EDropLuckyType), typeof(List<int>) }), "RandomDrop");
        Patch(harmony, AccessTools.Method(t, "RollDropWeightIndex",
            new[] { typeof(List<int>), typeof(List<int>), typeof(bool), typeof(bool), typeof(float) }), "RollWeight");
        // 掉落数量修饰（按掉落组）：观察 Id/ratio/返回值
        Patch(harmony, AccessTools.Method(t, "ApplyExtraDropLuckType",
            new[] { typeof(Echoes.GenralDrop), typeof(float) }), "ApplyLuck",
            postfix: nameof(PostfixApplyLuck));

        log.LogInfo("[Probe] diagnostic patches installed");
    }

    private static void Patch(Harmony harmony, MethodBase? original, string tag, string? postfix = null)
    {
        if (original == null) { _log.LogError($"[Probe] FAILED find {tag}"); return; }
        var post = postfix != null ? new HarmonyMethod(typeof(ProbePatches).GetMethod(postfix, All)!) : null;
        harmony.Patch(original,
            prefix: new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(GenericPrefix), All)!),
            postfix: post);
        _log.LogInfo($"[Probe] patched {tag}");
    }

    private static void GenericPrefix(object[] __args, MethodBase __originalMethod)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 5) return;
        _lastLog = DateTime.UtcNow;
        var argStr = string.Join(" | ", __args.Select((a, i) => $"[{i}]={Format(a)}"));
        _log.LogInfo($"[Probe] {__originalMethod.Name}({argStr})");
    }

    private static void PostfixApplyLuck(float __result, object[] __args)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 5) return;
        _lastLog = DateTime.UtcNow;
        var g = __args.Length > 0 && __args[0] is Echoes.GenralDrop gd
            ? $"Id={gd.Id},Name={gd.PackName},Type={gd.PackType},Luck={gd.LuckType}"
            : "?";
        var ratio = __args.Length > 1 ? __args[1] : "?";
        _log.LogInfo($"[Probe] ApplyLuck({g}, ratio={ratio}) => {__result:0.###}");
    }

    private static string Format(object? o)
    {
        if (o == null) return "null";
        if (o is List<int> li)
        {
            var sb = new System.Text.StringBuilder($"List<int>[{li.Count}] {{");
            for (var i = 0; i < Math.Min(li.Count, 30); i++) { if (i > 0) sb.Append(','); sb.Append(li[i]); }
            return sb.Append('}').ToString();
        }
        var s = o.ToString() ?? "";
        return s.Length > 200 ? s[..200] + "..." : s;
    }
}
