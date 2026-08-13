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
            "一键分解目标稀有度（EMemoryRarityType 枚举名，逗号分隔）：Special=原初回忆（红色），Unique=传奇，Rare=稀有，Magic=魔法，Normal=普通。默认全部=清空背包记忆。空=关闭。");
        AutoDisassembleOnEnter = config.Bind("分解", "AutoOnEnter", true,
            "进入分解模式（点分解按钮）时自动分解所有目标稀有度记忆。");
        AutoDisassembleKey = config.Bind("分解", "DisassembleKey", "F9",
            "游戏内热键（UnityEngine.KeyCode 名称）：在背包界面按一下立即分解所有目标稀有度记忆。");

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

        // 4b. 全局热键（F8/F9）：挂 InputManager.Update（MonoBehaviour，任何场景/界面都每帧运行）
        var im = typeof(InputManager);
        var imUpdate = AccessTools.Method(im, "Update", Type.EmptyTypes);
        if (imUpdate == null) { log.LogError("[Mod] FAILED find InputManager.Update"); }
        else
        {
            harmony.Patch(imUpdate, postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(InputManagerUpdatePostfix), All)!));
            log.LogInfo("[Mod] patched InputManager.Update (Global hotkeys)");
        }

        // 5. 一键分解（原初回忆）：进入分解模式自动分解 + F9 热键手动触发
        var ubp = typeof(Echoes.UI.UIBackPack);
        var startDis = AccessTools.Method(ubp, "StartDisassembling", Type.EmptyTypes);
        if (startDis == null) { log.LogError("[Mod] FAILED find UIBackPack.StartDisassembling"); }
        else
        {
            harmony.Patch(startDis, postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(StartDisassemblingPostfix), All)!));
            log.LogInfo("[Mod] patched UIBackPack.StartDisassembling (AutoDisassemble)");
        }
        var ubpUpdate = AccessTools.Method(ubp, "Update", Type.EmptyTypes);
        if (ubpUpdate == null) { log.LogError("[Mod] FAILED find UIBackPack.Update"); }
        else
        {
            harmony.Patch(ubpUpdate, postfix: new HarmonyMethod(typeof(ModPatches).GetMethod(nameof(UIBackPackUpdatePostfix), All)!));
            log.LogInfo("[Mod] patched UIBackPack.Update (Disassemble hotkey)");
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

            // F9：一键分解（清空背包记忆）——全局热键，任何界面可用
            if (Enum.TryParse(AutoDisassembleKey.Value, out KeyCode kc9) && UnityEngine.Input.GetKeyDown(kc9))
                TryAutoDisassemble();
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

    // ── 一键分解（清空背包记忆）：全局热键 + 进入分解模式自动触发 ──
    private static void StartDisassemblingPostfix(Echoes.UI.UIBackPack __instance)
    {
        if (AutoDisassembleOnEnter.Value) TryAutoDisassemble();
    }

    private static void UIBackPackUpdatePostfix()
    {
        // 背包界面内热键（保险：OnUpdate 钩子不响应时的兜底）
        if (Enum.TryParse(AutoDisassembleKey.Value, out KeyCode kc) && UnityEngine.Input.GetKeyDown(kc))
            TryAutoDisassemble();
    }

    private static Echoes.UI.UIBackPack? GetBackPackUI()
    {
        try
        {
            // 1) 已打开的背包页面
            var page = Echoes.Core.Managers.UIManager.GetActivePage("UIBackPack") as Echoes.UI.UIBackPack;
            if (page != null) return page;
        }
        catch (Exception e) { _log.LogWarning($"[Mod] GetActivePage failed: {e.Message}"); }
        try
        {
            // 2) 强制创建实例（不显示，仅用于调用数据逻辑）
            var created = Echoes.Core.Managers.UIManager.GetOrCreatePage("UIBackPack") as Echoes.UI.UIBackPack;
            if (created != null) return created;
        }
        catch (Exception e) { _log.LogWarning($"[Mod] GetOrCreatePage failed: {e.Message}"); }
        try
        {
            // 3) 场景中查找（含未激活）
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<Echoes.UI.UIBackPack>();
            if (all != null && all.Length > 0) return all[0];
        }
        catch (Exception e) { _log.LogWarning($"[Mod] FindObjectsOfTypeAll failed: {e.Message}"); }
        return null;
    }

    private static void TryAutoDisassemble()
    {
        try
        {
            // 解析目标稀有度（EMemoryRarityType 枚举名 → int，与 Memory.Rarity 对应）
            var rarities = new HashSet<int>();
            foreach (var n in DisassembleRarities.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse<Echoes.Core.Managers.EMemoryRarityType>(n, true, out var r)) rarities.Add((int)r);
            if (rarities.Count == 0) return;

            var pdm = Echoes.Core.Managers.PlayerDataManager.p_instance;
            var backpackDict = pdm?.PlayerBackpackData?.Backpack;
            if (backpackDict == null)
            {
                _log.LogWarning($"[Mod] AutoDisassemble: PlayerBackpackData.Backpack 为 null (pdm={pdm != null})");
                return;
            }
            if (!backpackDict.ContainsKey(Echoes.Core.Managers.EBackpackItemType.Memory)) return;
            var backpack = backpackDict[Echoes.Core.Managers.EBackpackItemType.Memory];
            if (backpack?.BackpackItems == null) return;

            // 快照目标（避免遍历中修改集合）
            // 注意：Il2Cpp 列表元素是基类包装，C# `is` 会全部失败；必须用 TryCast（基于原生类型检查）
            var targets = new System.Collections.Generic.List<Echoes.Core.Managers.Memory>();
            foreach (var item in backpack.BackpackItems)
            {
                var m = item?.TryCast<Echoes.Core.Managers.Memory>();
                if (m != null && rarities.Contains(m.Rarity)) targets.Add(m);
            }
            if (targets.Count == 0)
            {
                var first = backpack.BackpackItems.Count > 0 ? backpack.BackpackItems[0] : null;
                var firstM = first?.TryCast<Echoes.Core.Managers.Memory>();
                _log.LogInfo($"[Mod] AutoDisassemble: 无目标记忆（BackpackItems.Count={backpack.BackpackItems.Count}，首项 isMemory={(firstM != null)} rarity={firstM?.Rarity ?? -1}，目标={DisassembleRarities.Value}）");
                return;
            }

            var ui = GetBackPackUI();
            if (ui == null)
            {
                _log.LogWarning("[Mod] AutoDisassemble: 找不到 UIBackPack 实例（请先打开一次背包界面）");
                return;
            }

            _log.LogInfo($"[Mod] AutoDisassemble: 找到 {targets.Count} 件目标记忆（稀有度 {DisassembleRarities.Value}），使用游戏自带批量分解");
            foreach (var r in rarities)
            {
                try
                {
                    // 游戏自带"分解全部[稀有度]"，内部一次性处理 + 统一刷新（无逐个动画）
                    ui.DisassembleAll(r);
                    _log.LogInfo($"[Mod]   DisassembleAll(rarity={r}) 完成");
                }
                catch (Exception e)
                {
                    _log.LogWarning($"[Mod]   DisassembleAll({r}) 失败: {e.Message}，回退逐个分解该稀有度");
                    foreach (var m in targets)
                    {
                        if (m.Rarity != r) continue;
                        try { ui.DisassembleMemory(m); }
                        catch (Exception inner) { _log.LogWarning($"[Mod]   回退分解失败 {m.GetMemroyUID()}: {inner.Message}"); }
                    }
                }
            }
            try { ui.RefreshUI(); } catch { }
            _log.LogInfo("[Mod] AutoDisassemble 完成");
        }
        catch (Exception e)
        {
            _log.LogWarning($"[Mod] AutoDisassemble error: {e.Message}");
        }
    }
}
