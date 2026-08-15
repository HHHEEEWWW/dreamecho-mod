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
        // 数值链路：BuildMemoryAttr 收到的词缀配置（确认 T1 替换是否传递到数值 roll）
        Patch(harmony, AccessTools.Method(t, "BuildMemoryAttr",
            new[] { typeof(Echoes.ConceptMemoryAffix), typeof(Echoes.Core.Managers.EAffixType), typeof(bool) }), "Attr",
            postfix: nameof(PostfixAttr));
        // 词缀生成链路：BuildMemoryRandom 的 memoryLevel（不限频，调用量小）
        var bmr = AccessTools.Method(t, "BuildMemoryRandom",
            new[] { typeof(List<Echoes.ConceptMemoryAffixPack>), typeof(List<int>), typeof(int), typeof(int), typeof(HashSet<int>), typeof(HashSet<int>) });
        if (bmr != null)
            harmony.Patch(bmr, prefix: new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(PrefixBMR), All)!));

        // UI 打开链路：OpenPage 页面名（定位"回忆工作台"分解界面）
        // 注意：OpenPage 有泛型/非泛型两个重载（参数均为 string），AccessTools 会歧义，需精确查找
        var um = typeof(Echoes.Core.Managers.UIManager);
        var openPage = um.GetMethods(All)
            .FirstOrDefault(m => m.Name == "OpenPage" && !m.IsGenericMethod
                                 && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        if (openPage == null) { _log.LogError("[Probe] FAILED find UIManager.OpenPage"); }
        else
        {
            harmony.Patch(openPage, postfix: new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(OpenPagePostfix), All)!));
            _log.LogInfo("[Probe] patched UIManager.OpenPage");
        }
        // UI 按键分发：观察 TAB（UIShowTabUI）等输入事件
        var processKey = AccessTools.Method(um, "ProcessKeyInput",
            new[] { typeof(GamepadModule.InputActionEvent), typeof(bool), typeof(bool) });
        if (processKey == null) { _log.LogError("[Probe] FAILED find UIManager.ProcessKeyInput"); }
        else
        {
            harmony.Patch(processKey, prefix: new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(ProcessKeyPrefix), All)!));
            _log.LogInfo("[Probe] patched UIManager.ProcessKeyInput");
        }

        // 卸下装备链路观察（排查"已装备标签残留"问题）
        Patch(harmony, AccessTools.Method(typeof(Echoes.UI.UIEquipComponent), "Unequip",
            new[] { typeof(Echoes.Core.Enum.EMemorySlotType) }), "EquipUnequip",
            postfix: nameof(EquipUnequipPostfix));
        Patch(harmony, AccessTools.Method(typeof(Echoes.UI.UIEquipMemoryPage), "UnEquip", Type.EmptyTypes), "MemoryPageUnEquip",
            postfix: nameof(MemoryPageUnEquipPostfix));

        // ==== 已装备残留专项探针（2026-08-15 接手后新增）====
        // 数据层卸下（唯一调用者）：卸下前后全背包扫描，残留当场现形
        Patch(harmony, AccessTools.Method(typeof(Echoes.UI.UIBackPackSystem), "UnEquip",
            new[] { typeof(Echoes.Core.Enum.EMemorySlotType) }), "UnEquipData",
            prefix: nameof(UnEquipDataPrefix), postfix: nameof(UnEquipDataPostfix));
        // 数据层装备：记录装备写入的 EquipSlot
        Patch(harmony, AccessTools.Method(typeof(Echoes.UI.UIBackPackSystem), "Equip",
            new[] { typeof(Echoes.Core.Managers.Memory), typeof(int) }), "EquipData",
            postfix: nameof(EquipDataPostfix));
        // UI 层"已装备标签"判定点：CheckMemorySlotType(Memory)
        Patch(harmony, AccessTools.Method(typeof(Echoes.UI.UIBackPackMemoryEquipViewSystem), "CheckMemorySlotType",
            new[] { typeof(Echoes.Core.Managers.Memory) }), "CheckMemorySlotType",
            prefix: nameof(CheckSlotPrefix));
        // F9 分解链路（嫌疑#1）：调用前后残留扫描
        Patch(harmony, AccessTools.Method(typeof(Echoes.UI.UIBackPack), "DisassembleAll",
            new[] { typeof(int) }), "DisassembleAll",
            prefix: nameof(DisassembleAllPrefix), postfix: nameof(DisassembleAllPostfix));
        // 已装备判定数据源：CollectEquippedMemoryUIDs
        Patch(harmony, AccessTools.Method(typeof(Echoes.Core.Managers.PlayerDeckData), "CollectEquippedMemoryUIDs",
            new[] { typeof(HashSet<string>) }), "CollectEquipped",
            prefix: nameof(CollectEquippedPrefix), postfix: nameof(CollectEquippedPostfix));

        log.LogInfo("[Probe] diagnostic patches installed");
    }

    private static void Patch(Harmony harmony, MethodBase? original, string tag, string? prefix = null, string? postfix = null)
    {
        if (original == null) { _log.LogError($"[Probe] FAILED find {tag}"); return; }
        var pre = prefix != null ? new HarmonyMethod(typeof(ProbePatches).GetMethod(prefix, All)!) : null;
        var post = postfix != null ? new HarmonyMethod(typeof(ProbePatches).GetMethod(postfix, All)!) : null;
        harmony.Patch(original,
            prefix: pre ?? new HarmonyMethod(typeof(ProbePatches).GetMethod(nameof(GenericPrefix), All)!),
            postfix: post);
        _log.LogInfo($"[Probe] patched {tag}");
    }

    // ── 已装备残留专项探针方法 ──

    // 数据层卸下（UIBackPackSystem.UnEquip）——卸下后立即全背包扫描，残留当场现形
    private static void UnEquipDataPrefix(Echoes.Core.Enum.EMemorySlotType slotType)
    {
        _log.LogInfo($"[Probe] UnEquip(data): slot={slotType}");
    }

    private static void UnEquipDataPostfix(Echoes.Core.Enum.EMemorySlotType slotType)
    {
        try { DumpEquipResidue($"UnEquip(data) after slot={slotType}"); }
        catch (Exception e) { _log.LogWarning($"[Probe] UnEquipDataPostfix error: {e.Message}"); }
    }

    // UI 层卸下入口 postfix：同样扫描
    private static void EquipUnequipPostfix(Echoes.Core.Enum.EMemorySlotType slot)
    {
        try { DumpEquipResidue($"UIEquipComponent.Unequip after slot={slot}"); }
        catch (Exception e) { _log.LogWarning($"[Probe] EquipUnequipPostfix error: {e.Message}"); }
    }

    private static void MemoryPageUnEquipPostfix()
    {
        try { DumpEquipResidue("UIEquipMemoryPage.UnEquip after"); }
        catch (Exception e) { _log.LogWarning($"[Probe] MemoryPageUnEquipPostfix error: {e.Message}"); }
    }

    // 数据层装备：记录装备写入
    private static void EquipDataPostfix(Echoes.Core.Managers.Memory memoryItem, int slotId)
    {
        try
        {
            _log.LogInfo($"[Probe] Equip(data): uid={(memoryItem != null ? memoryItem.GetMemroyUID() : "null")} slotId={slotId} EquipSlot={(memoryItem != null ? (int)memoryItem.EquipSlot : -1)}");
        }
        catch (Exception e) { _log.LogWarning($"[Probe] EquipDataPostfix error: {e.Message}"); }
    }

    // UI"已装备标签"判定点：只打印 EquipSlot != None 的判定（限频）
    private static void CheckSlotPrefix(Echoes.Core.Managers.Memory memory)
    {
        try
        {
            if (memory == null) return;
            if (memory.EquipSlot == Echoes.Core.Enum.EMemorySlotType.None) return;
            if ((DateTime.UtcNow - _lastLog).TotalSeconds < 2) return;
            _lastLog = DateTime.UtcNow;
            _log.LogInfo($"[Probe] CheckMemorySlotType: uid={memory.GetMemroyUID()} EquipSlot={(int)memory.EquipSlot}");
        }
        catch (Exception e) { _log.LogWarning($"[Probe] CheckSlotPrefix error: {e.Message}"); }
    }

    // F9 分解链路（嫌疑#1）
    private static void DisassembleAllPrefix(int type)
    {
        _log.LogInfo($"[Probe] DisassembleAll(type={type}) CALLED");
    }

    private static void DisassembleAllPostfix(int type)
    {
        try { DumpEquipResidue($"DisassembleAll(type={type}) after"); }
        catch (Exception e) { _log.LogWarning($"[Probe] DisassembleAllPostfix error: {e.Message}"); }
    }

    // 已装备判定数据源观察（限频）
    private static void CollectEquippedPrefix(HashSet<string> buffer)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 30) return;
        _lastLog = DateTime.UtcNow;
        _log.LogInfo($"[Probe] CollectEquippedMemoryUIDs CALLED (buffer before={buffer?.Count ?? -1})");
    }

    private static void CollectEquippedPostfix(HashSet<string> buffer)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 30) return;
        _lastLog = DateTime.UtcNow;
        _log.LogInfo($"[Probe] CollectEquippedMemoryUIDs done: {(buffer != null ? buffer.Count : -1)} uids");
    }

    // 残留检测：背包中 EquipSlot != None 但 UID 不在任何卡组 = 残留现场
    private static void DumpEquipResidue(string tag)
    {
        var pdm = Echoes.Core.Managers.PlayerDataManager.p_instance;
        if (pdm?.PlayerDeckData == null) { _log.LogInfo($"[Probe] {tag}: PlayerDeckData 不可用"); return; }
        var equipped = new HashSet<string>();
        try { pdm.PlayerDeckData.CollectEquippedMemoryUIDs(equipped); }
        catch (Exception e) { _log.LogWarning($"[Probe] {tag}: CollectEquippedMemoryUIDs 失败 {e.Message}"); return; }
        var backpackDict = pdm.PlayerBackpackData?.Backpack;
        if (backpackDict == null || !backpackDict.ContainsKey(Echoes.Core.Managers.EBackpackItemType.Memory))
        {
            _log.LogInfo($"[Probe] {tag}: 背包不可用（backpackDict={backpackDict != null}）");
            return;
        }
        var backpack = backpackDict[Echoes.Core.Managers.EBackpackItemType.Memory];
        if (backpack?.BackpackItems == null) { _log.LogInfo($"[Probe] {tag}: BackpackItems 为空"); return; }

        var residue = new System.Collections.Generic.List<string>();
        var totalEquipped = 0;
        foreach (var item in backpack.BackpackItems)
        {
            var m = item?.TryCast<Echoes.Core.Managers.Memory>();
            if (m == null) continue;
            if (m.EquipSlot == Echoes.Core.Enum.EMemorySlotType.None) continue;
            totalEquipped++;
            if (!equipped.Contains(m.GetMemroyUID()))
                residue.Add($"uid={m.GetMemroyUID()} slot={(int)m.EquipSlot}");
        }
        if (residue.Count > 0)
            _log.LogInfo($"[Probe] ★{tag}: 发现 {residue.Count} 件残留（EquipSlot!=None 共 {totalEquipped}，卡组 {equipped.Count}）：{string.Join("; ", residue)}");
        else
            _log.LogInfo($"[Probe] {tag}: 无残留（EquipSlot!=None {totalEquipped} 件均在卡组）");
    }

    private static void GenericPrefix(object[] __args, MethodBase __originalMethod)
    {
        try
        {
            if ((DateTime.UtcNow - _lastLog).TotalSeconds < 5) return;
            _lastLog = DateTime.UtcNow;
            var argStr = string.Join(" | ", __args.Select((a, i) => $"[{i}]={Format(a)}"));
            _log.LogInfo($"[Probe] {__originalMethod.Name}({argStr})");
        }
        catch (Exception e)
        {
            _log.LogWarning($"[Probe] GenericPrefix error: {e.Message}");
        }
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

    // BuildMemoryRandom 词缀生成链路（memoryLevel 是词缀档位选择的输入）
    private static void PrefixBMR(object[] __args)
    {
        if (__args.Length < 4) return;
        var packCnt = __args[0] is List<Echoes.ConceptMemoryAffixPack> pl ? pl.Count : -1;
        var wCnt = __args[1] is List<int> wl ? wl.Count : -1;
        _log.LogInfo($"[Probe] BMR packs={packCnt} weights={wCnt} memoryLevel={__args[2]} MinLevel={__args[3]}");
    }

    // BuildMemoryAttr 收到的词缀配置详情（数值链路验证）
    private static void PostfixAttr(object[] __args)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 5) return;
        _lastLog = DateTime.UtcNow;
        if (__args.Length > 0 && __args[0] is Echoes.ConceptMemoryAffix a)
        {
            var max = __args.Length > 2 && __args[2] is bool b && b;
            _log.LogInfo($"[Probe] Attr id={a.Id} Level={a.Level} MaxLevel={a.MaxLevel} Min={a.AttrMin:0.##} Max={a.AttrMax:0.##} maxRoll={max} content={Trunc(a.AttrContent)}");
        }
    }

    // UIManager.OpenPage(string) —— 打开页面时打印页面名与 UI 类型（定位 TAB 工作台）
    private static void OpenPagePostfix(string pageName, Echoes.UI.UIBase __result)
    {
        _log.LogInfo($"[Probe] OpenPage: '{pageName}' -> {(__result?.GetType().FullName ?? "null")}");
    }

    // UIManager.ProcessKeyInput —— 观察按键事件（限频；UIShowTabUI=TAB）
    private static void ProcessKeyPrefix(GamepadModule.InputActionEvent inputAction, bool press, bool holding)
    {
        if ((DateTime.UtcNow - _lastLog).TotalSeconds < 2) return;
        _lastLog = DateTime.UtcNow;
        _log.LogInfo($"[Probe] Key: {inputAction.KeyType} press={press} holding={holding}");
    }

    private static string Trunc(string? s) => s == null ? "null" : (s.Length > 60 ? s[..60] : s);

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
