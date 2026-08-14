# DreamEchoMod 安装指南（玩家版）

为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060）制作的增强 MOD。以下步骤适用于**任何玩家**（无需任何编程基础）。

## 功能一览

| 功能 | 说明 | 热键 |
|---|---|---|
| 掉落翻倍 | 装备碎片 ×10、车票 ×2（可配置） | — |
| 词缀最高档 | 掉落的记忆装备词缀全部为最高档（T1） | — |
| 稀有度平均化 | 各稀有度掉落等概率，高级装备明显变多 | — |
| 自动拾取 | 打怪爆出的物品自动飞入背包，无需按键 | **F8** 开关 |
| 一键分解 | 按一下清空背包所有记忆（含原初回忆），一次性批量分解 | **F9** |

## 需要的文件

1. **BepInEx 6（IL2CPP 版）**：`BepInExPack_IL2CPP-6.0.755.zip`
   - 下载：https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/
2. **DreamEchoMod.dll**：MOD 插件本体（找作者获取）

## 安装步骤

### 第 1 步：找到游戏目录

Steam 库 → 右键《梦境回响 DreamEcho》→ 管理 → 浏览本地文件
（默认路径：`...\steamapps\common\DreamEcho`）

### 第 2 步：解压 BepInEx 到游戏目录

把 `BepInExPack_IL2CPP-6.0.755.zip` **整个解压到游戏根目录**（就是有 `DreamEchoes.exe` 的那个文件夹），解压后应看到：
```
DreamEchoes.exe
winhttp.dll          ← 注入器（BepInEx 的）
doorstop_config.ini
BepInEx\             ← 框架目录
dotnet\              ← 运行时
```

### 第 3 步：放入 MOD 插件

把 `DreamEchoMod.dll` 复制到：
```
游戏目录\BepInEx\plugins\DreamEchoMod.dll
```
（`plugins` 文件夹如果不存在就手动新建一个）

### 第 4 步：启动游戏

从 **Steam 启动游戏**（不要直接双击 exe）。

- 第一次启动会比较慢（BepInEx 会自动生成 160 个游戏接口文件到 `BepInEx\interop\`，仅首次需要）
- 正常进入游戏即安装成功

## 验证是否生效

启动后查看日志文件：`游戏目录\BepInEx\LogOutput.log`
看到这几行即成功：
```
Loading [DreamEchoMod 0.1.0]
[DreamEchoMod] Plugin loaded!
[Mod] patched DropHelper.BuildMemoryRandom ...
```

游戏内验证：打怪掉落物自动飞向你（自动拾取）、装备词缀全最高档、背包界面按 **F9** 一键清空记忆。

## 配置修改（可选）

不需要配置也能用全部功能。想调整的话，第一次启动后修改：
`游戏目录\BepInEx\config\com.dreamecho.mod.cfg`

常用项：
```ini
[自动拾取]
AutoAbsorb = true          # 自动拾取总开关
Interval = 0.5             # 拾取间隔（秒）
ToggleKey = F8             # 开/关热键

[分解]
DisassembleRarities = Normal,Magic,Rare,Unique,Special   # 一键分解的稀有度（默认全部分解=清空背包）
DisassembleKey = F9        # 一键分解热键

[掉落]
DropMultiplierPacks = 701:10,711:2   # 碎片×10、车票×2
```

修改后重启游戏生效。

## 常见问题

| 问题 | 处理 |
|---|---|
| 游戏打不开/闪退 | 确认 BepInEx 解压路径正确（winhttp.dll 必须在 exe 旁边）；删除 `BepInEx\interop\` 后重启游戏让它重新生成 |
| 插件没加载（日志无 DreamEchoMod） | 确认 `DreamEchoMod.dll` 在 `BepInEx\plugins\` 下（不要放子文件夹） |
| 游戏更新后 MOD 失效 | Steam 大更新有时会**清除游戏目录里的 BepInEx 框架**（`BepInEx\`、`dotnet\` 整个消失）。处理：重新解压 `BepInExPack_IL2CPP-6.0.755.zip` 到游戏根目录——zip 里是 `BepInExPack\` 子目录，把里面的 `BepInEx\`、`dotnet\`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 全部**移动/复制到游戏根目录**（与 `DreamEchoes.exe` 同级），再把 `DreamEchoMod.dll` 放回 `BepInEx\plugins\`，重新启动游戏（会自动重建接口文件）。若日志出现 `FAILED find`，找作者要新版 DLL |
| 想卸载 MOD | 删除 `BepInEx\plugins\DreamEchoMod.dll` 即可，不影响存档 |

## 说明

- 本 MOD 为单机增强，不含任何联网功能，不影响 Steam 成就与存档
- 不保证所有电脑/游戏版本兼容，遇到问题把 `BepInEx\LogOutput.log` 发给作者即可
