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
/// 1) T1Only：词缀强制最高档（Get postfix 替换 MaxLevel 行）
/// 2) MinMemoryLevel：装备生成等级提升到 81（让 T1 词缀通过装备等级校验，防止装备生成失败）
/// 3) DropMultiplier：每包独立倍数放大（格式 "包ID:倍数,包ID:倍数"），保持构成可控
/// 日志全部限频（防止刷屏卡死）。
/// </summary>
public static class ModPatches
{
    private static ManualLogSource _log = null!;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static DateTime _lastLog = DateTime.MinValue;

    // ── 配置 ──
    public static ConfigEntry<bool> EnableT1Only { get; private set; } = null!;
    public static ConfigEntry<int> MinMemoryLevel { get; private set; } = null!;
    public static ConfigEntry<string> DropPacks { get; private set; } = null!;

    public static void Install(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        EnableT1Only = config.Bind("词缀", "T1Only", true,
            "词缀强制最高档（T1）。false=原版。");
        MinMemoryLevel = config.Bind("词缀", "MinMemoryLevel", 81,
            "装备生成等级下限（配合 T1Only 通过等级校验，防止装备生成失败）。1=原版。");
        DropPacks = config.Bind("掉落", "DropMultiplierPacks", "701:10,711:2",
            "掉落放大配置：包ID:倍数,包ID:倍数。701=装备碎片；711=车票；721=记忆装备(勿放大)；741=金币。装备:车票默认 10:2=5:1。");

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

        // 2. 装备等级提升（让 T1 通过校验）
        var buildRandom = AccessTools.Method(t, "BuildMemoryRandom",
            new[] { typeof(List<Echoes.ConceptMemoryAffixPack>), typeof(List<int>), typeof(int), typeof(int), typeof(HashSet<int>), typeof(HashSet<int>) });
        if (buildRandom != null)
        {
            harmony.Patch(buildRandom, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(BuildMemoryRandomPrefix), All)!));
            log.LogInfo("[Mod] patched DropHelper.BuildMemoryRandom (MinMemoryLevel)");
        }
        var buildMemory = AccessTools.Method(t, "BuildMemory",
            new[] { typeof(Echoes.Drop), typeof(int), typeof(List<Echoes.HomeShopTargetedBuyOrder>), typeof(HashSet<int>) });
        if (buildMemory != null)
        {
            harmony.Patch(buildMemory, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(BuildMemoryPrefix), All)!));
            log.LogInfo("[Mod] patched DropHelper.BuildMemory (MinMemoryLevel)");
        }

        // 3. 掉落翻倍（每包独立倍数）：CreateDrop 两个重载
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

    private static void LogRateLimited(string msg)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 3) return;
        _lastLog = DateTime.UtcNow;
        _log.LogInfo(msg);
    }

    // ── T1 词缀：把返回词缀替换为该词缀最高档（MaxLevel）配置行 ──
    // 注意：postfix 里调用 __instance.Get 会再次触发本 patch，必须用递归保护
    [ThreadStatic] private static bool _inT1Patch;

    private static void AffixGetPostfix(ref Echoes.ConceptMemoryAffix __result,
        Echoes.Config.TConceptMemoryAffix __instance)
    {
        if (!EnableT1Only.Value || __result == null || __result.MaxLevel <= 1) return;
        if (__result.Level >= __result.MaxLevel) return;
        if (_inT1Patch) return; // 内部查询（查 MaxLevel 档）不再替换，防无限递归

        _inT1Patch = true;
        try
        {
            var best = __instance.Get(__result.Id, __result.MaxLevel);
            if (best != null && best.Level == __result.MaxLevel)
            {
                LogRateLimited($"[Mod] T1: affix {__result.Id} L{__result.Level}->L{best.Level}");
                __result = best;
            }
        }
        finally
        {
            _inT1Patch = false;
        }
    }

    // ── 装备等级提升 ──
    private static void BuildMemoryRandomPrefix(ref int memoryLevel, ref int MinLevel)
    {
        if (MinMemoryLevel.Value > 1)
        {
            if (memoryLevel < MinMemoryLevel.Value) memoryLevel = MinMemoryLevel.Value;
            if (MinLevel < MinMemoryLevel.Value) MinLevel = MinMemoryLevel.Value;
        }
    }

    private static void BuildMemoryPrefix(ref int memoryLevel)
    {
        if (MinMemoryLevel.Value > 1 && memoryLevel < MinMemoryLevel.Value)
            memoryLevel = MinMemoryLevel.Value;
    }

    // ── 掉落翻倍（每包独立倍数）──
    private static void CreateDropPrefix(List<int> packIdList)
    {
        if (packIdList == null || packIdList.Count == 0) return;

        // 解析 "id:mult,id:mult"
        var mults = new Dictionary<int, int>();
        foreach (var part in DropPacks.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var seg = part.Split(':');
            if (seg.Length != 2) continue;
            if (!int.TryParse(seg[0].Trim(), out var id) || id <= 0) continue;
            if (!int.TryParse(seg[1].Trim(), out var m) || m <= 1) continue;
            mults[id] = m;
        }
        if (mults.Count == 0) return;

        var toAdd = new List<int>();
        for (var i = 0; i < packIdList.Count; i++)
        {
            if (mults.TryGetValue(packIdList[i], out var m))
                for (var k = 0; k < m - 1; k++) toAdd.Add(packIdList[i]);
        }
        foreach (var id in toAdd) packIdList.Add(id);

        if (toAdd.Count > 0)
            LogRateLimited($"[Mod] Drop +{toAdd.Count} packs ({packIdList.Count - toAdd.Count}->{packIdList.Count})");
    }
}
