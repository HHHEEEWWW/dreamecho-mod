using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace DreamEchoMod;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.dreamecho.mod";
    public const string Name = "DreamEchoMod";
    public const string Version = "0.1.0";

    public override void Load()
    {
        Log.LogInfo($"[DreamEchoMod] Plugin loaded! Guid={Guid}");
        Log.LogInfo($"[DreamEchoMod] Unity version: {UnityEngine.Application.version}");
        Log.LogInfo($"[DreamEchoMod] Data path: {UnityEngine.Application.dataPath}");
        Log.LogInfo($"[DreamEchoMod] Persistent path: {UnityEngine.Application.persistentDataPath}");
        Log.LogInfo($"[DreamEchoMod] IL2CPP interop access OK");

        // 正式修改功能（T1 词缀 + 掉落翻倍，配置文件可控）
        ModPatches.Install(Log, Config);

        // 诊断探针：仅装备残留链路（UnEquip/Equip/CheckMemorySlotType/CollectEquipped/DisassembleAll）
        // [BISECT-2] 8/16 游戏更新后全量探针致 coreclr 崩溃，先恢复 equip-only 子集定位
        ProbePatches.Install(Log, equipOnly: true);
    }
}
