using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace DreamEchoMod;

/// <summary>
/// 正式修改功能：
/// 1) T1Only：词缀强制最高档（Postfix 替换为 MaxLevel 行，无越界风险）
/// 2) DropMultiplier：指定掉落包数量翻倍（Prefix 扩展 packIdList，保持构成）
/// 全部由 BepInEx 配置文件控制（cfg），无需改代码即可调参。
/// </summary>
public static class ModPatches
{
    private static ManualLogSource _log = null!;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // ── 配置 ──
    public static ConfigEntry<bool> EnableT1Only { get; private set; } = null!;
    public static ConfigEntry<string> DropPacks { get; private set; } = null!;
    public static ConfigEntry<float> DropMultiplier { get; private set; } = null!;

    public static void Install(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        EnableT1Only = config.Bind("词缀", "T1Only", true,
            "新掉落装备的词缀强制最高档（T1）。false=原版档位。");
        DropPacks = config.Bind("掉落", "DropMultiplierPacks", "701,711,721,741",
            "要放大的掉落包 ID 列表（逗号分隔）。701/711/721/741=四类材料包；630xx=记忆包。");
        DropMultiplier = config.Bind("掉落", "DropMultiplier", 10f,
            "上述掉落包的掉落数量倍数（1=原版）。");

        var harmony = new Harmony("com.dreamecho.mod");
        var t = typeof(Echoes.Core.Utility.DropHelper);

        // 1. T1 词缀：Get(id, level) 返回后替换为最高档
        var affixGet = AccessTools.Method(typeof(Echoes.Config.TConceptMemoryAffix), "Get",
            new[] { typeof(int), typeof(int) });
        if (affixGet == null) { log.LogError("[Mod] FAILED find TConceptMemoryAffix.Get"); }
        else
        {
            harmony.Patch(affixGet, postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(AffixGetPostfix), All)!));
            log.LogInfo("[Mod] patched TConceptMemoryAffix.Get (T1Only)");
        }

        // 2. 掉落翻倍：CreateDrop 两个重载都 patch
        foreach (var sig in new[]
        {
            new[] { typeof(List<int>), typeof(Vector3), typeof(float) },
            new[] { typeof(List<int>), typeof(Vector3), typeof(int), typeof(int), typeof(HashSet<int>) },
        })
        {
            var m = AccessTools.Method(t, "CreateDrop", sig);
            if (m == null) { log.LogError($"[Mod] FAILED find CreateDrop({string.Join(",", sig.Select(s => s.Name))})"); continue; }
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(CreateDropPrefix), All)!));
            log.LogInfo($"[Mod] patched CreateDrop({string.Join(",", sig.Select(s => s.Name))})");
        }
    }

    // ── T1 词缀：把返回词缀替换为该词缀最高档（MaxLevel）配置行 ──
    private static void AffixGetPostfix(ref Echoes.ConceptMemoryAffix __result,
        Echoes.Config.TConceptMemoryAffix __instance)
    {
        if (!EnableT1Only.Value || __result == null || __result.MaxLevel <= 1) return;
        if (__result.Level >= __result.MaxLevel) return; // 已经是最高档

        var best = __instance.Get(__result.Id, __result.MaxLevel);
        if (best != null && best.Level == __result.MaxLevel)
        {
            _log.LogInfo($"[Mod] T1: affix {__result.Id} level {__result.Level} -> {best.Level} (MaxLevel={best.MaxLevel})");
            __result = best;
        }
    }

    // ── 掉落翻倍：扩展 packIdList 中目标包的数量 ──
    private static void CreateDropPrefix(List<int> packIdList)
    {
        if (DropMultiplier.Value <= 1f || packIdList == null || packIdList.Count == 0) return;

        var targets = DropPacks.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1).Where(v => v > 0).ToHashSet();
        if (targets.Count == 0) return;

        var extra = (int)Math.Floor(DropMultiplier.Value) - 1;
        if (extra <= 0) return;

        // 对列表里的每个目标包，追加 extra 份（列表扩容）
        var toAdd = new List<int>();
        for (var i = 0; i < packIdList.Count; i++)
        {
            if (targets.Contains(packIdList[i]))
                for (var k = 0; k < extra; k++) toAdd.Add(packIdList[i]);
        }
        foreach (var id in toAdd) packIdList.Add(id);

        if (toAdd.Count > 0)
            _log.LogInfo($"[Mod] Drop x{extra + 1}: added {toAdd.Count} packs to {packIdList.Count - toAdd.Count} -> {packIdList.Count}");
    }
}
