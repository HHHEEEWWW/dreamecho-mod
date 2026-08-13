# 阶段性总结：梦境回响 DreamEcho MOD

- 日期：2026-08-14（更新：T1 方案 #4 验证成功 + 0.1.1 兼容验证完成）
- 阶段：装备/掉落/词缀系统 MOD 开发（中期，进行中）
- 项目：`E:\deepseekharness\BeplnEx-mod-workplace\dreamecho-mod`（git main）

---

## 1. 项目目标

为 Steam 游戏《梦境回响 DreamEcho》（AppID 3226060，Unity 2022.3.62 IL2CPP）开发 BepInEx 6 MOD，聚焦：

1. ✅ 掉落数量提升（碎片 ×10、车票 ×2 = 5:1）
2. ✅ 词缀只出最高 T 级（T1）——**方案 #4 验证成功（2026-08-14）**
3. ⚠️ 稀有度调整（平均化已上线，3:1 精确比例待定）
4. ⏳ 一键分解（含传奇）——未开始

## 2. 已完成 ✅

### 2.1 技术链路（MVP，已稳定）
- BepInEx 6.0.0-be.755（IL2CPP）注入游戏根目录
- BepInEx 自动生成 160 个 interop 程序集（`BepInEx/interop/`）
- DreamEchoMod 插件（net6.0）：加载 + HarmonyX Patch 机制验证通过
- 构建部署：`powershell -File build.ps1`

### 2.2 掉落数量放大（已稳定）
- `CreateDrop` 按包放大：`DropMultiplierPacks = 701:10,711:2`
- **双保险防指数爆炸**：深度保护（仅最外层放大）+ Postfix 恢复列表原状（防游戏缓存累积）
- 实测：8 包 → 18 包固定，多次掉落不累积，游戏不卡

### 2.3 稀有度平均化（已上线）
- `RandomDrop` 权重替换：`RarityWeights = 100`（单值=所有档位等概率）
- 实测：掉落物高级/稀有明显增多 ✅
- 注意：不同掉落类型档位数不同（1/2/3/4/5 档），单值配置自动适配

### 2.4 工具链
- `tools/TypeExplorer/`：interop 反射搜索工具（关键词查类型/方法/字段）
- ilspycmd 8.2.0.7535：代理 DLL 反编译查签名/参数名
- 探针框架（ProbePatches）：运行时观察，限频防刷屏

## 3. 游戏机制研究成果（逆向结论）

### 3.1 物品体系
| 内部 | 说明 |
|---|---|
| `Memory` | 装备（记忆），含 `Affixes` 列表、`EquipSlot` |
| `MemoryShard` | 记忆碎片（材料） |
| `ConceptMemoryAffix` | 词缀配置（Luban 表）：Id/Level(档位)/MaxLevel/MutexGroup/AttrMin/AttrMax/Weight |
| `ConceptMemoryAffixPack` | 词缀包（权重掷骰单元） |
| `EMemoryRarityType` | 稀有度：Normal/Magic/Rare/Unique/Special |
| `EConceptType` | 物品分类：Memory/Shard/MapTicket/... |

### 3.2 掉落链路（`DropHelper`）
```
CreateDrop(packIdList, pos, dropRatio)      掉落物生成（按包放大点）
  └ GetDrop(packId, stage, out rarity, dropLevel, excluded, mustDrop)   条目选择
      └ RandomDrop(weights, luckType, rarityIndexList)  稀有度权重掷骰（平均化点）
BuildMemoryRandom(affixPacks, weights, memoryLevel, MinLevel, ...)  → 选中词缀
BuildMemory(drop, memoryLevel, mustTargetList, mustAddAffix)        → 装备生成
BuildMemoryAttr(affix, type, max)           词缀属性生成
```

### 3.3 掉落包身份映射（实测）
| 包 ID | 内容 | 类型 |
|---|---|---|
| 701 | 装备碎片（content 60001~60016，Type=1，Rarity 2/3/4） | PackType=106 |
| 711 | 车票相关（content 8xxxx） | PackType=107 |
| 721 | **记忆装备本体**（content=ConceptMemoryBase ID） | PackType=104 |
| 741 | 金币（content=4） | PackType=201 |
| 630xx | 记忆类（PackType=106，高级图替代 701？待确认） | PackType=106 |

### 3.4 词缀档位机制（关键）
- 词缀档位（T 级）由**查询 level 参数**决定：`TConceptMemoryAffix.Get(id, level)` 返回对应档位行
- `MaxLevel`：词缀最高档编号（7/5/0；0=固定词缀）
- **`BuildMemoryRandom` 的 `memoryLevel` 参数是词缀档位选择的输入**（实测恒 20/40，疑似与难度/车票等级相关）
- 词缀等级需求（req/MinLevelRange）：T1=81（最高），T7=1（最低）

## 4. T1 词缀：攻坚完成 ✅（方案 #4 成功，2026-08-14 实测）

| # | 方案 | 结果 | 结论 |
|---|---|---|---|
| 1 | `TConceptMemoryAffix.Get` postfix 替换为 MaxLevel 档 | **装备 null/词缀全丢** + 标签变但数值不变 | ✗ postfix 内调原方法无限递归（已修）；替换对象不影响数值 roll |
| 2 | `GetDrop` dropLevel→81 | **碎片包 null**（高级图稀有度过滤/条目限制） | ✗ 影响掉落条目选择 |
| 3 | `BuildMemoryRandom` memoryLevel→81（prefix ref） | **词缀全丢**（MinLevel 同改导致词缀包过滤）；且 ref 修改疑未生效 | ✗/疑 |
| 4 | **`BuildMemoryRandom` postfix 替换返回词缀为 MaxLevel 档**（不动任何 level 参数） | ✅ **词缀全最高档，数值/标签都变**，装备正常生成 | ✅ **成功，正式方案** |

**方案 #4 实测证据（日志）**：
- 单局 62 次替换：`affix 50001 L1→L5`、`10002 L4→L7`、`20013 L1→L7`、`22003 L1→L7`…（MaxLevel 7/5/0，0=固定词缀不替换）
- 替换对象 = BMR 返回词缀（数值 roll 的输入对象）→ 数值+标签同时变 T1
- **等级修改（memoryLevel/dropLevel→81）已全部停用**：探针证实 IL2CPP `ref int` 修改**未传回**（prefix 改后探针仍见原值 60/70），且 81 级装备注入低级图疑为 UI 卡死诱因（偶发，ESC 无法返回）；T1 效果与等级修改无关，postfix 独立完成
- memoryLevel 实测分布：81（217 次）/ 70（69 次）/ 60（11 次）——随难度/车票等级变化，非恒 20/40

**关键结论（踩坑沉淀）**：
- HarmonyX IL2CPP 下：**prefix 的 `ref` 值类型参数修改无效**；**postfix 的 `ref` 返回值修改有效**（`__result` 写回）→ 改值走 postfix/返回值，不要依赖 prefix ref
- 词缀显示与数值共用 BMR 返回对象，替换该对象即可两端一致

## 5. 踩坑记录（教训）

1. **指数爆炸（两次）**：
   - 嵌套调用：CreateDrop 内层再触发 → 深度保护
   - **列表被游戏缓存**：Prefix 修改传入列表永久累积（8→18→110→1014→10022）→ **Postfix 恢复列表原状**（关键！）
2. **Postfix 内调用原方法 = 无限递归** → 递归保护标志
3. **日志刷屏卡死**：探针/修改日志量大（6.8万行/局）→ 全部限频（3-5 秒 1 条）
4. **IL2CPP prefix ref 修改无效（已确认）**：无条件日志证实 prefix 执行但修改未传回原方法（探针仍见原值）；**postfix 的 ref 返回值修改有效** → 改值统一走 postfix/返回值链路
5. **BepInEx 自动适配游戏更新**：buildid 更新后 interop 自动重新生成（内置 Cpp2IL 支持 metadata 31.1）。**0.1.1 实测（2026-08-14）**：buildid 24711309→24718487，interop 自动重建，8 个补丁全部安装成功，**MOD 零改动兼容**
6. **Luban 配置类用属性非字段**；`List<int>` 是 `Il2CppSystem.Collections.Generic.List`（不实现 LINQ）
7. **UI 偶发卡死（ESC 无法返回、页面可操作）**：0.1.1 更新后新版（停等级修改）多轮实测未复现；高度怀疑与 81 级装备数据注入相关，已随等级修改停用消除

## 6. 当前配置（`BepInEx/config/com.dreamecho.mod.cfg`）

```ini
[词缀]
MemoryDropLevel = 81        # 【已停用】原等级修改方案（ref 未传回+疑致 UI 卡死），T1 由 postfix 实现
MemoryDropPacks = 721       # 【已停用】同上
[掉落]
DropMultiplierPacks = 701:10,711:2   # 装备碎片×10 : 车票×2 = 5:1
[稀有度]
RarityWeights = 100         # 单值=所有档位等概率（平均化）
```

## 7. 待办

- [x] **T1 方案 #4 验证**（BMR postfix 替换）——✅ 成功，全最高档
- [x] **0.1.1 补丁兼容**（buildid 24718487）——✅ 零改动兼容，8 补丁全装
- [ ] 确认 memoryLevel 来源（车票等级？难度？）——观察日志已有 60/70/81 分布，可结合车票等级对照
- [ ] 一键分解功能（含传奇）——需要定位分解逻辑（背包 UI 相关类）
- [ ] 稀有度精确比例（若需 3:1 而非平均化，配置多值权重）
- [ ] 高级图碎片包确认（630xx 是否替代 701）

## 8. 文件结构

```
dreamecho-mod/
  ├─ src/DreamEchoMod/
  │   ├─ Plugin.cs           入口（Load）
  │   ├─ ModPatches.cs       正式功能（掉落放大/稀有度/词缀）
  │   └─ ProbePatches.cs     诊断探针（限频）
  ├─ tools/TypeExplorer/     interop 反射搜索工具
  ├─ docs/                   设计/计划/研判/验证结论/本总结
  ├─ il2cpp-dump/            导出摘要（候选类线索）
  └─ build.ps1               构建+部署
```
