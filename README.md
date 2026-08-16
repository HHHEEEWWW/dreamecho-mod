# 梦境回响 DreamEcho MOD（DreamEchoMod）

为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060，Unity 2022.3.62 IL2CPP）开发的 BepInEx 6 插件。技术链路（BepInEx 注入 → IL2CPP interop 类访问 → HarmonyX 补丁）已打通，当前四个正式功能上线：掉落翻倍、T1 词缀、稀有度平均化、自动拾取。

## 已实现功能（`src/DreamEchoMod/ModPatches.cs`）

| 功能 | 原理 | 配置 |
|---|---|---|
| 词缀最高档（T1） | `BuildMemoryRandom` Postfix 把选中词缀替换为最高档（数值+标签全变） | `EnableT1`（总开关） |
| 掉落包数量翻倍 | `CreateDrop` 前缀向包列表按倍率追加条目（深度保护仅最外层生效，后缀恢复列表防游戏缓存指数爆炸） | `EnableDropMultiplier`、`DropMultiplierPacks` |
| 稀有度平均化 | `RandomDrop` 权重前缀改写为均一值 | `EnableRarityAvg`、`RarityWeights` |
| 自动拾取 | 复用游戏一键拾取（`InteractiveItemManager.AbsorbAllDropItem`），挂 `OnUpdate` 节流 0.5s 全图吸收；**F8** 游戏内开/关 | `EnableAutoAbsorb`、`AutoAbsorb`、`Interval`、`ToggleKey` |

> 已移除（游戏作者原生支持，MOD 不再干预）：一键分解（游戏已自带"分解全部装备"）、F10 修复热键、全部诊断探针。相关 cfg 键保留兼容（见配置表【已停用】）。

## 前置条件

- 已安装 Steam 版《梦境回响 DreamEcho》（`E:\steam\steamapps\common\DreamEcho`）
- **BepInEx-Manager（BPM）隔离模式**：BepInEx 整树位于 BPM 档案
  `<BPM数据根>\plugin-library\dreamecho-2ffb\<档案id>\BepInEx\`；游戏根目录只有注入件
  （`winhttp.dll` + `doorstop_config.ini` + `.doorstep_version`），**没有 BepInEx 文件夹是正常形态**
- interop 程序集由 BepInEx 首次启动自动生成到档案 `BepInEx\interop\`（160 个 DLL）
- .NET SDK（编译用；插件运行时由 BepInEx 自带的 .NET 6.0.7 提供）

## 构建与部署

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本逻辑：读游戏目录 `doorstop_config.ini` 的 `target_assembly` 反推出档案 BepInEx 根 →
`dotnet build -p:BepDir=<档案BepInEx>`（csproj 引用全部走 `$(BepDir)`，不写死路径）→
复制 `DreamEchoMod.dll` 到档案 `BepInEx\plugins\`。日志在档案 `BepInEx\LogOutput.log`。

## 配置说明

插件首次加载后生成 `BepInEx\config\com.dreamecho.mod.cfg`，重启游戏生效：

| 节 | 键 | 默认值 | 说明 |
|---|---|---|---|
| 词缀 | `MemoryDropLevel` | `81` | 【已停用】原等级修改方案（IL2CPP ref 修改未传回且疑致 UI 卡死）；T1 由 postfix 强制最高档实现 |
| 词缀 | `MemoryDropPacks` | `721` | 【已停用】同上 |
| 掉落 | `DropMultiplierPacks` | `701:10,711:2` | 包ID:倍数。701=装备碎片；711=车票；721=记忆装备(勿放大)；741=金币 |
| 稀有度 | `RarityWeights` | `100` | 单值=所有档位平均化；多值=按档位逐个指定；空=原版 |
| 自动拾取 | `AutoAbsorb` | `true` | 自动拾取总开关 |
| 自动拾取 | `Interval` | `0.5` | 自动拾取间隔（秒） |
| 自动拾取 | `ToggleKey` | `F8` | 游戏内开/关热键（KeyCode 名） |
| 分解 | `DisassembleRarities` | `Normal,Magic,Rare,Unique,Special` | 【已停用】一键分解目标稀有度（游戏作者已原生添加分解功能） |
| 分解 | `AutoOnEnter` | `false` | 【已停用】进入分解模式时自动分解 |
| 分解 | `DisassembleKey` | `None` | 【已停用】一键分解热键 |
| 开关 | `EnableT1` | `true` | T1 词缀总开关 |
| 开关 | `EnableDropMultiplier` | `true` | 掉落包翻倍总开关 |
| 开关 | `EnableRarityAvg` | `true` | 稀有度平均化总开关 |
| 开关 | `EnableAutoAbsorb` | `true` | 自动拾取总开关 |
| 开关 | `EnableDisassemble` | `false` | 【已停用】一键分解总开关（MOD 不再干预分解） |

## 验证方法

1. 从 Steam 启动游戏（或直接运行 `DreamEchoes.exe`）
2. 查看档案日志 `<BPM数据根>\plugin-library\dreamecho-2ffb\<档案id>\BepInEx\LogOutput.log`
3. 关键行：
   - `Loading [DreamEchoMod 0.1.0]` —— 插件被加载
   - `[Mod] patched DropHelper.BuildMemoryRandom ...` 等 —— 各补丁安装成功
   - `[Mod] BMR prefix: memoryLevel=...`、`[Mod] Rarity [...] ...`、`[Mod] Drop +N packs` —— 游戏内实际触发
   - `[Mod] InputManager.Update heartbeat` —— 全局热键钩子持续运行

## 目录结构

```
dreamecho-mod/
  ├─ src/DreamEchoMod/      插件源码（Plugin.cs / ModPatches.cs，net6.0）
  ├─ il2cpp-dump/           结构导出摘要（interop 说明与数值类线索）
  ├─ docs/                  设计文档 / 实施计划 / 研判报告 / 阶段性总结
  ├─ tools/                 BepInExPack、Cpp2IL、TypeExplorer 等工具（TypeExplorer 入 git）
  ├─ build.ps1              构建 + 部署一条命令（doorstop 反推档案路径）
  └─ test-equip-bug.bat     测试辅助：启动游戏 + 自动打开档案日志（纯 ASCII）
```

## 当前状态

- ✅ 技术链路 MVP 完成（详见 `docs/链路验证结论.md`）
- ✅ 掉落链路研判完成（详见 `docs/研判-装备词缀掉落系统.md`）
- ✅ 四大功能上线：T1 词缀 / 掉落翻倍 / 稀有度平均化 / 自动拾取（F8）
- ✅ 开发链适配 BPM 隔离模式（csproj `$(BepDir)` + build.ps1 doorstop 反推档案部署）
- ✅ 已装备标签残留 bug 已修复（2026-08-16：旧卡组引用清空一次性修复，相关探针/热键已移除）
- ✅ 8/16 游戏更新兼容（buildid 24761955，BepInEx 升级 be.785，patch 全部正常）
- 📋 待办：稀有度 3:1 精确比例（配置多值权重已支持）、玩家分发打包（DLL + INSTALL.md + BepInExPack）

## 相关文档

- 设计：`docs/superpowers/specs/2026-08-13-dreamecho-mod-design.md`
- 计划：`docs/superpowers/plans/2026-08-13-dreamecho-mod.md`
- 研判：`docs/研判-装备词缀掉落系统.md`
- 结论：`docs/链路验证结论.md`
- 总结：`docs/阶段性总结-装备词缀掉落MOD.md`
- 安装：`INSTALL.md`
