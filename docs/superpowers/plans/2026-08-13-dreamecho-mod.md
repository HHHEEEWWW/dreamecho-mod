# 梦境回响 DreamEcho MOD（技术链路 MVP）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060，Unity IL2CPP）跑通「BepInEx 6 注入 → IL2CPP 类结构导出 → C# 插件加载并访问游戏类」的完整技术链路。

**Architecture:** 把 BepInEx 6 (IL2CPP) 解压到游戏根目录实现注入；用 Cpp2IL 把 `GameAssembly.dll` + `global-metadata.dat` 导出为 DummyDll 代理程序集放入 `BepInEx/interop/`；新建 .NET C# 插件引用 interop 程序集，`Load()` 时打印 Unity 版本与路径证明类访问成功；产物复制到 `BepInEx/plugins/`。

**Tech Stack:** BepInEx 6.0.755 (IL2CPP x64)、Cpp2IL 2022.0.7、.NET SDK 9.0.302（已装）、C# / net8.0（以 BepInEx 自带运行时为准，见 Task 2）。

## Global Constraints

- 游戏目录：`E:\steam\steamapps\common\DreamEcho`（勿改动游戏原始文件，只新增 BepInEx 相关文件）
- 项目目录：`E:\AI work\item-box\code\dreamecho-mod`
- 禁止使用 MelonLoader；只允许 BepInEx 6 IL2CPP
- 所有命令单行执行，不使用续行符
- `tools/`、`il2cpp-dump/`、`bin/`、`obj/`、`dist/` 不入 git（.gitignore 已配置）
- 数值修改功能不在本阶段范围

---

### Task 1: 下载工具链（BepInExPack_IL2CPP + Cpp2IL）

**Files:**
- Create: `tools/BepInExPack_IL2CPP/BepInExPack_IL2CPP-6.0.755.zip`（下载产物，不入 git）
- Create: `tools/Cpp2IL/Cpp2IL-2022.0.7-Windows.exe`（下载产物，不入 git）

**Interfaces:**
- Produces: Task 2 使用 `tools/BepInExPack_IL2CPP/BepInExPack_IL2CPP-6.0.755.zip`；Task 3 使用 `tools/Cpp2IL/Cpp2IL-2022.0.7-Windows.exe`

- [ ] **Step 1: 创建 tools 目录并下载 BepInExPack_IL2CPP 6.0.755**

```bash
mkdir -p 'E:\AI work\item-box\code\dreamecho-mod\tools\BepInExPack_IL2CPP' 'E:\AI work\item-box\code\dreamecho-mod\tools\Cpp2IL'
curl -L -o 'E:\AI work\item-box\code\dreamecho-mod\tools\BepInExPack_IL2CPP\BepInExPack_IL2CPP-6.0.755.zip' 'https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/'
```

Expected: 下载完成，文件存在（约 40-90MB）。

- [ ] **Step 2: 下载 Cpp2IL 2022.0.7 Windows 版**

```bash
curl -L -o 'E:\AI work\item-box\code\dreamecho-mod\tools\Cpp2IL\Cpp2IL-2022.0.7-Windows.exe' 'https://github.com/SamboyCoding/Cpp2IL/releases/download/2022.0.7/Cpp2IL-2022.0.7-Windows.exe'
```

Expected: 下载完成，文件存在（约 100-200MB 自包含 .NET 单文件）。

- [ ] **Step 3: 验证两个文件**

```bash
ls -la 'E:\AI work\item-box\code\dreamecho-mod\tools\BepInExPack_IL2CPP' 'E:\AI work\item-box\code\dreamecho-mod\tools\Cpp2IL'
unzip -l 'E:\AI work\item-box\code\dreamecho-mod\tools\BepInExPack_IL2CPP\BepInExPack_IL2CPP-6.0.755.zip' | head -30
```

Expected: zip 列表显示 `winhttp.dll`、`doorstop_config.ini`、`BepInEx/` 目录；exe 存在且非 0 字节。

- [ ] **Step 4: 更新 .gitignore 并提交**

`.gitignore` 追加 `tools/` 一行，然后：

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && git add .gitignore && git commit -m "chore: ignore tools directory"
```

---

### Task 2: 安装 BepInEx 6 IL2CPP 到游戏目录

**Files:**
- Create: `E:\steam\steamapps\common\DreamEcho\winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`BepInEx\`、`dotnet\`（来自 BepInExPack 解压）
- Read: `E:\steam\steamapps\common\DreamEcho\BepInEx\dotnet\shared\Microsoft.NETCore.App\`（确定运行时版本 → 决定 Task 4 的 TargetFramework）

**Interfaces:**
- Consumes: Task 1 的 zip
- Produces: 已注入的 BepInEx 环境；`BEPINEX_RUNTIME` 值（net6.0 或 net8.0）传给 Task 4

- [ ] **Step 1: 解压 BepInExPack 到游戏根目录**

```bash
cd 'E:\steam\steamapps\common\DreamEcho' && unzip -o 'E:\AI work\item-box\code\dreamecho-mod\tools\BepInExPack_IL2CPP\BepInExPack_IL2CPP-6.0.755.zip'
```

Expected: 游戏根目录出现 `winhttp.dll`、`doorstop_config.ini`、`BepInEx\`、`dotnet\`。

- [ ] **Step 2: 确认 BepInEx 自带的 .NET 运行时版本**

```bash
ls 'E:\steam\steamapps\common\DreamEcho\BepInEx\dotnet\shared\Microsoft.NETCore.App'
```

Expected: 输出一个版本目录如 `8.0.x` 或 `6.0.x`。**记录该版本**：若为 `8.x` → Task 4 用 `net8.0`；若为 `6.x` → 用 `net6.0`。

- [ ] **Step 3: 创建 interop 目录**

```bash
mkdir -p 'E:\steam\steamapps\common\DreamEcho\BepInEx\interop' 'E:\steam\steamapps\common\DreamEcho\BepInEx\plugins'
```

Expected: 两个目录存在。

- [ ] **Step 4: 验证注入文件齐全**

```bash
ls 'E:\steam\steamapps\common\DreamEcho\winhttp.dll' 'E:\steam\steamapps\common\DreamEcho\doorstop_config.ini' 'E:\steam\steamapps\common\DreamEcho\BepInEx\core\BepInEx.Unity.IL2CPP.dll'
```

Expected: 三个文件都存在（门禁 doorstop + 核心 DLL）。

无 git 提交（文件在游戏目录，不在项目仓库）。

---

### Task 3: 用 Cpp2IL 导出游戏 IL2CPP 类结构

**Files:**
- Create: `il2cpp-dump\dump.cs`、`il2cpp-dump\DummyDll\*.dll`、`il2cpp-dump\script.json`（导出产物，不入 git）
- Create: `il2cpp-dump\导出摘要.md`（人工整理的类线索，入 git）
- Copy: `il2cpp-dump\DummyDll\*.dll` → `E:\steam\steamapps\common\DreamEcho\BepInEx\interop\`

**Interfaces:**
- Consumes: Task 2 的 BepInEx 环境（interop 目录）
- Produces: Task 4 插件引用 `E:\steam\steamapps\common\DreamEcho\BepInEx\interop\UnityEngine.dll`、`UnityEngine.CoreModule.dll` 等

- [ ] **Step 1: 运行 Cpp2IL 导出**

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod\tools\Cpp2IL' && ./Cpp2IL-2022.0.7-Windows.exe --game-path 'E:\steam\steamapps\common\DreamEcho' --output-as il2cppdumper --output-root 'E:\AI work\item-box\code\dreamecho-mod\il2cpp-dump'
```

Expected: 输出 `dump.cs`、`DummyDll\`、`script.json` 等。若报 metadata 版本不支持错误，记录错误信息并停止（回退方案见 Step 4）。

- [ ] **Step 2: 验证导出产物**

```bash
ls 'E:\AI work\item-box\code\dreamecho-mod\il2cpp-dump' && ls 'E:\AI work\item-box\code\dreamecho-mod\il2cpp-dump\DummyDll' | head -20
```

Expected: `dump.cs` 存在且非空；`DummyDll\` 下包含 `Assembly-CSharp.dll`、`UnityEngine.dll`、`UnityEngine.CoreModule.dll` 等。

- [ ] **Step 3: 复制 DummyDll 到 BepInEx/interop/**

```bash
cp 'E:\AI work\item-box\code\dreamecho-mod\il2cpp-dump\DummyDll\'*.dll 'E:\steam\steamapps\common\DreamEcho\BepInEx\interop\'
```

Expected: interop 目录出现全部代理 DLL。

- [ ] **Step 4（回退）：若 Cpp2IL 不支持 metadata v31**

改用 Il2CppDumper（Perfare 归档版 6.7.2，从 `https://github.com/Perfare/Il2CppDumper/releases/download/v6.7.2/Il2CppDumper-v6.7.2.zip` 下载），用法：

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod\tools\Il2CppDumper' && ./Il2CppDumper.exe 'E:\steam\steamapps\common\DreamEcho\GameAssembly.dll' 'E:\steam\steamapps\common\DreamEcho\DreamEchoes_Data\il2cpp_data\Metadata\global-metadata.dat' 'E:\AI work\item-box\code\dreamecho-mod\il2cpp-dump'
```

若两者都失败：本任务中止，把错误信息回报用户（这是计划外风险，需要用户决策）。

- [ ] **Step 5: 写导出摘要并提交**

在 `il2cpp-dump\导出摘要.md` 记录：Unity 版本线索、导出工具与版本、`dump.cs` 大小、DummyDll 数量、以及用 grep 从 dump.cs 搜出的与数值相关的候选类名（如含 gold/money/coin/stat/attribute/level/exp 的类，各列出 5 个以内类全名，供后续阶段参考）。然后：

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && git add 'il2cpp-dump/导出摘要.md' && git commit -m "docs: IL2CPP 导出摘要与数值类线索"
```

---

### Task 4: 创建并编译 DreamEchoMod 插件

**Files:**
- Create: `src/DreamEchoMod/DreamEchoMod.csproj`
- Create: `src/DreamEchoMod/Plugin.cs`

**Interfaces:**
- Consumes: Task 2 的 `BEPINEX_RUNTIME`（net6.0/net8.0）、Task 3 的 interop DLL
- Produces: `src/DreamEchoMod/bin/Release/<runtime>/DreamEchoMod.dll`，Task 5 部署

- [ ] **Step 1: 创建 csproj**（`<TargetFramework>` 用 Task 2 Step 2 记录的值，以下以 net8.0 为例）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>DreamEchoMod</AssemblyName>
    <RootNamespace>DreamEchoMod</RootNamespace>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="BepInEx.Core">
      <HintPath>E:\steam\steamapps\common\DreamEcho\BepInEx\core\BepInEx.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.Unity.IL2CPP">
      <HintPath>E:\steam\steamapps\common\DreamEcho\BepInEx\core\BepInEx.Unity.IL2CPP.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.dll">
      <HintPath>E:\steam\steamapps\common\DreamEcho\BepInEx\core\BepInEx.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>E:\steam\steamapps\common\DreamEcho\BepInEx\interop\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>E:\steam\steamapps\common\DreamEcho\BepInEx\interop\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 创建 Plugin.cs**

```csharp
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
```

- [ ] **Step 3: 编译**

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && dotnet build src/DreamEchoMod/DreamEchoMod.csproj -c Release
```

Expected: `Build succeeded`，产出 `src/DreamEchoMod/bin/Release/<runtime>/DreamEchoMod.dll`。若有引用解析错误（HintPath 找不到），检查 Task 3 Step 3 的 interop 复制是否完成。

- [ ] **Step 4: 提交**

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && git add src/DreamEchoMod && git commit -m "feat: DreamEchoMod 插件骨架（IL2CPP interop 访问验证）"
```

---

### Task 5: 部署插件并启动游戏验证链路

**Files:**
- Copy: `src/DreamEchoMod/bin/Release/<runtime>/DreamEchoMod.dll` → `E:\steam\steamapps\common\DreamEcho\BepInEx\plugins\DreamEchoMod.dll`
- Read: `E:\steam\steamapps\common\DreamEcho\BepInEx\LogOutput.log`

**Interfaces:**
- Consumes: Task 4 的编译产物
- Produces: 链路验证结论（MVP 成功/失败）

- [ ] **Step 1: 复制插件到游戏 plugins 目录**

```bash
cp 'E:\AI work\item-box\code\dreamecho-mod\src\DreamEchoMod\bin\Release\net8.0\DreamEchoMod.dll' 'E:\steam\steamapps\common\DreamEcho\BepInEx\plugins\'
```

（`net8.0` 按实际 TargetFramework 调整）

Expected: 文件复制成功。

- [ ] **Step 2: 备份旧日志并启动游戏**

```bash
cd 'E:\steam\steamapps\common\DreamEcho' && rm -f BepInEx/LogOutput.log && cmd //c start "" DreamEchoes.exe && sleep 30
```

Expected: 游戏窗口出现，等待 30 秒让其完成加载（首次启动 BepInEx 会生成日志）。

- [ ] **Step 3: 检查 BepInEx 日志**

```bash
grep -E 'DreamEchoMod|Chainloader|Error|Exception' 'E:\steam\steamapps\common\DreamEcho\BepInEx\LogOutput.log' | head -40
```

Expected 关键行：
- `Loading [DreamEchoMod 0.1.0]`（插件被 Chainloader 加载）
- `[DreamEchoMod] Plugin loaded!`（Load 执行）
- `[DreamEchoMod] Data path: E:/steam/steamapps/common/DreamEcho/DreamEchoes_Data`（IL2CPP interop 访问成功 ✅）

若出现 `Exception`/`TypeLoadException`/`MissingMethodException`：把完整异常栈记录到 `验证失败日志.txt`（项目根，入 git 前先排除——直接保存到 il2cpp-dump/ 下，不入 git），并回报用户。

- [ ] **Step 4: 关闭游戏进程**

```bash
taskkill //F //IM DreamEchoes.exe
```

Expected: 游戏进程被终止（验证完毕不留后台进程）。

- [ ] **Step 5: 结论**

若 Step 3 四行关键日志全部出现 → MVP 成功，链路打通。在 `docs/链路验证结论.md` 记录：BepInEx 版本、运行时版本、Unity 版本线索、验证日志片段。提交：

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && git add docs && git commit -m "docs: 技术链路验证结论"
```

若失败：保留日志，把失败原因回报用户，不提交结论文档。

---

### Task 6: 可重复构建脚本与 README

**Files:**
- Create: `build.ps1`（构建 + 部署一条命令）
- Create: `README.md`

**Interfaces:**
- Consumes: Task 4 的 csproj
- Produces: 用户可重复执行的部署入口

- [ ] **Step 1: 创建 build.ps1**

```powershell
$GameDir = 'E:\steam\steamapps\common\DreamEcho'
$Proj = 'E:\AI work\item-box\code\dreamecho-mod'
$Runtime = 'net8.0'
dotnet build "$Proj\src\DreamEchoMod\DreamEchoMod.csproj" -c Release
Copy-Item "$Proj\src\DreamEchoMod\bin\Release\$Runtime\DreamEchoMod.dll" "$GameDir\BepInEx\plugins\" -Force
Write-Host '部署完成: DreamEchoMod.dll -> BepInEx\plugins\'
```

- [ ] **Step 2: 创建 README.md**

内容要求：项目用途一句话；前置条件（Steam 游戏、BepInEx 已装、dotnet SDK）；构建部署命令（`powershell -File build.ps1`）；验证方法（启动游戏看 `BepInEx\LogOutput.log`）；目录结构说明；当前状态（技术链路已打通，数值修改待开发）；链接到设计文档与计划。

- [ ] **Step 3: 验证脚本可执行**

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && powershell -ExecutionPolicy Bypass -File build.ps1
```

Expected: 构建成功 + `已复制` 输出，插件 DLL 时间戳更新。

- [ ] **Step 4: 提交**

```bash
cd 'E:\AI work\item-box\code\dreamecho-mod' && git add build.ps1 README.md && git commit -m "docs: 构建部署脚本与 README"
```

---

## Self-Review

- **Spec 覆盖**：Task 2=安装 BepInEx ✅；Task 3=Il2CppDumper/Cpp2IL 导出（含 v31 回退）✅；Task 4=最小插件访问类 ✅；Task 5=启动验证 + LogOutput 检查 ✅；Task 6=可重复流程（spec 成功标准第 4 条）✅；风险表回退方案在 Task 3 Step 4 ✅。
- **占位符扫描**：无 TBD/TODO；唯一变量 `net8.0` 由 Task 2 Step 2 的实测结果决定并在 Task 5/6 标注了按实际调整。
- **类型一致性**：`BasePlugin`/`Load()`/`Log` 为 BepInEx 6 IL2CPP 官方 API；插件 GUID `com.dreamecho.mod` 在 Task 4 与 Task 5 日志检查中一致；interop 路径在 Task 2→3→4 传递一致。
