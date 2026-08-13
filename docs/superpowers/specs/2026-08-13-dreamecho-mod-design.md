# 梦境回响 (DreamEcho) MOD — 设计文档

- 日期：2026-08-13
- 状态：已获用户批准（技术链路验证 MVP）

## 1. 背景与目标

为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060）开发数值修改类 MOD（作弊/增强类，具体数值后定）。

本阶段目标：**跑通技术链路** —— BepInEx 注入 → 游戏 IL2CPP 类结构可访问 → 插件生效。数值修改功能在链路打通后迭代。

## 2. 游戏技术事实（已侦察确认）

| 项目 | 值 |
|---|---|
| 游戏名 | 梦境回响 DreamEcho（Steam AppID 3226060） |
| 安装路径 | `E:\steam\steamapps\common\DreamEcho` |
| 主程序 | `DreamEchoes.exe` |
| 引擎 | Unity + **IL2CPP**（`GameAssembly.dll` 95MB + `il2cpp_data`） |
| global-metadata | `DreamEchoes_Data\il2cpp_data\Metadata\global-metadata.dat`（21.9MB，metadata version 31，Unity 2023.2+/Unity 6 时代） |
| BepInEx 现状 | 未安装 |
| 资源系统 | Addressables + DOTS(EntityScenes) + FMOD 音频 |
| 游戏类型 | 带词缀(affix)系统的刷宝类游戏（自带 `WikiWeb/affix_browser-*.html`） |
| 开发环境 | .NET SDK 9.0.302 ✅ |

## 3. 技术方案（已选定）

**BepInEx 6 (IL2CPP) + Il2CppDumper 导出 + C# 插件**（用户明确指定 BepInEx，排除 MelonLoader；也排除外部内存修改方案）。

## 4. 架构与目录

```
E:\steam\steamapps\common\DreamEcho\            ← 游戏目录（安装 BepInEx 6 + 编译产物）
E:\AI work\item-box\code\dreamecho-mod\        ← MOD 项目（新建，本工作区内）
  ├─ il2cpp-dump\           Il2CppDumper 导出结果（dump.cs + 代理 DLL）
  ├─ src\DreamEchoMod\      插件源码（.NET classlib，net6.0）
  └─ dist\                  编译产物（复制到游戏 BepInEx\plugins\）
```

## 5. 实施步骤

1. **安装 BepInEx 6 IL2CPP**：从 GitHub Releases（BepInEx 6.0.0-be IL2CPP）或 Thunderstore BepInExPack_Il2Cpp 下载，解压到游戏根目录（`winhttp.dll`、`doorstop_config.ini`、`BepInEx\`、`dotnet\`）。
2. **导出游戏类结构**：Il2CppDumper 读取 `GameAssembly.dll` + `global-metadata.dat` → 输出 `dump.cs` + 代理程序集（供插件编译引用）。
   - 回退方案：metadata v31 若导出失败，改用 Il2CppInspector。
3. **创建插件项目** `DreamEchoMod`：引用 BepInEx 6 程序集 + 导出的 `GameAssembly.dll` 代理；入口 `[BepInPlugin]` 类，`Awake()` 打印日志并访问游戏程序集（如读取 `Il2CppSystem` / 游戏 Assembly 名称或版本），证明类结构可访问。
4. **部署与验证**：编译产物复制到 `BepInEx\plugins\DreamEchoMod.dll`，启动游戏，检查 `BepInEx\LogOutput.log`：
   - ✅ 插件加载成功、无异常
   - ✅ 能访问游戏 IL2CPP 类（日志输出证明）
   - ✅ 游戏正常运行不崩溃

## 6. 成功标准（MVP）

- [ ] 游戏启动正常（BepInEx 注入生效）
- [ ] `LogOutput.log` 显示 DreamEchoMod 插件加载成功
- [ ] 日志显示插件成功访问了游戏程序集/类
- [ ] 编译与部署流程可重复（一条命令/脚本完成）

## 7. 风险与回退

| 风险 | 应对 |
|---|---|
| Il2CppDumper 不支持 metadata v31 | 换 Il2CppInspector；或降低目标（先注入+日志，不做类访问） |
| BepInEx 6 与游戏 Unity 版本不兼容 | 换 BepInEx 6 不同 build（be 版迭代快）；确认 GameAssembly 加载顺序 |
| 游戏更新导致结构变化 | 记录 Unity 版本与 metadata 指纹（md5），更新后重新导出 |
| 反作弊 | 单机游戏无反作弊迹象（有 Steamworks.NET 插件），风险低 |

## 8. 后续（不在本阶段范围）

- 确定具体数值修改项（金币/属性/词缀/掉落率等）
- 逆向定位数值所在的类/字段/方法
- 实现 Hook / 字段修改功能并做游戏内验证
