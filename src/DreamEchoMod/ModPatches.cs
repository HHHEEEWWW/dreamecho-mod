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
    public static ConfigEntry<string> RarityWeights { get; private set; } = null!;

    public static void Install(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        EnableT1Only = config.Bind("词缀", "T1Only", true,
            "词缀强制最高档（T1）。false=原版。");
        MinMemoryLevel = config.Bind("词缀", "MinMemoryLevel", 81,
            "装备生成等级下限（配合 T1Only 通过等级校验，防止装备生成失败）。1=原版。");
        DropPacks = config.Bind("掉落", "DropMultiplierPacks", "701:10,711:2",
            "掉落放大配置：包ID:倍数,包ID:倍数。701=装备碎片；711=车票；721=记忆装备(勿放大)；741=金币。装备:车票默认 10:2=5:1。");
        RarityWeights = config.Bind("稀有度", "RarityWeights", "100,100,100,100,100",
            "掉落稀有度权重（逗号分隔，按普通→稀有顺序）。平均权重=各稀有度等概率；空=原版。");

        var harmony = new Harmony("com.dreamecho.mod");
        var t = typeof(Echoes.Core.Utility.DropHelper);

        // 0. 稀有度倒挂：RandomDrop 的权重数组替换（普通→稀有递增）
        var randomDrop = AccessTools.Method(t, "RandomDrop",
            new[] { typeof(List<int>), typeof(Echoes.Core.Utility.DropHelper.EDropLuckyType), typeof(List<int>) });
        if (randomDrop == null) { log.LogError("[Mod] FAILED find DropHelper.RandomDrop"); }
        else
        {
            harmony.Patch(randomDrop, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(RandomDropPrefix), All)!));
            log.LogInfo("[Mod] patched DropHelper.RandomDrop (RarityInvert)");
        }
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
            harmony.Patch(m,
                prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(CreateDropPrefix), All)!),
                postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(CreateDropPostfix), All)!));
            log.LogInfo($"[Mod] patched CreateDrop({string.Join(",", sig.Select(s => s.Name))})");
        }
    }

    private static void LogRateLimited(string msg)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 3) return;
        _lastLog = DateTime.UtcNow;
        _log.LogInfo(msg);
    }

    // ── 稀有度倒挂：把权重数组替换为配置的「普通→稀有」递增权重 ──
    [ThreadStatic] private static bool _rarityPatched;

    private static void RandomDropPrefix(List<int> weights)
    {
        if (weights == null || weights.Count == 0) return;
        if (_rarityPatched) return; // 防 RandomDrop 内部调 RollDropWeightIndex 再触发（前缀快照）
        var cfg = RarityWeights.Value;
        if (string.IsNullOrWhiteSpace(cfg)) return;

        var parts = cfg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != weights.Count)
        {
            LogRateLimited($"[Mod] RarityWeights 数量({parts.Length})与权重数量({weights.Count})不符，忽略");
            return;
        }
        var vals = new List<int>();
        foreach (var p in parts)
            if (int.TryParse(p, out var v) && v >= 0) vals.Add(v);
        if (vals.Count != weights.Count) return;

        _rarityPatched = true;
        try
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < weights.Count; i++) { if (i > 0) sb.Append(','); sb.Append(weights[i]); }
            var before = sb.ToString();
            for (var i = 0; i < weights.Count; i++) weights[i] = vals[i];
            LogRateLimited($"[Mod] Rarity {before} -> {string.Join(",", vals)}");
        }
        finally
        {
            _rarityPatched = false;
        }
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
    // 两个陷阱都要防：
    // 1) CreateDrop 嵌套调用 → 深度保护（仅最外层放大）
    // 2) 游戏缓存/复用同一个列表对象 → Prefix 放大后 Postfix 必须恢复原状（否则永久累积指数爆炸）
    [ThreadStatic] private static int _dropDepth;
    [ThreadStatic] private static int _addedCount;

    private static void CreateDropPrefix(List<int> packIdList)
    {
        _dropDepth++;
        if (_dropDepth != 1) return; // 内层调用：不放大

        if (packIdList == null || packIdList.Count == 0) return;

        try
        {
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

            _addedCount = 0;
            var originalCount = packIdList.Count;
            for (var i = 0; i < originalCount; i++)
            {
                if (mults.TryGetValue(packIdList[i], out var m))
                {
                    for (var k = 0; k < m - 1; k++)
                    {
                        packIdList.Add(packIdList[i]);
                        _addedCount++;
                    }
                }
            }

            if (_addedCount > 0)
                LogRateLimited($"[Mod] Drop +{_addedCount} packs ({originalCount}->{packIdList.Count})");
        }
        catch (Exception e)
        {
            _log.LogError($"[Mod] CreateDropPrefix error: {e.Message}");
        }
    }

    private static void CreateDropPostfix(List<int> packIdList)
    {
        if (_dropDepth == 1 && _addedCount > 0 && packIdList != null)
        {
            // 恢复列表原状（删除本次添加的末尾元素），防止游戏缓存放大后的列表导致指数爆炸
            for (var i = 0; i < _addedCount && packIdList.Count > 0; i++)
                packIdList.RemoveAt(packIdList.Count - 1);
            _addedCount = 0;
        }
        if (_dropDepth > 0) _dropDepth--;
    }
}
