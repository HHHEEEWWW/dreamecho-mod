# 梦境回响 DreamEcho MOD（DreamEchoMod）

为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060，Unity 2022.3.62 IL2CPP）开发的 BepInEx 6 插件。技术链路（BepInEx 注入 → IL2CPP interop 类访问 → HarmonyX 补丁）已打通，当前五个修改方向全部上线：掉落翻倍、T1 词缀、稀有度平均化、自动拾取、一键分解。

## 已实现功能（`src/DreamEchoMod/ModPatches.cs`）

| 功能 | 原理 | 配置 |
|---|---|---|
| 词缀最高档（T1） | `BuildMemoryRandom` Postfix 把选中词缀替换为最高档（数值+标签全变） | `MemoryDropLevel`（已停用）、`MemoryDropPacks`（已停用） |
| 掉落包数量翻倍 | `CreateDrop` 前缀向包列表按倍率追加条目（深度保护仅最外层生效，后缀恢复列表防游戏缓存指数爆炸） | `DropMultiplierPacks` |
| 稀有度平均化 | `RandomDrop` 权重前缀改写为均一值 | `RarityWeights` |
| **自动拾取** | 复用游戏一键拾取（`InteractiveItemManager.AbsorbAllDropItem`），挂 `OnUpdate` 节流 0.5s 全图吸收；**F8** 游戏内开/关 | `AutoAbsorb`、`Interval`、`ToggleKey` |
| **一键分解** | 遍历背包记忆按稀有度过滤 → 调用游戏自带 `DisassembleAll(rarity)` 一次性批量分解（含原初回忆）；**F9** 任意界面触发 | `DisassembleRarities`、`AutoOnEnter`、`DisassembleKey` |

另有诊断探针 `src/DreamEchoMod/ProbePatches.cs`：只读观察稀有度权重、掉落比率、词缀档位、UI 页面打开等链路参数（限频写日志），用于校准配置。

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
| 分解 | `DisassembleRarities` | `Normal,Magic,Rare,Unique,Special` | 一键分解目标稀有度（枚举名，默认全部=清空记忆；空=关闭） |
| 分解 | `AutoOnEnter` | `true` | 进入分解模式时自动分解 |
| 分解 | `DisassembleKey` | `F9` | 一键分解热键（KeyCode 名，任意界面可用） |
| 开关 | `EnableT1 / EnableDropMultiplier / EnableRarityAvg / EnableAutoAbsorb / EnableDisassemble` | `true` | 各功能总开关（排查问题时按个关闭做二分定位） |
| 修复 | `RepairKey` | `F10` | 修复热键：清除背包中"已卸下但仍标记已装备"的记忆状态并存档 |

## 验证方法

1. 从 Steam 启动游戏（或直接运行 `DreamEchoes.exe`）
2. 查看档案日志 `<BPM数据根>\plugin-library\dreamecho-2ffb\<档案id>\BepInEx\LogOutput.log`（入口：`test-equip-bug.bat` 自动定位并打开）
3. 关键行：
   - `Loading [DreamEchoMod 0.1.0]` —— 插件被加载
   - `[Mod] patched DropHelper.BuildMemoryRandom ...` 等 —— 各补丁安装成功
   - `[Mod] BMR prefix: memoryLevel=...`、`[Mod] Rarity [...] ...` —— 游戏内实际触发
   - `[Probe] ...` —— 探针观察输出（含已装备残留专项：`UnEquip(data)` / `DisassembleAll` / `★...发现 N 件残留`）

## 目录结构

```
dreamecho-mod/
  ├─ src/DreamEchoMod/      插件源码（Plugin.cs / ModPatches.cs / ProbePatches.cs，net6.0）
  ├─ il2cpp-dump/           结构导出摘要（BepInEx 自动生成 interop 的说明与数值类线索）
  ├─ docs/                  设计文档 / 实施计划 / 研判报告 / 链路验证结论
  ├─ tools/                 BepInExPack、Cpp2IL、TypeExplorer 等工具（TypeExplorer 入 git）
  └─ build.ps1              构建 + 部署一条命令
```

## 当前状态

- ✅ 技术链路 MVP 完成（详见 `docs/链路验证结论.md`）
- ✅ 掉落链路研判完成（详见 `docs/研判-装备词缀掉落系统.md`）
- ✅ 五大功能上线验收：T1 词缀 / 掉落翻倍 / 稀有度平均化 / 自动拾取（F8）/ 一键分解（F9）
- ✅ 开发链适配 BPM 隔离模式（csproj `$(BepDir)` + build.ps1 doorstop 反推档案部署）
- 🔴 **排查中**：已装备标签残留 bug——F10 修复热键 + 5 功能开关已部署；专项探针
  （`UIBackPackSystem.UnEquip/Equip`、`DisassembleAll`、`CheckMemorySlotType`、`CollectEquippedMemoryUIDs`）
  已部署，跑一轮 `test-equip-bug.bat` 复现即可取证定位元凶

## 相关文档

- 设计：`docs/superpowers/specs/2026-08-13-dreamecho-mod-design.md`
- 计划：`docs/superpowers/plans/2026-08-13-dreamecho-mod.md`
- 研判：`docs/研判-装备词缀掉落系统.md`
- 结论：`docs/链路验证结论.md`
