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
/// 1) 装备等级提升（只对装备包 GetDrop 提升 dropLevel）→ 词缀按等级需求自然可选最高档（T1），
///    完全符合游戏规则，不影响掉落条目选择（碎片/金币包不动）。
/// 2) DropMultiplier：指定掉落包数量翻倍（深度保护 + 列表恢复，防指数爆炸）。
/// 3) 稀有度平均化：RandomDrop 权重全部设为同一值（各稀有度等概率）。
/// </summary>
public static class ModPatches
{
    private static ManualLogSource _log = null!;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static DateTime _lastLog = DateTime.MinValue;

    // ── 配置 ──
    public static ConfigEntry<int> MemoryDropLevel { get; private set; } = null!;
    public static ConfigEntry<string> MemoryPacks { get; private set; } = null!;
    public static ConfigEntry<string> DropPacks { get; private set; } = null!;
    public static ConfigEntry<string> RarityWeights { get; private set; } = null!;

    public static void Install(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        MemoryDropLevel = config.Bind("词缀", "MemoryDropLevel", 81,
            "【已停用】原装备掉落等级下限方案（IL2CPP ref 修改未传回且疑破坏装备 UI）。T1 改由 postfix 强制最高档实现。");
        MemoryPacks = config.Bind("词缀", "MemoryDropPacks", "721",
            "【已停用】原视为装备包并提升掉落等级的包 ID 列表（逗号分隔）。");
        DropPacks = config.Bind("掉落", "DropMultiplierPacks", "701:10,711:2",
            "掉落放大配置：包ID:倍数,包ID:倍数。701=装备碎片；711=车票；721=记忆装备(勿放大)；741=金币。");
        RarityWeights = config.Bind("稀有度", "RarityWeights", "100",
            "掉落稀有度权重：单个数字=所有档位平均化（推荐 100）；多个数字=按档位逐个指定；空=原版。");

        var harmony = new Harmony("com.dreamecho.mod");
        var t = typeof(Echoes.Core.Utility.DropHelper);

        // 1. 词缀 T1：仅 postfix 替换选中词缀为最高档（已实证生效 62 次替换）。
        //    prefix 的 memoryLevel 修改已停用：探针证实 ref 修改未传回，且 81 级装备疑为 UI 卡死根源。
        var bmr = AccessTools.Method(t, "BuildMemoryRandom",
            new[] { typeof(List<Echoes.ConceptMemoryAffixPack>), typeof(List<int>), typeof(int), typeof(int), typeof(HashSet<int>), typeof(HashSet<int>) });
        if (bmr == null) { log.LogError("[Mod] FAILED find DropHelper.BuildMemoryRandom"); }
        else
        {
            harmony.Patch(bmr,
                prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(BuildMemoryRandomPrefix), All)!),
                postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(BuildMemoryRandomPostfix), All)!));
            log.LogInfo("[Mod] patched DropHelper.BuildMemoryRandom (observe prefix + T1 postfix)");
        }

        // 1b. GetDrop dropLevel 修改已停用（同上理由），仅观察
        var getDrop = AccessTools.Method(t, "GetDrop",
            new[] { typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(int), typeof(HashSet<int>), typeof(Dictionary<int, List<int>>) });
        if (getDrop == null) { log.LogError("[Mod] FAILED find DropHelper.GetDrop"); }
        else
        {
            harmony.Patch(getDrop, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(GetDropPrefix), All)!));
            log.LogInfo("[Mod] patched DropHelper.GetDrop (observe only)");
        }

        // 2. 掉落翻倍：CreateDrop 两个重载（深度保护 + 列表恢复）
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

        // 3. 稀有度平均化
        var randomDrop = AccessTools.Method(t, "RandomDrop",
            new[] { typeof(List<int>), typeof(Echoes.Core.Utility.DropHelper.EDropLuckyType), typeof(List<int>) });
        if (randomDrop == null) { log.LogError("[Mod] FAILED find DropHelper.RandomDrop"); }
        else
        {
            harmony.Patch(randomDrop, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(RandomDropPrefix), All)!));
            log.LogInfo("[Mod] patched DropHelper.RandomDrop (RarityAvg)");
        }
    }

    private static void LogRateLimited(string msg)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 3) return;
        _lastLog = DateTime.UtcNow;
        _log.LogInfo(msg);
    }

    // ── 观察模式：只打印 memoryLevel（修改已停用——探针证实 IL2CPP ref 修改未传回）──
    private static void BuildMemoryRandomPrefix(int memoryLevel)
    {
        _log.LogInfo($"[Mod] BMR prefix(observe): memoryLevel={memoryLevel}");
    }

    // ── T1 词缀：BMR 返回的选中词缀替换为最高档（Get 未被 patch，无递归）──
    private static void BuildMemoryRandomPostfix(ref Il2CppSystem.ValueTuple<Echoes.ConceptMemoryAffix, Echoes.ConceptMemoryAffixPack> __result)
    {
        if (__result.Item1 == null) return;
        var affix = __result.Item1;
        if (affix.MaxLevel <= 1 || affix.Level >= affix.MaxLevel) return;
        var best = Echoes.ConfigManager.Tables.TConceptMemoryAffix.Get(affix.Id, affix.MaxLevel);
        if (best != null && best.Level == affix.MaxLevel)
        {
            LogRateLimited($"[Mod] BMR T1: affix {affix.Id} L{affix.Level}->L{best.Level} (min={best.AttrMin:0.#} max={best.AttrMax:0.#})");
            __result.Item1 = best;
        }
    }

    // ── 观察模式：只打印装备包 dropLevel（修改已停用——同上理由）──
    private static void GetDropPrefix(int packId, int dropLevel)
    {
        LogRateLimited($"[Mod] GetDrop(observe): pack={packId} dropLevel={dropLevel}");
    }

    // ── 稀有度平均化：权重全部设为同一值 ──
    [ThreadStatic] private static bool _rarityPatched;

    private static void RandomDropPrefix(List<int> weights)
    {
        if (weights == null || weights.Count == 0) return;
        if (_rarityPatched) return;
        var cfg = RarityWeights.Value;
        if (string.IsNullOrWhiteSpace(cfg)) return;

        var parts = cfg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var vals = new List<int>();
        if (parts.Length == 1)
        {
            // 单值：所有档位平均化
            if (!int.TryParse(parts[0], out var v) || v <= 0) return;
            for (var i = 0; i < weights.Count; i++) vals.Add(v);
        }
        else
        {
            // 多值：按长度匹配
            if (parts.Length != weights.Count) return;
            foreach (var p in parts)
                if (int.TryParse(p, out var v) && v >= 0) vals.Add(v);
            if (vals.Count != weights.Count) return;
        }

        _rarityPatched = true;
        try
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < weights.Count; i++) { if (i > 0) sb.Append(','); sb.Append(weights[i]); }
            var before = sb.ToString();
            for (var i = 0; i < weights.Count; i++) weights[i] = vals[i];
            LogRateLimited($"[Mod] Rarity [{weights.Count}] {before} -> {string.Join(",", vals)}");
        }
        finally
        {
            _rarityPatched = false;
        }
    }

    // ── 掉落翻倍（深度保护 + 列表恢复）──
    [ThreadStatic] private static int _dropDepth;
    [ThreadStatic] private static int _addedCount;

    private static void CreateDropPrefix(List<int> packIdList)
    {
        _dropDepth++;
        if (_dropDepth != 1) return;
        if (packIdList == null || packIdList.Count == 0) return;

        try
        {
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
                    for (var k = 0; k < m - 1; k++)
                    {
                        packIdList.Add(packIdList[i]);
                        _addedCount++;
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
            for (var i = 0; i < _addedCount && packIdList.Count > 0; i++)
                packIdList.RemoveAt(packIdList.Count - 1);
            _addedCount = 0;
        }
        if (_dropDepth > 0) _dropDepth--;
    }
}
