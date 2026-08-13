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
    }
}
