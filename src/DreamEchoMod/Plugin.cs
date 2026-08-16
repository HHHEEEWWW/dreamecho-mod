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

        // 临时诊断探针：稀有度掷骰参数观察（限频）
        // [BISECT-1] 临时禁用探针，定位 coreclr 0xc0000005 崩溃源（游戏 8/16 更新后）
        // ProbePatches.Install(Log);
    }
}
