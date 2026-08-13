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
/// 1) MinDropLevel：把掉落等级提升到指定值（默认 81）→ 词缀按等级需求自然可选最高档（T1），
///    完全符合游戏规则（等价于在高等级地图刷装备），不破坏装备生成校验。
/// 2) DropMultiplier：指定掉落包数量翻倍（Prefix 扩展 packIdList，保持构成）。
/// 全部由 BepInEx 配置文件控制（cfg），无需改代码即可调参。
/// </summary>
public static class ModPatches
{
    private static ManualLogSource _log = null!;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // ── 配置 ──
    public static ConfigEntry<int> MinDropLevel { get; private set; } = null!;
    public static ConfigEntry<string> DropPacks { get; private set; } = null!;
    public static ConfigEntry<float> DropMultiplier { get; private set; } = null!;

    public static void Install(ManualLogSource log, ConfigFile config)
    {
        _log = log;

        MinDropLevel = config.Bind("词缀", "MinDropLevel", 81,
            "掉落等级下限（词缀 T 档按等级需求选择；81=T1 最高档可出）。1=原版。");
        DropPacks = config.Bind("掉落", "DropMultiplierPacks", "701,711",
            "要放大的掉落包 ID 列表（逗号分隔）。701=装备碎片(600xx)；711=车票相关(8xxxx)；721=记忆装备(勿放大，会卡)；741=金币。");
        DropMultiplier = config.Bind("掉落", "DropMultiplier", 10f,
            "上述掉落包的掉落数量倍数（1=原版）。");

        var harmony = new Harmony("com.dreamecho.mod");
        var t = typeof(Echoes.Core.Utility.DropHelper);

        // 1. 掉落等级提升（词缀 T1 按规则可出）
        var getDrop = AccessTools.Method(t, "GetDrop",
            new[] { typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(int), typeof(HashSet<int>), typeof(Dictionary<int, List<int>>) });
        if (getDrop == null) { log.LogError("[Mod] FAILED find DropHelper.GetDrop"); }
        else
        {
            harmony.Patch(getDrop, prefix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(GetDropPrefix), All)!));
            log.LogInfo("[Mod] patched DropHelper.GetDrop (MinDropLevel)");
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

    // ── 掉落等级提升：词缀 T 档按等级需求自然提升 ──
    private static void GetDropPrefix(ref int dropLevel)
    {
        if (MinDropLevel.Value > 1 && dropLevel < MinDropLevel.Value)
        {
            _log.LogInfo($"[Mod] dropLevel {dropLevel} -> {MinDropLevel.Value} (T1 词缀可出)");
            dropLevel = MinDropLevel.Value;
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
