# 产业统合控制 · Industry Master

《城市：天际线 II》(Cities: Skylines II) 的产业与贸易综合调节模组。用一套设置面板统一管理
货运载重、工厂仓储、产量倍率、进出口价格、虚空进出口、企业虚空收入、空置建筑入驻与虚空岗位，
让"产业城市"的物流和经济按玩家期望的节奏运转，而不是被原版的货车、仓储与订单流程卡住。

> 当前版本 **1.0**，适配游戏 **1.6.4f1**。
> 模组 ID：`IndustryMaster`；面板显示名：**产业统合控制**。

---

## 1. 这个模组解决什么问题

原版《城市：天际线 II》的产业链受三重瓶颈约束：货车数量与载重、工厂仓储上限、进出口订单的
匹配与运输时间。城市规模一大，常见症状是：

- 工厂成品堆到 75% 库存才提出出口请求，仓库却早已塞满，产能被迫闲置；
- 原料订单要等货车往返，工厂间歇停摆；
- 商店与工厂的"经营收入"依赖实际成交，账面长期为零或极低，盈利能力条一路下跌；
- 空置商业/工业建筑迟迟等不到企业入驻；
- 大规模电厂发电量统计在 32 位累加下溢出，面板显示异常。

本模组把这些环节拆成可独立开关的选项：需要真实物流的玩家只开产量与仓储；想让产业脱离货车
瓶颈的玩家开虚空进出口；想让企业账面收入与城市规模匹配的玩家开企业虚空收入。

**设计原则：**除"虚空"类选项（明确标注为直接生成货物或直接入账）外，所有改动都在原版规则内
运行——保留原版配方比例、员工/效率/原料/仓储的限制，不凭空加钱、不瞬间清空仓库。

---

## 2. 安装

1. 下载 Release 中的 `IndustryMaster-1.0.zip`，或从本仓库自行构建（见第 6 节）。
2. 解压出 `IndustryMaster` 文件夹，其中应包含 `mod.json`、`IndustryMaster.dll`、
   `IndustryMaster.mjs`、`0Harmony.dll`。
3. 放入本地模组目录（Windows）：

   ```
   %USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\IndustryMaster\
   ```

4. 启动游戏，在模组/选项界面启用 **产业统合控制**，设置项位于"选项 → 模组 → 产业统合控制"。

> `IndustryMaster.mjs` 是 UI 模块，负责在建筑信息面板的"收入"行标注当日虚空收入。缺少它模组
> 功能不受影响，但面板上不会出现该标注。

---

## 3. 设置项说明

设置界面分为七页：**运输与仓储 / 产量调节 / 进出口 / 进出口价格 / 本地经济 / 库存补充 / 运行状态**。

### 3.1 运输与仓储（Logistics）

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| 启用货车载重调节 | 开 | 允许覆盖载货单元的载重上限 |
| 载重（吨 / 载货单元） | 100 | 100–1000 吨；开启强制满载时缺货自动补足，关闭时按实际订单与库存装货 |
| 强制满载（缺货自动生成） | 开 | 已有运输任务的载货单元在装货阶段补满，缺少部分凭空生成；分批卸货后不反复补货；建筑升级材料订单数量保持原样 |
| 公司运输名额至少 50 辆 | 开 | 保留原本更高的名额；车辆仍按需派出，可用数量受效率影响 |
| 工厂仓储统一 1 万吨 | 开 | 采集型专门产业与普通加工工厂统一为 10,000 吨总仓储；已有超额库存保留，卖出后容量回落 |

### 3.2 产量调节（Production）

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| 启用产量调节 | 开 | 总开关；1 倍为资产原始配方，仍受员工、效率、原料与仓库空间限制 |
| 原料消耗随产量同比变化 | 开 | 开启时保留原配方比例；关闭时提高单位原料的产出 |
| 农业 / 林业 / 矿业 / 石油开采 / 渔业 / 加工工业 / 办公产业 | 1× | 0.1×–100×，按原始配方计算 |

### 3.3 进出口（Export）

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| 无限出口需求（持续外销） | 开 | 有可售成品就持续提出原生出口请求，不等 75% 库存，也不设本模组的外销总额上限。注意：这不是直接加钱或瞬间清空仓库 |
| 保留成品兼作原料的最低用量 | 开 | 避免循环配方把下一批原料全部卖光 |
| 工厂虚空进口 | 关 | 工厂新发出的实物原料采购订单直接从外部市场补进仓库，按实际货款结算，无需货车 |
| 工厂虚空出口 | 关 | 工厂新发出的实物出口订单直接从库存扣货，货款进公司账户，无需货车；适用于加工与采集产业 |

### 3.4 进出口价格（Prices）

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| 启用进出口结算调节 | 开 | 开启后使用分资源报价与价格倍率；单独收取的运输费仍按游戏规则计算 |
| 全局进口 / 出口价格倍率 | 1× | 已应用的资源报价或原版货价乘以该倍率；0 倍分别表示免货款 / 无货款收入 |
| 选择资源 → 覆盖该资源进口/出口价 → 应用 | — | 可为单个资源保存独立价格（0–1,000,000），切换资源后仍保留；单笔结算上限 20 亿，避免原版整数溢出 |

### 3.5 本地经济（Local economy）

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| 本地工厂向商铺直接交货 | 关 | 按原版匹配到本地供货订单后直接扣货、入库并结算，省去发车；原版匹配仍要求可达路线 |
| 每家工厂每日收入 | 0（关） | 每个游戏日向在营的加工与采集企业发放一次，**直接计入企业现金** |
| 每家商家每日收入 | 0（关） | 每个游戏日向在营的商业企业发放一次，直接计入企业现金 |
| 一键入驻并补发收入 | 按钮 | 按原版规则安排企业入驻符合条件的空置房产，并给经营收入为 0 的工厂/商家补发当日收入（当天已到账的跳过）；入驻在模拟恢复后推进 |
| 一键安排 / 撤销虚空岗位 | 按钮 | 给当前符合条件的失业居民安排免通勤岗位；撤销时释放本模组安排的岗位，让居民重新求职 |

### 3.6 库存补充（Inventory）

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| 补货模式 | 关闭 | 关闭 / 持续自动补满 / 每秒补充容量的 10%。按现实时间计时，暂停时停止；原料与成品共用仓储容量，按涉及的实物种类均分目标，保留已有超额库存 |
| 补充商店商品和原料 | 开 | 仅对商业企业生效 |
| 补充工厂原料和成品 | 开 | 开启虚空进口时暂停对应原料补货；开启虚空出口时暂停对应成品补货 |

> 补货生成的库存直接产生，不计作生产收入（面板会单独标注）。

### 3.7 运行状态（Status）

- **修正超大发电量统计溢出**（默认开）：用 64 位汇总显示电力生产量，不修改电厂参数与输电线容量。
- 状态、出口请求数、满载记录、电力统计等实时读数，以及"重新扫描并应用"按钮。

---

## 4. 1.0 的修复：虚空收入现在计入建筑面板盈利

### 现象

开启"每家工厂/商家每日收入"后，企业现金确实每天增加，但打开该公司的建筑信息面板，
"盈利"一行仍只显示原版计算出的产值利润，虚空收入没有体现，读数与账面现金互相矛盾。

### 根因（已对照 1.6.4f1 的 `Game.dll` 反编译确认）

1. 面板的盈利读数来自 `Game.UI.InGame.CompanySection.QueryCompanyData(Entity)`：
   它先 `m_Results.Fill(0)`，再写入 `m_Results[0] = component.m_Income`、
   `m_Results[1] = component.m_Worth`、`m_Results[2] = component.m_Profit`
   （`ResultType` 枚举中 `Profit` 的下标为 2；`component` 即 `CompanyStatisticData`）。
2. `CompanyStatisticData` 由 `Game.Simulation.CompanyEconomyStatisticSystem` 每帧重算，
   其 `m_Profit` 是**产值估算**而非真实到账：
   `m_Income` 按产量 × 价格估算，`m_Profit = m_Income - (租金 + 工资 + 电 + 水 + 污水 + 垃圾 + 税 + 原料成本)`。
3. 本模组的 `VirtualCompanyIncomeSystem` 走的是另一条路：直接把
   `EconomyUtils.AddResources(Resource.Money, 金额, 账户)` 写进企业现金缓冲，并记录当日回执；
   它从未写入 `CompanyStatisticData`，因此盈利行天然看不到这笔钱。

顺带确认：**盈利能力条本身没有问题**。`Game.Companies.Profitability` 由
`(公司总资产 - 上次数值) / 100` 映射而来，而总资产遍历 `Resources`（含 Money），
所以虚空收入早已反映在能力条上——缺陷只存在于那一行"盈利"数字。

### 修复

`source/CompanyIncomePanel.cs` 原有的补丁只负责在"收入"行追加一个"＋（虚空收入）"标注，
1.0 在同一处增加第二个补丁：

- 对 `CompanySection.QueryCompanyData(Entity)` 挂 postfix；
- 从私有嵌套枚举 `ResultType` 反射解析 `Profit` 的真实下标（不硬编码 2），并用
  `MissingMemberException` 守护：字段类型或枚举项变化时补丁拒绝安装，由 `Mod.cs` 记录警告，
  模组其余功能继续工作，不会带病运行；
- 通过 Harmony 的 `___m_Results` 字段注入拿到面板结果数组（`NativeArray<int>` 是对非托管内存的
  视图，写入副本即可修改游戏随后读取的数组），把当日回执 `LatestPayment(entity)` 加进盈利行；
- 加法复用模组内已有的 `DirectTradePolicy.AddSaturated`，防止大额累加造成整数溢出。

结果：盈利行 = 原版产值利润 + 当日虚空收入，与"收入行标注 + 企业实际现金"三方一致。

---

## 5. 兼容性与限制

- 适配 **Cities: Skylines II 1.6.4f1**。`Mod.cs` 会校验游戏 `UpdateSystem` 程序集的 MVID，
  与下面记录不一致时自动停用全部改动并在状态里提示"版本不匹配，修改已停用"：
  `46198354-d4f9-4d23-bf32-7e3c085d8b92`
- 依赖 Harmony（随包附带 `0Harmony.dll`），不需要额外安装 Harmony 或 HookUI 类前置。
- 虚空收入按**游戏日**结算，回执在跨日时清空；因此盈利行显示的是"今日已到账"的口径。
- 本仓库的 1.0 由内部构建线 1.7.2 重新编号而来（详见 CHANGELOG）；1.0 的 DLL 已通过与游戏
  程序集同源编译验证，但**未在游戏内实测**这一具体行为，欢迎在 Issue 中反馈。

---

## 6. 从源码构建

前置：.NET SDK 6 及以上（仅使用其中的 Roslyn `csc`），以及一份本机安装的游戏。

```bash
# bash / MSYS2 / Git Bash
./build/build.sh "D:/steam/steamapps/common/Cities Skylines II"
```

```powershell
# Windows PowerShell
.\build\build.ps1 -GamePath "D:\steam\steamapps\common\Cities Skylines II"
```

构建脚本用 SDK 自带的 `csc` 直接编译，`-nostdlib` 并引用 `Cities2_Data/Managed` 下的
**全部**程序集（游戏自带 Mono 的 `mscorlib.dll` 与 `netstandard.dll` 都在其中）。这是关键：
模组代码用到 `float.IsFinite`、`Dictionary.TryAdd`、`ReadOnlySpan<T>`（netstandard 2.1 表面），
而 Harmony 与游戏程序集又引用 `mscorlib` 里的 `System.Reflection.Emit.Label`——只有同时引用游戏
自己的整套托管程序集才能两者兼得，换成任何 .NET 目标框架包都会编译失败。

产物输出到 `dist/IndustryMaster/`（含 `mod.json`、`.mjs`、`0Harmony.dll`），可直接复制到模组目录。

---

## 7. 仓库结构

```
mod.json                  模组清单（id / 显示名 / 版本 / 适配版本）
IndustryMaster.mjs        UI 模块：建筑面板"收入"行的虚空收入标注
source/                   C# 源码（20 个文件）
  Mod.cs                          入口：设置注册、Harmony 安装、系统注册、版本校验
  Settings.cs                     设置项、双语标签与分组
  VirtualCompanyIncomeSystem.cs   每日虚空收入发放与回执
  CompanyIncomePanel.cs           面板补丁：收入行标注 + 盈利行修正（1.0）
  DirectTradePolicy.cs            虚空进出口共用的价格与结算策略
  DirectFactoryTrade.cs           工厂虚空进口/出口结算
  LocalFactoryTrade.cs            本地工厂→商铺直接交货
  TradeHooks.cs / TradePrices.cs  贸易与价格的 Harmony 挂钩
  InventoryPolicy.cs / InventoryRefill.cs / FullLoading.cs   库存与载重策略
  PrefabController.cs / Policy.cs / TaxHooks.cs / TaxRange.cs  预制体、税制挂钩
  VacantCompanies.cs              空置建筑入驻与零收入补发
  VirtualJobs.cs / VirtualJobHooks.cs   虚空岗位
  ElectricityDisplay.cs           发电量统计 64 位修正
lib/0Harmony.dll          Harmony 运行时（随包分发）
lib/Harmony-LICENSE.txt   Harmony 的 MIT 许可
build/build.sh, build.ps1 构建脚本
.github/workflows/code-review.yml   自动审查 + 版本一致性校验
.github/scripts/review.mjs          审查脚本（含审查规则 SYSTEM 提示词）
CHANGELOG.md              版本记录
```

## 8. 自动代码审查

`.github/workflows/code-review.yml` 在 **push 到 main** 或 **开 PR** 时自动跑：

- **模型审查**：取本次改动的 diff（`*.cs` / `*.mjs` / `*.json` / `*.md` / 脚本，限 6 万字符），
  交给模型审。PR 触发就贴成 PR 评论；push 触发则更新 issue「自动代码审查记录」里那一条机器人
  评论（不会每次刷一条新的）。
- **模型来源**：仓库 secret `REVIEW_API_KEY` + 变量 `REVIEW_API_BASE`、`REVIEW_MODEL`、
  `REVIEW_API_FORMAT`（`openai` 走 `/chat/completions`，`anthropic` 走 `/v1/messages`）。
  换供应商只需 `gh secret set REVIEW_API_KEY` 和改这三个变量，不动代码。没配 key 或模型不可达时
  job 只记一行日志并退出 0（PR 上会留一条"本次未审查"的说明），不让审查故障把提交流程卡红。
- **版本一致性**（另一个 job）：校验 `mod.json` 的 `version`、`IndustryMaster.mjs` 头部的
  `Version:`、`source/Mod.cs` 的 `AssemblyVersion` 三者一致，不一致直接失败。
- **CI 不编译**：`Cities2_Data\Managed` 的游戏程序集不可分发，编译验证只能在本机跑
  `build/build.sh` 或 `build/build.ps1`，所以审查提示词明确要求不报"可能编译不过"这类臆测。

审查口径写在 `.github/scripts/review.mjs` 的 `SYSTEM` 常量里（金钱必须走
`DirectTradePolicy.AddSaturated`、Harmony 补丁必须校验元数据并在失败时抛
`MissingMemberException` 由 `Mod.cs` 降级、反射枚举下标不得硬编码、用户可见文本必须
`Mod.Text(中文, English)` 双语等）。要调教审查重点，改那段提示词即可。

> 例外：`mod.json` 的 `displayName` / `description` 由模组管理器静态读取，运行时拿不到
> `Mod.Text`，只能写字面量（本仓库写成中英并列），提示词里已注明不据此报问题。

## 9. 许可与致谢

- 本仓库源码未附带开源许可协议，版权归仓库所有者。
- 内置的 **Harmony**（`lib/0Harmony.dll`）由 Andreas Pardeike 开发，遵循 MIT 许可，
  许可全文见 `lib/Harmony-LICENSE.txt`。
- 感谢 Colossal Order 与 Paradox Interactive 的《城市：天际线 II》，以及社区对游戏程序集的
  长期测绘工作。
