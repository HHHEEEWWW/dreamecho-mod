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
/// 1) T1 词缀：BuildMemoryRandom postfix 替换选中词缀为最高档（已实证生效）。
/// 2) DropMultiplier：指定掉落包数量翻倍（深度保护 + 列表恢复，防指数爆炸）。
/// 3) 稀有度平均化：RandomDrop 权重全部设为同一值（各稀有度等概率）。
/// 4) 自动拾取：复用游戏自带一键拾取（InteractiveItemManager.AbsorbAllDropItem），
///    挂 InteractiveItemManager.OnUpdate 节流调用；配置开关 + F8 热键切换。
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
    public static ConfigEntry<bool> AutoAbsorbEnabled { get; private set; } = null!;
    public static ConfigEntry<float> AutoAbsorbInterval { get; private set; } = null!;
    public static ConfigEntry<string> AutoAbsorbToggle { get; private set; } = null!;
    public static ConfigEntry<string> DisassembleRarities { get; private set; } = null!;
    public static ConfigEntry<bool> AutoDisassembleOnEnter { get; private set; } = null!;
    public static ConfigEntry<string> AutoDisassembleKey { get; private set; } = null!;
    public static ConfigEntry<bool> EnableT1 { get; private set; } = null!;
    public static ConfigEntry<bool> EnableDropMultiplier { get; private set; } = null!;
    public static ConfigEntry<bool> EnableRarityAvg { get; private set; } = null!;
    public static ConfigEntry<bool> EnableAutoAbsorb { get; private set; } = null!;
    public static ConfigEntry<bool> EnableDisassemble { get; private set; } = null!;
    public static ConfigEntry<string> RepairKey { get; private set; } = null!;

    // 自动拾取运行时状态
    private static float _lastAbsorbTime = float.MinValue;
    private static bool _autoAbsorbOn = true;

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
        AutoAbsorbEnabled = config.Bind("自动拾取", "AutoAbsorb", true,
            "自动拾取开关：开启后周期性执行游戏自带的一键拾取（AbsorbAllDropItem），掉落物自动入包。");
        AutoAbsorbInterval = config.Bind("自动拾取", "Interval", 0.5f,
            "自动拾取执行间隔（秒）。0.5=每半秒拾取一次；调大更省资源、拾取稍慢。");
        AutoAbsorbToggle = config.Bind("自动拾取", "ToggleKey", "F8",
            "游戏内热键（UnityEngine.KeyCode 名称）：按一下切换自动拾取开/关（运行时生效，无需重启）。");
        DisassembleRarities = config.Bind("分解", "DisassembleRarities", "Normal,Magic,Rare,Unique,Special",
            "【已停用】一键分解目标稀有度（EMemoryRarityType 枚举名，逗号分隔）：Special=原初回忆（红色），Unique=传奇，Rare=稀有，Magic=魔法，Normal=普通。默认全部=清空背包记忆。空=关闭。游戏作者已添加分解全部装备功能，本功能移除。");
        AutoDisassembleOnEnter = config.Bind("分解", "AutoOnEnter", false,
            "【已停用】进入分解模式（点分解按钮）时自动分解所有目标稀有度记忆。游戏作者已添加分解功能，本功能移除。");
        AutoDisassembleKey = config.Bind("分解", "DisassembleKey", "None",
            "【已停用】游戏内热键（UnityEngine.KeyCode 名称）：在背包界面按一下立即分解所有目标稀有度记忆。游戏作者已添加分解功能，本功能移除。");
        EnableT1 = config.Bind("开关", "EnableT1", true, "T1 词缀（掉落词缀强制最高档）总开关。");
        EnableDropMultiplier = config.Bind("开关", "EnableDropMultiplier", true, "掉落包翻倍总开关。");
        EnableRarityAvg = config.Bind("开关", "EnableRarityAvg", true, "稀有度平均化总开关。");
        EnableAutoAbsorb = config.Bind("开关", "EnableAutoAbsorb", true, "自动拾取总开关。");
        EnableDisassemble = config.Bind("开关", "EnableDisassemble", false,
            "【已停用】一键分解总开关。游戏作者已添加分解功能，MOD 不再干预分解。");
        RepairKey = config.Bind("修复", "RepairKey", "F10",
            "修复热键（UnityEngine.KeyCode 名称）：清除'已卸下但仍显示已装备'的残留状态（对账卡组引用与 EquipSlot 字段，仅修不一致，不动正常装备）。");

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

        // 4. 自动拾取：挂 InteractiveItemManager.OnUpdate（游戏每帧驱动），节流调用一键拾取
        var iim = typeof(Echoes.Core.Managers.InteractiveItemManager);
        var onUpdate = AccessTools.Method(iim, "OnUpdate", Type.EmptyTypes);
        if (onUpdate == null) { log.LogError("[Mod] FAILED find InteractiveItemManager.OnUpdate"); }
        else
        {
            harmony.Patch(onUpdate, postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(OnUpdatePostfix), All)!));
            log.LogInfo("[Mod] patched InteractiveItemManager.OnUpdate (AutoAbsorb)");
        }

        // 4b. 全局热键（F8/F10）：挂 InputManager.Update（MonoBehaviour，任何场景/界面都每帧运行）
        //     F9 分解热键已移除（游戏作者已添加分解全部装备功能，MOD 不再干预分解）
        var im = typeof(InputManager);
        var imUpdate = AccessTools.Method(im, "Update", Type.EmptyTypes);
        if (imUpdate == null) { log.LogError("[Mod] FAILED find InputManager.Update"); }
        else
        {
            harmony.Patch(imUpdate, postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(InputManagerUpdatePostfix), All)!));
            log.LogInfo("[Mod] patched InputManager.Update (Global hotkeys)");
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
        if (!EnableT1.Value) return;
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
        if (!EnableRarityAvg.Value) return;
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
        if (!EnableDropMultiplier.Value) return;
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

    // ── 全局热键：InputManager.Update（MonoBehaviour，任何场景都每帧运行）──
    private static DateTime _lastHeartbeat = DateTime.MinValue;

    private static void InputManagerUpdatePostfix()
    {
        try
        {
            // 心跳日志（每 30 秒 1 条）：验证本钩子在回忆工作台等界面持续运行
            if ((DateTime.UtcNow - _lastHeartbeat).TotalSeconds >= 30)
            {
                _lastHeartbeat = DateTime.UtcNow;
                _log.LogInfo("[Mod] InputManager.Update heartbeat");
            }

            // F8：切换自动拾取
            if (Enum.TryParse(AutoAbsorbToggle.Value, out KeyCode kc8) && UnityEngine.Input.GetKeyDown(kc8))
            {
                _autoAbsorbOn = !_autoAbsorbOn;
                LogRateLimited($"[Mod] AutoAbsorb {( _autoAbsorbOn ? "ON" : "OFF")} (hotkey {kc8})");
            }

            // F9 一键分解已移除（游戏作者已添加分解功能，MOD 不再干预分解）

            // F10：修复"已卸下但仍显示已装备"残留（对账卡组引用与 EquipSlot，仅修不一致）
            if (Enum.TryParse(RepairKey.Value, out KeyCode kc10) && UnityEngine.Input.GetKeyDown(kc10))
                RepairEquipState();
        }
        catch (Exception e)
        {
            _log.LogWarning($"[Mod] InputManagerUpdatePostfix error: {e.Message}");
        }
    }

    // ── 自动拾取：每帧钩子（限频日志验证 OnUpdate 被游戏驱动）──
    private static void OnUpdatePostfix(Echoes.Core.Managers.InteractiveItemManager __instance)
    {
        try
        {
            if (!EnableAutoAbsorb.Value) return;
            if (!_autoAbsorbOn || !AutoAbsorbEnabled.Value) return;

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastAbsorbTime < AutoAbsorbInterval.Value) return;
            _lastAbsorbTime = now;

            // 复用游戏自带一键拾取（行为与手动按吸收键一致）
            if (__instance != null) __instance.AbsorbAllDropItem();
        }
        catch (Exception e)
        {
            _log.LogWarning($"[Mod] AutoAbsorb error: {e.Message}");
        }
    }

    // ── F10 修复：清除"已卸下但仍显示已装备"残留（v2 精确对账版）──
    // 8/16 认知：UI"已装备"按卡组引用判定（CollectEquippedMemoryUIDs 收集全部 MemoryDeck
    // 的 Slot2Memory 引用），与 Memory.EquipSlot 字段可能不一致。卸下 bug 的表现：
    //   方向 A：EquipSlot 已清但非当前卡组仍引用该 UID → UI 仍显示已装备
    //   方向 B：EquipSlot 未清（字段残留），但卡组无引用
    // 修复原则：只对账不一致状态，不动当前卡组的正常装备。
    private static void RepairEquipState()
    {
        try
        {
            var pdm = Echoes.Core.Managers.PlayerDataManager.p_instance;
            if (pdm?.PlayerDeckData == null) { _log.LogWarning("[Mod] Repair: PlayerDeckData 不可用"); return; }
            var deckData = pdm.PlayerDeckData;
            var backpackDict = pdm.PlayerBackpackData?.Backpack;
            if (backpackDict == null || !backpackDict.ContainsKey(Echoes.Core.Managers.EBackpackItemType.Memory))
            { _log.LogWarning("[Mod] Repair: 背包不可用"); return; }
            var backpack = backpackDict[Echoes.Core.Managers.EBackpackItemType.Memory];
            if (backpack?.BackpackItems == null) return;

            // 1. 收集引用集合：全部卡组 + 当前卡组
            var allEquipped = new Il2CppSystem.Collections.Generic.HashSet<string>();
            try { deckData.CollectEquippedMemoryUIDs(allEquipped); } catch (Exception e) { _log.LogWarning($"[Mod] Repair: CollectEquippedMemoryUIDs 失败 {e.Message}"); }

            var curIdx = -1;
            var curDeck = deckData.currentMemoryDeck;
            try { curIdx = curDeck?.Index ?? -1; } catch { }
            var curUids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (curDeck?.Slot2Memory != null)
                    foreach (var kv in curDeck.Slot2Memory)
                        try { curUids.Add(kv.Value?.ToString() ?? ""); } catch { }
            }
            catch (Exception e) { _log.LogWarning($"[Mod] Repair: 读当前卡组引用失败 {e.Message}"); }

            _log.LogInfo($"[Mod] Repair: 当前卡组 index={curIdx} 引用 {curUids.Count} 个；全部卡组引用 {allEquipped.Count} 个；背包记忆 {backpack.BackpackItems.Count} 件");

            // 2. 对账背包记忆
            var fixedSlot = 0;
            var diag = new System.Text.StringBuilder();
            foreach (var item in backpack.BackpackItems)
            {
                var m = item?.TryCast<Echoes.Core.Managers.Memory>();
                if (m == null) continue;
                string uid = "?";
                try { uid = m.GetMemroyUID(); } catch (Exception e) { uid = $"<ERR:{e.Message}>"; }
                var hasSlot = m.EquipSlot != Echoes.Core.Enum.EMemorySlotType.None;
                var inCur = curUids.Contains(uid);
                var inAny = allEquipped.Contains(uid);

                if (hasSlot && !inCur)
                {
                    // 方向 B：字段残留（卡组无引用）→ 清字段
                    m.EquipSlot = Echoes.Core.Enum.EMemorySlotType.None;
                    fixedSlot++;
                    diag.Append($"[{uid} 清字段]");
                }
                else if (!hasSlot && inAny && !inCur)
                {
                    // 方向 A：卡组残留引用（已卸下但非当前卡组仍引用）→ 稍后从卡组移除
                    diag.Append($"[{uid} 卡组残留]");
                }
                else
                {
                    diag.Append(hasSlot ? $"[{uid} 装备中]" : $"[{uid} 空闲]");
                }
            }
            _log.LogInfo($"[Mod] Repair: 对账扫描 {diag}");

            // 3. 方向 A：从非当前卡组移除"已卸下（EquipSlot=None）"的残留引用
            var removedRefs = 0;
            try
            {
                var decks = deckData.MemoryDecks;
                if (decks != null && curIdx != -1)
                {
                    var di = 0;
                    foreach (var d in decks)
                    {
                        di++;
                        if (d == null) continue;
                        var idx = -1;
                        try { idx = d.Index; } catch { }
                        if (idx == curIdx) continue; // 不动当前使用卡组
                        var slot2 = d.Slot2Memory;
                        if (slot2 == null || slot2.Count == 0) continue;
                        // 收集该卡组中引用"空闲记忆"的槽位（EquipSlot=None 且不在当前卡组）
                        var toRemove = new System.Collections.Generic.List<Echoes.Core.Enum.EMemorySlotType>();
                        foreach (var kv in slot2)
                        {
                            var uid = "?";
                            try { uid = kv.Value?.ToString() ?? ""; } catch { continue; }
                            if (curUids.Contains(uid)) continue; // 当前卡组在用，跳过
                            var m = FindBackpackMemory(backpack, uid);
                            if (m != null && m.EquipSlot == Echoes.Core.Enum.EMemorySlotType.None)
                                toRemove.Add(kv.Key);
                        }
                        foreach (var slot in toRemove)
                        {
                            try { slot2.Remove(slot); removedRefs++; }
                            catch (Exception e) { _log.LogWarning($"[Mod] Repair: 移除卡组#{di} 槽 {slot} 失败 {e.Message}"); }
                        }
                        if (toRemove.Count > 0)
                            _log.LogInfo($"[Mod] Repair: 卡组#{di}(index={idx}) 移除 {toRemove.Count} 个残留引用");
                    }
                }
            }
            catch (Exception e) { _log.LogWarning($"[Mod] Repair: 清卡组残留引用失败 {e.Message}"); }

            // 4. 存档
            if (fixedSlot > 0 || removedRefs > 0)
            {
                try { Echoes.Core.Managers.PlayerDataManager.Save(); } catch (Exception e) { _log.LogWarning($"[Mod] Repair: 存档失败 {e.Message}"); }
                _log.LogInfo($"[Mod] Repair: 修复完成——清字段 {fixedSlot} 件 + 移除卡组残留引用 {removedRefs} 个，已存档（重进/刷新界面后生效）");
            }
            else
            {
                _log.LogInfo($"[Mod] Repair: 未发现残留（全部一致）");
            }
        }
        catch (Exception e)
        {
            _log.LogWarning($"[Mod] Repair error: {e.Message}");
        }
    }

    // 按 UID 在背包中查找记忆（Il2Cpp 列表元素需 TryCast）
    private static Echoes.Core.Managers.Memory? FindBackpackMemory(Echoes.Core.Managers.Backpack backpack, string uid)
    {
        if (backpack?.BackpackItems == null || string.IsNullOrEmpty(uid)) return null;
        foreach (var item in backpack.BackpackItems)
        {
            var m = item?.TryCast<Echoes.Core.Managers.Memory>();
            if (m == null) continue;
            string u = "?";
            try { u = m.GetMemroyUID(); } catch { continue; }
            if (u == uid) return m;
        }
        return null;
    }

}
