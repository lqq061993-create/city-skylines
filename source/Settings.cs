using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Economy;
using Game.Modding;
using Game.Settings;

namespace IndustryMaster
{
    [FileLocation("ModsSettings/IndustryMaster/IndustryMaster")]
    [SettingsUITabOrder("Logistics", "Production", "Export", "Prices", "LocalEconomy", "Inventory", "Status")]
    [SettingsUIGroupOrder("Truck", "Storage", "Yields", "DirectTrade", "Trading", "PriceMultipliers", "PriceEditor", "LocalTrade", "VirtualIncome", "CompanyRecovery", "VirtualJobs", "Refill", "Live")]
    [SettingsUIShowGroupName("Truck", "Storage", "Yields", "DirectTrade", "Trading", "PriceMultipliers", "PriceEditor", "LocalTrade", "VirtualIncome", "CompanyRecovery", "VirtualJobs", "Refill", "Live")]
    public sealed class MasterSettings : ModSetting
    {
        [SettingsUISection("Logistics", "Truck")]
        public bool TruckEnabled { get; set; }
        [SettingsUISection("Logistics", "Truck")]
        [SettingsUISlider(min = 100, max = 1000, step = 10, scalarMultiplier = 1, unit = "integer")]
        public int TruckTonnes { get; set; }
        [SettingsUISection("Logistics", "Truck")]
        public bool ForceFullLoading { get; set; }
        [SettingsUISection("Inventory", "Refill")]
        public InventoryMode InventoryRefillMode { get; set; }
        [SettingsUISection("Inventory", "Refill")]
        public bool RefillShops { get; set; }
        [SettingsUISection("Inventory", "Refill")]
        public bool RefillFactories { get; set; }
        [SettingsUISection("Inventory", "Refill")]
        public string InventoryStatus => Mod.Current?.InventoryStatus??"";
        [SettingsUISection("Logistics", "Truck")]
        public bool FleetEnabled { get; set; }
        [SettingsUISection("Logistics", "Storage")]
        public bool StorageEnabled { get; set; }
        [SettingsUISection("Logistics", "Storage")]
        public string StorageTarget => Mod.Text("每家采集与加工工厂总仓储10,000吨", "10,000 tonnes total per extractor / factory");
        [SettingsUISection("Production", "Yields")]
        public bool ProductionEnabled { get; set; }
        [SettingsUISection("Production", "Yields")]
        public bool ScaleInputs { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Farming { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Forestry { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Mining { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Oil { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Fishing { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Manufacturing { get; set; }
        [SettingsUISection("Production", "Yields")]
        [SettingsUISlider(min = 0.1f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float Office { get; set; }
        [SettingsUISection("Export", "Trading")]
        public bool ContinuousExports { get; set; }
        [SettingsUISection("Export", "Trading")]
        public bool KeepInputReserve { get; set; }
        [SettingsUISection("Export", "Trading")]
        public string ExportScope => Mod.Text("持续外销会增加出口订单。虚空出口开启时，符合条件的工厂订单直接结算。", "Continuous offers generate more export orders. Direct exports settle eligible factory orders immediately.");
        [SettingsUISection("Export", "DirectTrade")]
        public bool DirectFactoryImports { get; set; }
        [SettingsUISection("Export", "DirectTrade")]
        public bool DirectFactoryExports { get; set; }
        [SettingsUISection("Export", "DirectTrade")]
        public string DirectTradeStatus => Mod.Current?.DirectTradeStatus ?? "";
        [SettingsUISection("LocalEconomy", "LocalTrade")]
        public bool LocalFactoryTradeEnabled { get; set; }
        [SettingsUISection("LocalEconomy", "LocalTrade")]
        public string LocalTradeStatus => Mod.Current?.LocalTradeStatus ?? "";
        [SettingsUISection("LocalEconomy", "VirtualIncome")]
        [SettingsUISlider(min = 0, max = VirtualCompanyIncomeSystem.MaximumDailyIncome, step = 100, scalarMultiplier = 1, unit = "money")]
        public int FactoryIncomePerDay { get; set; }
        [SettingsUISection("LocalEconomy", "VirtualIncome")]
        [SettingsUISlider(min = 0, max = VirtualCompanyIncomeSystem.MaximumDailyIncome, step = 100, scalarMultiplier = 1, unit = "money")]
        public int CommercialIncomePerDay { get; set; }
        [SettingsUISection("LocalEconomy", "VirtualIncome")]
        public string CompanyIncomeStatus => Mod.Current?.CompanyIncomeStatus ?? "";
        [SettingsUISection("LocalEconomy", "CompanyRecovery")]
        [SettingsUIButton]
        public bool RecoverCompanies { set { Mod.Current?.QueueCompanyRecovery(); } }
        [SettingsUISection("LocalEconomy", "CompanyRecovery")]
        public string CompanyRecoveryStatus => Mod.Current?.CompanyRecoveryStatus ?? "";
        [SettingsUISection("LocalEconomy", "VirtualJobs")]
        [SettingsUIButton]
        public bool AssignVirtualJobs { set { Mod.Current?.QueueVirtualJobs(assign: true); } }
        [SettingsUISection("LocalEconomy", "VirtualJobs")]
        [SettingsUIButton]
        public bool ReleaseVirtualJobs { set { Mod.Current?.QueueVirtualJobs(assign: false); } }
        [SettingsUISection("LocalEconomy", "VirtualJobs")]
        public string VirtualJobsStatus => Mod.Current?.VirtualJobsStatus ?? "";
        [SettingsUISection("Prices", "PriceEditor")]
        public bool PricesEnabled { get; set; }
        [SettingsUISection("Prices", "PriceMultipliers")]
        [SettingsUISlider(min = 0f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float ImportPriceMultiplier { get; set; }
        [SettingsUISection("Prices", "PriceMultipliers")]
        [SettingsUISlider(min = 0f, max = 100f, step = 0.1f, scalarMultiplier = 1, unit = "floatSingleFraction")]
        public float ExportPriceMultiplier { get; set; }
        internal float PriceMultiplier(bool import) => Policy.PriceMultiplier(import ? ImportPriceMultiplier : ExportPriceMultiplier);
        private TradeResource _priceResource;
        private bool _draftReady;
        [SettingsUISection("Prices", "PriceEditor")]
        public TradeResource PriceResource { get => _priceResource; set { _priceResource = value; LoadPriceDraft(); } }
        [SettingsUISection("Prices", "PriceEditor")]
        public bool OverrideImport { get; set; }
        [SettingsUISection("Prices", "PriceEditor")]
        [SettingsUISlider(min = 0, max = 1000000, step = 1, scalarMultiplier = 1, unit = "money")]
        public float ImportPrice { get; set; }
        [SettingsUISection("Prices", "PriceEditor")]
        public bool OverrideExport { get; set; }
        [SettingsUISection("Prices", "PriceEditor")]
        [SettingsUISlider(min = 0, max = 1000000, step = 1, scalarMultiplier = 1, unit = "money")]
        public float ExportPrice { get; set; }
        [SettingsUISection("Prices", "PriceEditor")]
        [SettingsUIButton]
        public bool ApplyResourcePrice { set { Mod.Current?.SaveSelectedPrice(this); } }
        [SettingsUISection("Prices", "PriceEditor")]
        [SettingsUIButton]
        public bool ResetResourcePrice { set { OverrideImport = OverrideExport = false; Mod.Current?.SaveSelectedPrice(this); } }
        [SettingsUISection("Prices", "PriceEditor")]
        public string CurrentPrices => Mod.Current?.Prices?.Describe(PriceResource) ?? Mod.Text("载入城市后可读取原价", "Load a city to read base prices");
        [SettingsUISection("Status", "Live")]
        public bool FixPowerDisplay { get; set; }
        [SettingsUISection("Status", "Live")]
        public string PowerStatus => Mod.Current?.PowerStatus ?? "";
        [SettingsUISection("Status", "Live")]
        public string FullLoadStatus => Mod.Current?.FullLoadStatus ?? "";
        internal void LoadPriceDraftIfNeeded() { if (!_draftReady) LoadPriceDraft(); }
        internal void LoadPriceDraft()
        {
            if (Mod.Current?.Prices == null) return;
            var quote = Mod.Current.Prices.Get(_priceResource);
            OverrideImport = quote.ImportEnabled; OverrideExport = quote.ExportEnabled;
            ImportPrice = quote.Import; ExportPrice = quote.Export; _draftReady = true;
        }
        [SettingsUISection("Status", "Live")]
        public string Status => Mod.Current?.Status ?? Mod.Text("等待加载", "Waiting");
        [SettingsUISection("Status", "Live")]
        public string ExportStatus => Mod.Current?.ExportStatus ?? "0";
        [SettingsUISection("Status", "Live")]
        [SettingsUIButton]
        public bool Rescan { set { Mod.Current?.Apply(); } }

        public MasterSettings(IMod mod) : base(mod) { SetDefaults(); }
        public override void SetDefaults()
        {
            TruckEnabled = FleetEnabled = StorageEnabled = ProductionEnabled = ContinuousExports = ScaleInputs = KeepInputReserve = true;
            PricesEnabled = ForceFullLoading = FixPowerDisplay = true;
            ImportPriceMultiplier = ExportPriceMultiplier = 1f;
            DirectFactoryImports = DirectFactoryExports = false;
            LocalFactoryTradeEnabled = false;
            FactoryIncomePerDay = CommercialIncomePerDay = 0;
            InventoryRefillMode=InventoryMode.Off;RefillShops=RefillFactories=true;
            TruckTonnes = 100;
            Farming = Forestry = Mining = Oil = Fishing = Manufacturing = Office = 1;
        }
        public override void Apply() { base.Apply(); Mod.Current?.QueueSettingsApply(); }
        internal float Multiplier(Resource output, bool extractor)
        {
            if (EconomyUtils.IsOfficeResource(output)) return Office;
            if (!extractor) return Manufacturing;
            switch (output)
            {
                case Resource.Wood: return Forestry;
                case Resource.Oil: return Oil;
                case Resource.Ore: case Resource.Coal: case Resource.Stone: return Mining;
                case Resource.Fish: return Fishing;
                default: return Farming;
            }
        }

        internal Dictionary<string, string> Labels(bool zh)
        {
            var result = new Dictionary<string, string> { [GetSettingsLocaleID()] = zh ? "产业统合控制" : "Industry Master" };
            string[] tabs = { "Logistics", "Production", "Export", "Prices", "LocalEconomy", "Inventory", "Status" };
            string[] tabNames = zh ? new[] { "运输与仓储", "产量调节", "进出口", "进出口价格", "本地经济", "库存补充", "运行状态" } : new[] { "Logistics", "Production", "Import / export", "Trade prices", "Local economy", "Inventory", "Status" };
            for (int i = 0; i < tabs.Length; i++) result[GetOptionTabLocaleID(tabs[i])] = tabNames[i];
            string[] groups = { "Truck", "Storage", "Yields", "Trading", "PriceEditor", "Live" };
            string[] groupNames = zh ? new[] { "货车", "工厂仓储", "产量倍率", "持续外销", "资源结算价格", "检查结果" } : new[] { "Trucks", "Factory storage", "Production multipliers", "Continuous trading", "Resource settlement prices", "Diagnostics" };
            for (int i = 0; i < groups.Length; i++) result[GetOptionGroupLocaleID(groups[i])] = groupNames[i];
            result[GetOptionGroupLocaleID("DirectTrade")] = zh ? "工厂虚空进出口" : "Direct factory trade";
            result[GetOptionGroupLocaleID("PriceMultipliers")] = zh ? "全局进出口价格倍率" : "Global trade price multipliers";
            result[GetOptionGroupLocaleID("LocalTrade")] = zh ? "本地虚空运货" : "Direct local freight";
            result[GetOptionGroupLocaleID("VirtualIncome")] = zh ? "企业虚空收入" : "Virtual company income";
            result[GetOptionGroupLocaleID("CompanyRecovery")] = zh ? "空置与零收入建筑" : "Vacancies and zero income";
            result[GetOptionGroupLocaleID("VirtualJobs")] = zh ? "虚空岗位" : "Virtual jobs";
            Label(result, "LocalFactoryTradeEnabled", zh ? "本地工厂向商铺直接交货" : "Direct factory deliveries to shops", zh ? "按原版匹配到本地供货订单后直接扣货、入库和结算，省去发车。原版匹配仍需要可达路线。" : "After native matching, transfer stock and settle the order without dispatching a truck. Native matching still requires a reachable route.");
            Label(result, "LocalTradeStatus", zh ? "本次进档的本地交货" : "Local deliveries since loading the city", "");
            Label(result, "FactoryIncomePerDay", zh ? "每家工厂每日收入" : "Daily income per factory", zh ? "每个游戏日向在营的加工和采集企业发放一次，直接计入企业现金。设为 0 停止发放。" : "Credit each operating factory and extractor once per simulation day. Set to 0 to stop payments.");
            Label(result, "CommercialIncomePerDay", zh ? "每家商家每日收入" : "Daily income per business", zh ? "每个游戏日向在营的商业企业发放一次，直接计入企业现金。设为 0 停止发放。" : "Credit each operating commercial business once per simulation day. Set to 0 to stop payments.");
            Label(result, "CompanyIncomeStatus", zh ? "本游戏日发放" : "Payments today", "");
            Label(result, "RecoverCompanies", zh ? "一键入驻并补发收入" : "Fill vacancies and pay support",
                zh ? "按原版规则安排企业入驻符合条件的空置房产。按日收入设置给经营收入为 0 的工厂和商家补发，当天已到账的企业跳过。入驻在恢复模拟后推进。"
                   : "Fill eligible vacant business properties through the native move-in process. Pay the configured grant to factories and shops with zero operating income that have yet to receive it today. Move-ins progress while simulation runs.");
            Label(result, "CompanyRecoveryStatus", zh ? "操作结果" : "Action result", "");
            Label(result, "AssignVirtualJobs", zh ? "一键安排虚空岗位" : "Assign virtual jobs", zh ? "给当前符合条件的失业居民安排免通勤岗位。" : "Assign jobs without commuting to currently eligible unemployed residents.");
            Label(result, "ReleaseVirtualJobs", zh ? "撤销虚空岗位" : "Release virtual jobs", zh ? "释放本模组安排的岗位，让这些居民重新求职。" : "Release jobs assigned by this mod so those residents can seek work again.");
            Label(result, "VirtualJobsStatus", zh ? "岗位操作结果" : "Job assignment result", "");
            Label(result, "TruckEnabled", zh ? "启用货车载重调节" : "Override truck capacity", "");
            Label(result, "ForceFullLoading", zh ? "强制满载（缺货自动生成）" : "Force full loads (create missing goods)", zh ? "已有运输任务的载货单元在装货阶段补满，缺少部分凭空生成。分批卸货后不反复补货；建筑升级材料订单数量保持原样。关闭后已生成的货物保留。" : "Fill cargo units at loading, creating any missing goods. Do not refill after partial deliveries. Building upgrade order amounts remain unchanged; generated goods persist when disabled.");
            result[GetOptionGroupLocaleID("Refill")]=zh?"自动补货":"Automatic refill";
            Label(result,"InventoryRefillMode",zh?"补货模式":"Refill mode",zh?"按现实时间计时，暂停时停止。原料和成品共用仓储容量，按涉及的实物种类均分目标；保留已有超额库存。补满模式每0.1秒检查，10%模式每秒增加各自容量份额的10%，到目标停止。":"Real-time timing while simulation runs. Share total physical storage among recipe goods. Full checks every 0.1s; 10% adds 10% of each capacity share per second, capped at its target.");
            Label(result,"RefillShops",zh?"补充商店商品和原料":"Refill commercial goods and inputs","");
            Label(result,"RefillFactories",zh?"补充工厂原料和成品":"Refill industrial inputs and output",zh?"开启虚空进口时暂停对应原料的自动补货。开启虚空出口时暂停对应成品的自动补货。":"Direct imports suspend auto-refill for eligible factory inputs. Direct exports suspend auto-refill for eligible factory output.");
            Label(result,"InventoryStatus",zh?"补货统计":"Refill statistics",zh?"额外库存直接生成，不计作生产收入。关闭后已生成库存保留。":"Stock is created directly, without recording production income. Existing additions remain when disabled.");
            result[GetEnumValueLocaleID(InventoryMode.Off)]=zh?"关闭":"Off";
            result[GetEnumValueLocaleID(InventoryMode.Full)]=zh?"持续自动补满":"Keep full";
            result[GetEnumValueLocaleID(InventoryMode.TenPercentPerSecond)]=zh?"每秒补充容量的10%":"Add 10% capacity per second";
            Label(result, "TruckTonnes", zh ? "载重（吨 / 载货单元）" : "Tonnes per cargo unit", zh ? "最低100吨，最高1000吨。开启强制满载时缺货自动补足，关闭时按实际订单和库存装货。" : "100–1000 tonnes. Forced loading creates shortages; when disabled, loads depend on stock and orders.");
            Label(result, "FleetEnabled", zh ? "公司运输名额至少50辆" : "At least 50 company vehicle slots", zh ? "保留原本更高的名额。车辆按需派出，可用数量仍受效率影响。" : "Preserve higher original limits. Dispatch and availability follow orders and efficiency.");
            Label(result, "StorageEnabled", zh ? "工厂仓储统一1万吨" : "10,000-tonne factory storage", zh ? "包括采集型专门产业和普通加工工厂。已有超额库存先保留，卖出后容量自动回落。" : "Includes extractors and manufacturing factories. Existing surplus stock is retained until exported, then capacity settles to the target.");
            Label(result, "StorageTarget", zh ? "目标容量" : "Storage target", "");
            Label(result, "ProductionEnabled", zh ? "启用产量调节" : "Override production", zh ? "1倍为资产原始配方；受员工、效率、原料和仓库空间限制。" : "1x uses original asset recipes. Workers, efficiency, inputs and storage still limit production.");
            Label(result, "ScaleInputs", zh ? "原料消耗随产量同比变化" : "Scale input use with output", zh ? "开启时保留原配方比例；关闭时提高单位原料的产出。" : "On: preserve recipe ratios. Off: increase yield per input.");
            string[] props = { "Farming", "Forestry", "Mining", "Oil", "Fishing", "Manufacturing", "Office" };
            string[] names = zh ? new[] { "农业", "林业", "矿业", "石油开采", "渔业", "加工工业", "办公产业" } : new[] { "Farming", "Forestry", "Mining", "Oil extraction", "Fishing", "Manufacturing", "Offices" };
            for (int i = 0; i < props.Length; i++) Label(result, props[i], names[i], zh ? "0.1 到 100 倍，按原始配方计算。" : "0.1x to 100x, applied to the original recipe.");
            Label(result, "ContinuousExports", zh ? "无限出口需求（持续外销）" : "Unlimited export demand (continuous offers)", zh ? "有可售成品就持续提出原生出口请求，不等75%库存，不设本插件的外销总额上限。不是直接加钱或瞬间清空仓库。" : "Offer output continuously, without the 75% stock threshold or a mod-imposed sales quota. No direct money injection or instant stock removal.");
            Label(result, "KeepInputReserve", zh ? "保留成品兼作原料的最低用量" : "Reserve output also used as an input", zh ? "避免循环配方把下一批原料全部卖光。" : "Keep one recipe batch when output is also an input.");
            Label(result, "ExportScope", zh ? "出口方式" : "Export behavior", "");
            Label(result, "DirectFactoryImports", zh ? "工厂虚空进口" : "Direct factory imports", zh ? "工厂新发出的实物原料采购订单从外部市场直接补进仓库，按实际货款结算，无需货车。" : "Settle new physical factory input orders with the external market. Pay for goods and receive stock without dispatching a truck.");
            Label(result, "DirectFactoryExports", zh ? "工厂虚空出口" : "Direct factory exports", zh ? "工厂新发出的实物出口订单直接从库存扣货，货款进公司账户，无需货车。适用于加工和采集产业。" : "Sell existing stock on new physical export orders and credit the company without dispatching a truck. Applies to manufacturers and extractors.");
            Label(result, "DirectTradeStatus", zh ? "本次进档已结算" : "Settled since loading this city", "");
            Label(result, "Status", zh ? "状态" : "Status", "");
            Label(result, "ExportStatus", zh ? "出口请求" : "Export requests", zh ? "这是请求数量，实际交易和运输由游戏处理。" : "Request count, not completed sales; the game handles trading and transport.");
            Label(result, "Rescan", zh ? "重新扫描并应用" : "Rescan and apply", "");
            Label(result, "PricesEnabled", zh ? "启用进出口结算调节" : "Enable import/export price adjustments", zh ? "开启后使用分资源报价和价格倍率。单独收取的运输费按游戏规则计算。" : "Apply resource quotes and price multipliers to outside trade. Separately charged freight follows the game's rules.");
            Label(result, "ImportPriceMultiplier", zh ? "进口价格倍率" : "Import price multiplier", zh ? "已应用的资源报价或原版货价乘以此倍率。0 倍免货款。" : "Multiply applied resource quotes or native goods prices. 0x waives the goods charge.");
            Label(result, "ExportPriceMultiplier", zh ? "出口价格倍率" : "Export price multiplier", zh ? "已应用的资源报价或原版货价乘以此倍率。0 倍无货款收入。" : "Multiply applied resource quotes or native goods prices. A 0x sale pays zero.");
            Label(result, "PriceResource", zh ? "选择资源" : "Resource", "");
            Label(result, "OverrideImport", zh ? "覆盖该资源进口价" : "Override this import price", "");
            Label(result, "OverrideExport", zh ? "覆盖该资源出口价" : "Override this export price", "");
            Label(result, "ImportPrice", zh ? "进口价（实物每吨，虚拟产品每单位）" : "Import price (per tonne / virtual unit)", zh ? "可输入0至100万元。调整后点击应用；单笔结算最多20亿，避免原版整数溢出。" : "0 to 1,000,000. Click apply after editing. A transaction is capped at 2 billion to prevent native integer overflow.");
            Label(result, "ExportPrice", zh ? "出口价（实物每吨，虚拟产品每单位）" : "Export price (per tonne / virtual unit)", "");
            Label(result, "ApplyResourcePrice", zh ? "应用该资源价格" : "Apply this resource's prices", zh ? "保存为该资源独立价格，切换资源后仍保留。不会重新结算已完成的交易。" : "Save independent prices for this resource. Completed transactions are not repriced.");
            Label(result, "ResetResourcePrice", zh ? "该资源恢复原价" : "Restore this resource's native prices", "");
            Label(result, "CurrentPrices", zh ? "当前已应用价格" : "Currently applied prices", "");
            Label(result, "FixPowerDisplay", zh ? "修正超大发电量统计溢出" : "Fix huge production statistics overflow", zh ? "用64位汇总显示电力生产量，不修改电厂发电参数或输电线容量。" : "Display production using a 64-bit sum, without changing power plants or line capacities.");
            Label(result, "PowerStatus", zh ? "电力统计" : "Power statistics", "");
            Label(result, "FullLoadStatus", zh ? "满载记录" : "Full-load records", "");
            string[] resourceZh = { "谷物", "方便食品", "食品", "蔬菜", "餐饮", "木材", "加工木材", "纸张", "家具", "车辆", "住宿", "石油", "石化产品", "矿石", "塑料", "金属", "电子产品", "软件", "煤炭", "石材", "牲畜", "棉花", "钢铁", "矿物", "混凝土", "机械", "化学品", "药品", "饮料", "纺织品", "电信", "金融", "媒体", "娱乐", "休闲", "鱼类" };
            foreach (TradeResource resource in Enum.GetValues(typeof(TradeResource))) result[GetEnumValueLocaleID(resource)] = zh ? resourceZh[(int)resource] : resource.ToString();
            return result;
        }
        private void Label(Dictionary<string, string> dict, string key, string label, string description)
        { dict[GetOptionLabelLocaleID(key)] = label; dict[GetOptionDescLocaleID(key)] = description; }
    }
}
