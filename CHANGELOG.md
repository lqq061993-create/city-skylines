# 更新记录

## 1.0 — 2026-11-27

首个公开版本，由内部构建线 **1.7.2** 重新编号发布（内部 1.5.0 → 1.7.2 依次为 DirectTrade、
Expansion、LocalEconomy、IncomeDisplay、CompanyRecovery 五轮迭代，仓库只保留 1.0 的完整源码）。

### 修复

- **企业虚空收入现在计入建筑信息面板的"盈利"行。**
  原版面板的盈利读数取自 `CompanyStatisticData.m_Profit`，它由
  `CompanyEconomyStatisticSystem` 用"产值估算收入 − 各项支出"算出，看不到本模组直接写进
  企业现金的虚空收入，导致读数与企业账面现金不一致。
  新增 postfix 到 `Game.UI.InGame.CompanySection.QueryCompanyData(Entity)`，把当日回执
  （`VirtualCompanyIncomeSystem.LatestPayment`）加到 `ResultType.Profit` 对应的结果槽位；
  下标由反射解析而非硬编码，加法使用 `DirectTradePolicy.AddSaturated` 防溢出。
  绑定校验失败时抛出 `MissingMemberException`，由 `Mod.cs` 记录警告并让其余功能继续运行。

### 其他

- 版本号统一为 1.0：`mod.json`、`IndustryMaster.mjs` 头部注释、程序集
  `AssemblyVersion`/`AssemblyFileVersion`、启动日志。
- `mod.json` 的 author 改为仓库所有者，描述补记 1.0 的盈利修复。
- 新增 `README.md`（完整功能与设置说明）、`build/build.sh`、`build/build.ps1`、`.gitignore`。

### 既有功能（1.0 内容）

- 货车载重 100–1000 吨、强制满载、公司运输名额下限 50、工厂统一 1 万吨仓储。
- 七类产业 0.1×–100× 产量倍率，可选原料消耗同比缩放。
- 无限出口需求、保留"成品兼作原料"的最低用量、工厂虚空进口/出口。
- 全局与分资源的进出口价格倍率与单资源定价。
- 本地工厂向商铺直接交货。
- 企业每日虚空收入（工厂/商家分别设定，按游戏日结算）。
- 一键安排空置建筑企业入驻并补发零收入企业收入。
- 虚空岗位安排与撤销。
- 库存自动补充（持续补满 / 每秒 10%）。
- 超大发电量统计的 64 位修正。
- 建筑信息面板"收入"行的虚空收入标注（UI 模块 `IndustryMaster.mjs`）。

### 已知限制

- 仅校验适配游戏 1.6.4f1（MVID `46198354-d4f9-4d23-bf32-7e3c085d8b92`），其他版本会自动停用改动。
- 盈利行展示的是"本游戏日已到账"的口径，跨日回执清空后回到原版产值利润。
- 1.0 的编译通过与游戏程序集同源验证，但未在游戏内实测该修复。
