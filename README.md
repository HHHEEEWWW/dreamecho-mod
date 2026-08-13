# 梦境回响 DreamEcho MOD（DreamEchoMod）

为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060，Unity 2022.3.62 IL2CPP）开发的 BepInEx 6 插件。当前阶段为**技术链路验证 MVP**：BepInEx 注入 → IL2CPP interop 类访问 → 插件加载，已全部打通。数值修改功能待后续阶段开发。

## 前置条件

- 已安装 Steam 版《梦境回响 DreamEcho》（`E:\steam\steamapps\common\DreamEcho`）
- BepInEx 6.0.0-be.755（IL2CPP）已安装到游戏目录（含 `winhttp.dll`、`BepInEx\`、`dotnet\`；interop 程序集由 BepInEx 首次启动时自动生成到 `BepInEx\interop\`）
- .NET SDK（编译用；插件运行时由 BepInEx 自带的 .NET 6.0.7 提供）

## 构建与部署

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

等价于：`dotnet build src\DreamEchoMod -c Release` + 复制 `DreamEchoMod.dll` 到游戏 `BepInEx\plugins\`。

## 验证方法

1. 从 Steam 启动游戏（或直接运行 `DreamEchoes.exe`）
2. 查看 `E:\steam\steamapps\common\DreamEcho\BepInEx\LogOutput.log`
3. 关键行：
   - `Loading [DreamEchoMod 0.1.0]` —— 插件被加载
   - `[DreamEchoMod] Plugin loaded!` —— Load() 执行
   - `[DreamEchoMod] Data path: ...` —— IL2CPP interop 访问成功

## 目录结构

```
dreamecho-mod/
  ├─ src/DreamEchoMod/      插件源码（csproj + Plugin.cs，net6.0）
  ├─ il2cpp-dump/           结构导出摘要（BepInEx 自动生成 interop 的说明与数值类线索）
  ├─ docs/                  设计文档 / 实施计划 / 链路验证结论
  ├─ tools/                 BepInExPack、Cpp2IL 等工具（不入 git）
  └─ build.ps1              构建 + 部署一条命令
```

## 当前状态

- ✅ 技术链路 MVP 完成（详见 `docs/链路验证结论.md`）
- ⏳ 数值修改：候选类已定位（`Echoes.Components.StatsComponent`、`Echoes.Core.Managers.UserManager` 等，见 `il2cpp-dump/导出摘要.md`），具体修改功能待定

## 相关文档

- 设计：`docs/superpowers/specs/2026-08-13-dreamecho-mod-design.md`
- 计划：`docs/superpowers/plans/2026-08-13-dreamecho-mod.md`
- 结论：`docs/链路验证结论.md`
