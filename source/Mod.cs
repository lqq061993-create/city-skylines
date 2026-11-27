using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.PSI.Environment;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Modding;
using Game.Pathfind;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using LoadPurpose = Colossal.Serialization.Entities.Purpose;

[assembly: AssemblyTitle("IndustryMaster")]
[assembly: AssemblyDescription("Industry, tax, freight, and local economy controls")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace IndustryMaster
{
    public sealed class Mod : IMod
    {
        private const string CompatibleMvid = "46198354-d4f9-4d23-bf32-7e3c085d8b92";
        private const string HarmonyId = "local.industrymaster.164";
        private const string VirtualJobHarmonyId = HarmonyId + ".virtual-jobs";
        internal static Mod Current;
        private static ILog Log;
        internal MasterSettings Settings;
        internal TradePrices Prices;
        private FullLoading _fullLoading;
        private ElectricityDisplay _electricity;
        private InventoryRefill _inventory;
        private DirectFactoryTrade _directTrade;
        private bool _directTradeFaulted;
        private LocalFactoryTrade _localTrade;
        private bool _localTradeFaulted;
        private VirtualJobs _virtualJobs;
        private VirtualJobAction _virtualJobAction;
        private string _virtualJobsStatus;
        private bool _virtualJobHooksReady;
        private VacantCompanies _vacantCompanies;
        private bool _companyRecoveryRequested;
        private string _companyRecoveryStatus;
        private enum VirtualJobAction { None, Assign, Release }
        private DateTime _settingsApplyAt, _nextStatusReport;
        private bool _hasPrefabSettings, _reportedDiagnosticsError;
        private (bool TruckEnabled, int Tonnes, bool FleetEnabled, bool StorageEnabled) _logisticsSettings;
        private (bool Enabled, bool ScaleInputs, float Farming, float Forestry, float Mining, float Oil, float Fishing, float Manufacturing, float Office) _productionSettings;
        internal string InventoryStatus => Text("补货 ", "Refill passes ")+(_inventory?.Passes??0)+Text(" 次，增加 ", ", created ")+((_inventory?.GeneratedKg??0)/1000d).ToString("N1")+Text(" 吨", " tonnes");
        internal static bool TradePricesEnabled => Current != null && Current._ready && Current.Settings?.PricesEnabled == true;
        internal static bool FullLoadingActive => Current != null && Current._ready && Current.Settings != null && Current.Settings.ForceFullLoading;
        internal string FullLoadStatus => Text("补满装货 ", "Filled loads ") + (_fullLoading?.FilledLoads ?? 0) + Text(" 次，生成 ", ", generated ") + ((_fullLoading?.GeneratedKg ?? 0) / 1000d).ToString("N1") + Text(" 吨", " tonnes");
        internal string PowerStatus => _electricity?.Description ?? Text("电力统计修正已关闭", "Power statistics fix disabled");
        internal string Status;
        internal string ExportStatus => Text("累计提出 " + _exportRequests + " 个请求", "Requests offered: " + _exportRequests);
        internal bool CityEconomyReady => _ready && _hasPrefabSettings && !_applying && _world.IsCreated
            && !_manager.isGameLoading && _manager.gameMode == GameMode.Game;
        internal string LocalTradeStatus => _localTradeFaulted
            ? Text("本地虚空运货已暂停，请查看模组日志", "Direct local deliveries paused. See the mod log.")
            : Text($"已交货 {_localTrade?.Orders ?? 0:N0} 笔，共 {(_localTrade?.Amount ?? 0) / 1000d:N1} 吨",
                $"Delivered {_localTrade?.Orders ?? 0:N0} orders ({(_localTrade?.Amount ?? 0) / 1000d:N1} t)");
        internal string CompanyIncomeStatus => CityEconomyReady
            ? _world.GetExistingSystemManaged<VirtualCompanyIncomeSystem>()?.Description ?? ""
            : Text("载入城市后显示发放情况", "Load a city to view payments");
        internal string CompanyRecoveryStatus
        {
            get
            {
                if (!CityEconomyReady) return Text("载入城市后可操作", "Load a city to run the action");
                if (_companyRecoveryRequested) return Text("正在安排入驻和补发收入", "Preparing move-ins and payments");
                if (_companyRecoveryStatus == null) return "";
                if (_vacantCompanies == null) return _companyRecoveryStatus;
                if (_vacantCompanies.Failed)
                    return Text($"已入驻 {_vacantCompanies.Filled:N0} 栋，后续入驻出错。", $"Filled {_vacantCompanies.Filled:N0} buildings before an error. ") + _companyRecoveryStatus;

                var progress = _vacantCompanies.IsRunning
                    ? Text($"入驻处理中，已入驻 {_vacantCompanies.Filled:N0} 栋。", $"Move-ins in progress. {_vacantCompanies.Filled:N0} buildings filled. ")
                    : Text($"已入驻 {_vacantCompanies.Filled:N0} 栋，跳过 {_vacantCompanies.Skipped:N0} 栋。", $"Filled {_vacantCompanies.Filled:N0} buildings and skipped {_vacantCompanies.Skipped:N0}. ");
                return progress + _companyRecoveryStatus;
            }
        }
        internal string VirtualJobsStatus => _ready && !_virtualJobHooksReady
            ? Text("虚空岗位初始化失败，请查看模组日志", "Virtual jobs could not initialize. See the mod log.")
            : _virtualJobsStatus ?? (CityEconomyReady
                ? Text("点击按钮为当前失业居民安排岗位", "Click Assign to place eligible unemployed residents")
                : Text("载入城市后可操作", "Load a city to manage virtual jobs"));
        internal bool DirectFactoryImportsActive => _ready && !_directTradeFaulted && Settings.DirectFactoryImports;
        internal bool DirectFactoryExportsActive => _ready && !_directTradeFaulted && Settings.DirectFactoryExports;
        internal string DirectTradeStatus => _directTradeFaulted
            ? Text("虚空进出口已暂停，请查看日志", "Direct factory trade paused. See the mod log.")
            : Text($"进口 {_directTrade?.ImportOrders ?? 0} 笔，{(_directTrade?.ImportAmount ?? 0) / 1000d:N1} 吨。出口 {_directTrade?.ExportOrders ?? 0} 笔，{(_directTrade?.ExportAmount ?? 0) / 1000d:N1} 吨。",
                $"Imported {_directTrade?.ImportOrders ?? 0} orders ({(_directTrade?.ImportAmount ?? 0) / 1000d:N1} t). Exported {_directTrade?.ExportOrders ?? 0} orders ({(_directTrade?.ExportAmount ?? 0) / 1000d:N1} t).");
        private World _world;
        private GameManager _manager;
        private PrefabController _controller;
        private Dictionary<string, MemorySource> _sources;
        private HashSet<MethodBase> _patched;
        private Harmony _harmony, _virtualJobHarmony;
        private FieldInfo _requireCapacityUpdate;
        private EntityQuery _exportQuery;
        private bool _hasQuery, _ready, _applying, _subscribed, _hasExportTick;
        private uint _lastExportFrame, _lastProtectedFrame;
        private bool _hasProtectedFrame;
        private long _exportRequests;
        internal static string Text(string zh, string en) =>
            (GameManager.instance?.localizationManager?.activeLocaleId?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ?? true) ? zh : en;

        public void OnLoad(UpdateSystem updateSystem)
        {
            // IMod loading on this game version bypasses constructors and initializers.
            Current = this;
            Log = LogManager.GetLogger("IndustryMaster").SetShowsErrorsInUI(false);
            _world = updateSystem.World;
            _manager = GameManager.instance;
            _sources = new Dictionary<string, MemorySource>();
            _patched = new HashSet<MethodBase>();
            Status = Text("等待载入城市", "Waiting for a city");
            try
            {
                Settings = new MasterSettings(this);
                AssetDatabase.global.LoadSettings("IndustryMaster", Settings, new MasterSettings(this), false);
                Settings.RegisterInOptionsUI();
                foreach (string locale in _manager.localizationManager.GetSupportedLocales())
                {
                    var source = new MemorySource(Settings.Labels(locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)));
                    _sources.Add(locale, source);
                    _manager.localizationManager.AddSource(locale, source);
                }
                _requireCapacityUpdate = typeof(VehicleCapacitySystem).GetField("m_RequireUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (typeof(UpdateSystem).Assembly.ManifestModule.ModuleVersionId.ToString("D") != CompatibleMvid
                    || _requireCapacityUpdate == null || _requireCapacityUpdate.FieldType != typeof(bool))
                {
                    Status = Text("版本不匹配，修改已停用", "Version mismatch; overrides disabled");
                    Log.Warn(Status);
                    return;
                }
                _controller = new PrefabController(_world);
                Prices = new TradePrices(_world);
                _harmony = new Harmony(HarmonyId);
                TradeHooks.Install(_harmony);
                TaxHooks.Install(_harmony);
                try { CompanyIncomePanel.Install(_harmony); }
                catch (Exception e) { Log.Warn("Company income panel could not initialize: " + e); }
                InstallVirtualJobs();
                Patch(typeof(DeliveryTruckAISystem), "OnUpdate", nameof(BeforeTruckUpdate), true);
                Patch(typeof(ElectricityStatisticsSystem), "OnUpdate", nameof(AfterPowerStatistics), false);
                Patch(typeof(ResourceBuyerSystem), "OnUpdate", nameof(BeforeImportUpdate), true);
                Patch(typeof(ResourceExporterSystem), "OnUpdate", nameof(BeforeExportUpdate), true);
                Patch(typeof(ExtractorCompanySystem), "OnUpdate", nameof(BeforeProduction), true);
                Patch(typeof(ProcessingCompanySystem), "OnUpdate", nameof(BeforeProduction), true);
                PatchCompatibility();
                _manager.onGamePreload += OnPreload;
                _manager.onGameLoadingComplete += OnLoaded;
                _subscribed = true;
                _ready = true;
                updateSystem.UpdateAt<InventorySystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<SettingsApplySystem>(SystemUpdatePhase.MainLoop);
                updateSystem.UpdateAt<VirtualCompanyIncomeSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateBefore<VacantCompanies, PropertyProcessingSystem>(SystemUpdatePhase.GameSimulation);
                Log.Info("IndustryMaster 1.0 loaded for 1.6.4f1 with company move-ins, zero-income support and virtual income in the profit row.");
                if (!_manager.isGameLoading && _manager.gameMode == GameMode.Game)
                {
                    RestoreVirtualJobs();
                    Apply();
                }
            }
            catch (Exception e) { Fail(e); }
        }
        private void InstallVirtualJobs()
        {
            _virtualJobHarmony = new Harmony(VirtualJobHarmonyId);
            try
            {
                VirtualJobHooks.Install(_virtualJobHarmony);
                _virtualJobHooksReady = true;
            }
            catch (Exception e)
            {
                _virtualJobHarmony.UnpatchAll(VirtualJobHarmonyId);
                Log.Warn("Virtual jobs could not initialize: " + e);
            }
        }

        private void Patch(Type type, string name, string handler, bool prefix)
        {
            var method = AccessTools.DeclaredMethod(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            if (!_patched.Add(method)) return;
            var patch = new HarmonyMethod(typeof(Mod), handler);
            _harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }
        private static bool AllowCompanyBalance() => Current == null || !Current._ready || !Current.Settings.ProductionEnabled;
        private static void AfterOtherOverride() { Current?.Apply(); }
        private static void BeforeImportUpdate()
        {
            Current?.SettleLocalTradeOrders();
            Current?.SettleDirectTrade(importing: true);
        }
        private static void BeforeExportUpdate()
        {
            Current?.OfferExports();
            Current?.SettleDirectTrade(importing: false);
        }

        private void SettleDirectTrade(bool importing)
        {
            if (!(importing ? DirectFactoryImportsActive : DirectFactoryExportsActive)
                || _applying || !_world.IsCreated || _manager.isGameLoading || _manager.gameMode != GameMode.Game)
                return;

            try
            {
                _directTrade ??= new DirectFactoryTrade(_world, Prices);
                _directTrade.SettleOrders(importing, Settings.KeepInputReserve);
            }
            catch (Exception e)
            {
                _directTradeFaulted = true;
                Log.Warn("Direct factory trade paused; native trade will handle subsequent orders. " + e);
            }
        }

        private void SettleLocalTradeOrders()
        {
            if (!CityEconomyReady || !Settings.LocalFactoryTradeEnabled || _localTradeFaulted) return;
            try
            {
                _localTrade ??= new LocalFactoryTrade(_world);
                _localTrade.SettleOrders();
            }
            catch (Exception e)
            {
                _localTradeFaulted = true;
                Log.Warn("Direct local deliveries paused: " + e);
            }
        }

        private void DisposeDirectTrade()
        {
            _directTrade?.Dispose();
            _directTrade = null;
            _directTradeFaulted = false;
        }

        private void DisposeLocalEconomy()
        {
            _companyRecoveryRequested = false;
            _companyRecoveryStatus = null;
            try { _vacantCompanies?.Cancel(); }
            catch (Exception e) { Log?.Warn("Company recovery cleanup: " + e); }
            finally { _vacantCompanies = null; }
            _virtualJobAction = VirtualJobAction.None;
            _virtualJobsStatus = null;
            _localTradeFaulted = false;
            try { _localTrade?.Dispose(); }
            catch (Exception e) { Log?.Warn("Local deliveries cleanup: " + e); }
            finally { _localTrade = null; }
            try { _virtualJobs?.Dispose(); }
            catch (Exception e) { Log?.Warn("Virtual jobs cleanup: " + e); }
            finally { _virtualJobs = null; }
        }
        private static void BeforeProduction() { Current?.ProtectStorage(); }
        private static void BeforeTruckUpdate() { if (FullLoadingActive) Current.AfterDeliveryActions(); }
        private static void AfterPowerStatistics()
        {
            if (Current == null || !Current._ready || Current._manager.gameMode != GameMode.Game || Current._manager.isGameLoading) return;
            try
            {
                Current._electricity?.Update();
                // This system updates even when no ResourceExporter exists. The native
                // exporter itself requires an existing order, so its prefix alone cannot
                // create the first order after all outstanding orders are gone.
                Current.OfferExports();
                if (DateTime.UtcNow >= Current._nextStatusReport)
                    Current.ReportStatus();
            }
            catch (Exception e) { Current.Fail(e); }
        }
        internal void BeforeDeliveryActions<T>(T job) where T : struct
        { try { _fullLoading?.BeforeActions(job); } catch (Exception e) { Fail(e); } }
        internal void AfterDeliveryActions()
        { try { if (FullLoadingActive) _fullLoading?.FinishActions(); } catch (Exception e) { Fail(e); } }
        internal void SaveSelectedPrice(MasterSettings editor)
        {
            if (Prices == null) return;
            try
            {
                Prices.Set(editor.PriceResource, new PriceQuote { ImportEnabled = editor.OverrideImport, ExportEnabled = editor.OverrideExport, Import = editor.ImportPrice, Export = editor.ExportPrice });
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Warn("Trade quote was not saved: " + e.Message);
                return;
            }
            editor.LoadPriceDraft();
            ReportStatus();
            Log.Info("Trade quote saved for " + editor.PriceResource + ": " + Prices.Describe(editor.PriceResource));
        }
        private void ProtectStorage()
        {
            if (!_ready || _applying || !_hasQuery || !Settings.StorageEnabled || _manager.isGameLoading || _manager.gameMode != GameMode.Game) return;
            uint frame = _world.GetExistingSystemManaged<SimulationSystem>().frameIndex;
            if (_hasProtectedFrame && frame == _lastProtectedFrame) return;
            _hasProtectedFrame = true; _lastProtectedFrame = frame;
            try
            {
                // Old inbound orders may deliver stock after loading. Guard before each
                // production tick as well, so the native min(freeSpace, output) never
                // becomes negative because this mod lowered the capacity.
                _controller.ProtectStorage();
            }
            catch (Exception e) { Fail(e); }
        }
        private void PatchCompatibility()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string name in new[] { "ChangeCompany.OverrideStorageSystem", "ChangeCompany.OverrideVehiclesSystem" })
                {
                    var type = assembly.GetType(name, false);
                    if (type != null) Patch(type, "OnUpdate", nameof(AfterOtherOverride), false);
                }
                var balance = assembly.GetType("ChangeCompany.ProductionBalanceSystem", false);
                if (balance != null) Patch(balance, "OnUpdate", nameof(AllowCompanyBalance), true);
                if (assembly.GetName().Name != "产能大师") continue;
                // The legacy plugin exposes a master switch. Do not Harmony-patch its
                // arbitrary obfuscated simulation/UI methods: some have exception
                // handlers that cannot be rewritten safely by the installed Harmony.
                var config = assembly.GetType("A.B", false);
                var enabled = config?.GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public);
                if (enabled == null || !enabled.CanWrite || enabled.PropertyType != typeof(bool))
                    throw new InvalidOperationException("Production Master master switch unavailable; disable it in the active playset.");
                if ((bool)enabled.GetValue(null)) enabled.SetValue(null, false);
                Log.Warn("Legacy Production Master loaded; its master switch is disabled. Disable the duplicate mod in the playset.");
            }
        }
        private void OnPreload(LoadPurpose purpose, GameMode mode)
        {
            DisposeLocalEconomy();
            _inventory?.Dispose();_inventory=null;
            try { DisposeDirectTrade(); _controller?.RestoreAll(); DisposeQuery(); _fullLoading?.Dispose(); _fullLoading = null; _electricity?.Dispose(); _electricity = null; } catch (Exception e) { Log.Warn("Preload cleanup: " + e); }
            _hasExportTick = _hasProtectedFrame = false;
            _hasPrefabSettings = false;
            _settingsApplyAt = _nextStatusReport = default;
            Status = Text("等待载入城市", "Waiting for a city");
        }
        private void OnLoaded(LoadPurpose purpose, GameMode mode)
        {
            if (!_ready || mode != GameMode.Game) return;
            RestoreVirtualJobs();
            try { PatchCompatibility(); Apply(true); } catch (Exception e) { Fail(e); }
        }
        private void RestoreVirtualJobs()
        {
            if (!_virtualJobHooksReady) return;
            try { VirtualJobs.RestoreSavedJobs(_world); }
            catch (Exception e) { Log.Warn("Restoring saved virtual jobs: " + e); }
        }
        internal void QueueSettingsApply()
        {
            if (_ready) _settingsApplyAt = DateTime.UtcNow.AddMilliseconds(180);
        }

        internal void QueueVirtualJobs(bool assign)
        {
            if (!_virtualJobHooksReady || !CityEconomyReady)
            {
                _virtualJobsStatus = Text("载入城市后可操作", "Load a city to manage virtual jobs");
                return;
            }
            _virtualJobAction = assign ? VirtualJobAction.Assign : VirtualJobAction.Release;
            _virtualJobsStatus = Text("正在处理虚空岗位", "Updating virtual jobs");
        }

        internal void QueueCompanyRecovery()
        {
            if (CityEconomyReady && _vacantCompanies?.IsRunning != true)
                _companyRecoveryRequested = true;
        }

        internal void ApplyQueuedSettings()
        {
            if (_settingsApplyAt != default && DateTime.UtcNow >= _settingsApplyAt && !_manager.isGameLoading)
            {
                _settingsApplyAt = default;
                Apply(forcePrefabs: false);
            }
            if (_virtualJobAction != VirtualJobAction.None && CityEconomyReady)
                RunVirtualJobAction();
            if (_companyRecoveryRequested && CityEconomyReady)
                RunCompanyRecovery();
        }

        private void RunCompanyRecovery()
        {
            _companyRecoveryRequested = false;
            try
            {
                var income = _world.GetExistingSystemManaged<VirtualCompanyIncomeSystem>();
                var payment = income?.PayZeroIncome(Settings) ?? (Companies: 0, Amount: 0L);
                _companyRecoveryStatus = Text($"补发 {payment.Companies:N0} 家，共 {payment.Amount:N0} 元。",
                    $"Paid {payment.Amount:N0} to {payment.Companies:N0} companies. ");
            }
            catch (Exception e)
            {
                _companyRecoveryStatus = Text("收入补发出错，请查看模组日志。", "Payment failed. See the mod log. ");
                Log.Warn("Zero-income payment: " + e);
            }
            try
            {
                _vacantCompanies = _world.GetExistingSystemManaged<VacantCompanies>()
                    ?? throw new InvalidOperationException("Missing company move-in system");
                _vacantCompanies.Start();
            }
            catch (Exception e)
            {
                _companyRecoveryStatus += Text("入驻启动出错，请查看模组日志。", "Move-ins could not start. See the mod log.");
                Log.Warn("Starting company move-ins: " + e);
            }
        }

        private void RunVirtualJobAction()
        {
            bool assign = _virtualJobAction == VirtualJobAction.Assign;
            _virtualJobAction = VirtualJobAction.None;
            try
            {
                _virtualJobs ??= new VirtualJobs(_world);
                int count = assign ? _virtualJobs.Assign() : _virtualJobs.Release();
                _virtualJobsStatus = assign
                    ? Text($"为 {count:N0} 名失业居民安排了岗位", $"Assigned virtual jobs to {count:N0} residents")
                    : Text($"已撤销 {count:N0} 个虚空岗位", $"Released {count:N0} virtual jobs");
            }
            catch (Exception e)
            {
                _virtualJobsStatus = Text("岗位操作出错，请查看模组日志", "Virtual job action failed. See the mod log.");
                Log.Warn("Virtual job action: " + e);
            }
        }

        internal void Apply(bool loadingComplete = false, bool forcePrefabs = true)
        {
            if (!_ready || _applying || !_world.IsCreated || _manager.gameMode != GameMode.Game || (_manager.isGameLoading && !loadingComplete)) return;
            _applying = true;
            try
            {
                var logistics = (Settings.TruckEnabled, Settings.TruckTonnes, Settings.FleetEnabled, Settings.StorageEnabled);
                var production = (Settings.ProductionEnabled, Settings.ScaleInputs, Settings.Farming, Settings.Forestry,
                    Settings.Mining, Settings.Oil, Settings.Fishing, Settings.Manufacturing, Settings.Office);
                bool prefabsChanged = forcePrefabs || !_hasPrefabSettings || logistics != _logisticsSettings || production != _productionSettings;
                if (prefabsChanged)
                {
                    _controller.Apply(Settings);
                    _logisticsSettings = logistics;
                    _productionSettings = production;
                    _hasProtectedFrame = false;
                }
                if (forcePrefabs || !_hasPrefabSettings) Prices.Refresh();
                _hasPrefabSettings = true;
                _fullLoading ??= new FullLoading(_world);
                _inventory ??= new InventoryRefill(_world);
                if (Settings.FixPowerDisplay)
                {
                    if (_electricity == null) _electricity = new ElectricityDisplay(_world);
                    _electricity.Attach();
                }
                else { _electricity?.Dispose(); _electricity = null; }
                Settings.LoadPriceDraftIfNeeded();
                if (!_hasQuery)
                {
                    _exportQuery = _world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Resources>(), ComponentType.ReadOnly<TripNeeded>(),
                            ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadOnly<Game.Agents.TaxPayer>() },
                        Any = new[] { ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(), ComponentType.ReadOnly<Game.Companies.ExtractorCompany>() },
                        None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
                    });
                    _hasQuery = true;
                }
                Status = Text("运行正常（工厂/车队/车型 " + _controller.Counts + "）", "Ready (factories/fleets/trucks " + _controller.Counts + ")");
                if (_controller.HasOverstock) Status += Text("；已有超额库存保留中", "; preserving existing excess stock");
                ReportStatus(auditPrefabs: prefabsChanged);
                if (prefabsChanged)
                    Log.Info("Applied. " + Status + " | truck=" + Settings.TruckTonnes + "t | continuous exports=" + Settings.ContinuousExports
                        + " | direct factory imports=" + DirectFactoryImportsActive + " | direct factory exports=" + DirectFactoryExportsActive);
            }
            catch (Exception e) { Fail(e); }
            finally { _applying = false; }
        }
        internal void RefreshDispatch()
        {
            _world.EntityManager.CompleteAllTrackedJobs();
            var system = _world.GetExistingSystemManaged<VehicleCapacitySystem>();
            if (system == null) throw new InvalidOperationException("Native vehicle capacity system missing");
            _requireCapacityUpdate.SetValue(system, true);
            system.Update();
            system.GetDeliveryTruckSelectData();
        }

        private void OfferExports()
        {
            if (!_ready || !_hasQuery || _applying || _manager.gameMode != GameMode.Game || _manager.isGameLoading) return;
            if (!Settings.ContinuousExports && !_controller.HasOverstock) return;
            var simulation = _world.GetExistingSystemManaged<SimulationSystem>();
            uint frame = simulation.frameIndex;
            if (_hasExportTick && unchecked(frame - _lastExportFrame) < 256) return;
            _hasExportTick = true; _lastExportFrame = frame;
            try
            {
                // Component reads wait on their own writers. Adding an export order
                // is an ECS structural change and synchronizes when it actually occurs.
                var em = _world.EntityManager;
                if (_controller.HasOverstock) ProtectStorage();
                if (Settings.ContinuousExports)
                {
                    var selection = _world.GetExistingSystemManaged<VehicleCapacitySystem>().GetDeliveryTruckSelectData();
                    using (var entities = _exportQuery.ToEntityArray(Allocator.Temp))
                        foreach (Entity company in entities)
                        {
                            if (em.HasComponent<ResourceExporter>(company) || em.HasComponent<ResourceBuyer>(company)
                                || em.HasComponent<PathInformation>(company)) continue;
                            Entity prefab = em.GetComponentData<PrefabRef>(company).m_Prefab;
                            if (!_controller.IsProducer(prefab)) continue;
                            bool exporting = false;
                            foreach (var trip in em.GetBuffer<TripNeeded>(company, true))
                                if (trip.m_Purpose == Game.Citizens.Purpose.Exporting) { exporting = true; break; }
                            if (exporting) continue;
                            var process = em.GetComponentData<IndustrialProcessData>(prefab);
                            Resource output = process.m_Output.m_Resource;
                            int stock = EconomyUtils.GetResources(output, em.GetBuffer<Resources>(company, true));
                            int reserve = 0;
                            if (Settings.KeepInputReserve)
                            {
                                if (process.m_Input1.m_Resource == output) reserve += process.m_Input1.m_Amount;
                                if (process.m_Input2.m_Resource == output) reserve += process.m_Input2.m_Amount;
                            }
                            int min, max;
                            if (EconomyUtils.IsOfficeResource(output)) max = 1000000;
                            else selection.GetCapacityRange(output, out min, out max);
                            int amount = Policy.ExportAmount(stock, reserve, max);
                            if (amount <= 0) continue;
                            // No invented inventory, direct payment, max-int demand or changes
                            // to construction orders. Native outside connections have no hard
                            // per-resource sales quota; native pathfinding and trading continue.
                            em.AddComponentData(company, new ResourceExporter { m_Resource = output, m_Amount = amount });
                            _exportRequests++;
                        }
                }
            }
            catch (Exception e) { Fail(e); }
        }
        private void ReportStatus(bool auditPrefabs = false)
        {
            _nextStatusReport = DateTime.UtcNow.AddSeconds(5);
            if (_controller == null) return;
            try
            {
                if (auditPrefabs) _controller.Audit();
                WriteStatus();
                _reportedDiagnosticsError = false;
            }
            catch (Exception e)
            {
                if (!_reportedDiagnosticsError) Log.Warn("Industry diagnostics: " + e.Message);
                _reportedDiagnosticsError = true;
            }
        }
        private void WriteStatus()
        {
            string dir = Path.Combine(EnvPath.kUserDataPath, "ModsData", "IndustryMaster");
            Directory.CreateDirectory(dir);
            var income = _world.GetExistingSystemManaged<VirtualCompanyIncomeSystem>();
            File.WriteAllText(Path.Combine(dir, "status.txt"), DateTimeOffset.Now.ToString("o") + "\n" + Status + "\nTruckTonnes=" + Settings.TruckTonnes
                + "\nStorageTargetKg=" + Policy.StorageKg + "\nProductionEnabled=" + Settings.ProductionEnabled + "\nContinuousExports=" + Settings.ContinuousExports
                + "\nExportRequests=" + _exportRequests + "\nOverstockProtected=" + _controller.HasOverstock
                + "\nMinimumTaxRate=" + TaxRange.Minimum + "\nMaximumTaxRate=" + TaxRange.Maximum
                + "\nFactoryIncomePerDay=" + Settings.FactoryIncomePerDay + "\nCommercialIncomePerDay=" + Settings.CommercialIncomePerDay
                + "\nLastPaidFactories=" + (income?.LastFactories ?? 0) + "\nLastPaidShops=" + (income?.LastShops ?? 0)
                + "\nLastCompanyIncome=" + (income?.LastIncome ?? 0) + "\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir,"inventory-status.txt"),DateTimeOffset.Now.ToString("o")+"\nMode="+Settings.InventoryRefillMode+"\nShops="+Settings.RefillShops+"\nFactories="+Settings.RefillFactories+"\n"+InventoryStatus+"\n",Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "trade-and-power-status.txt"), DateTimeOffset.Now.ToString("o") + "\n" + FullLoadStatus
                + "\nTradePricesEnabled=" + Settings.PricesEnabled + "\nImportPriceMultiplier=" + Settings.PriceMultiplier(true) + "\nExportPriceMultiplier=" + Settings.PriceMultiplier(false)
                + "\nImportPriceQuotes=" + (Prices?.ImportQuotes ?? 0) + "\nExportPriceQuotes=" + (Prices?.ExportQuotes ?? 0)
                + "\nDirectFactoryImports=" + DirectFactoryImportsActive + "\nDirectFactoryExports=" + DirectFactoryExportsActive
                + "\nDirectImportOrders=" + (_directTrade?.ImportOrders ?? 0) + "\nDirectImportKg=" + (_directTrade?.ImportAmount ?? 0)
                + "\nDirectExportOrders=" + (_directTrade?.ExportOrders ?? 0) + "\nDirectExportKg=" + (_directTrade?.ExportAmount ?? 0)
                + "\nLocalFactoryTradeEnabled=" + Settings.LocalFactoryTradeEnabled
                + "\nLocalDeliveryOrders=" + (_localTrade?.Orders ?? 0) + "\nLocalDeliveryKg=" + (_localTrade?.Amount ?? 0)
                + "\nPowerSumRaw64=" + (_electricity?.TotalRaw ?? 0) + "\nNativePowerRaw32=" + (_electricity?.NativeWrapped ?? 0)
                + "\nNegativePowerProducers=" + (_electricity?.NegativeProducers ?? 0) + "\n" + PowerStatus + "\n", Encoding.UTF8);
        }
        private void Fail(Exception e)
        {
            _ready = false;
            try { _controller?.RestoreAll(); } catch (Exception restore) { Log?.Warn("Restore: " + restore); }
            Status = Text("修改已安全停用，请查看日志", "Overrides disabled; see log");
            Log?.Warn("Stopped: " + e);
        }
        private void DisposeQuery() { if (_hasQuery && _world.IsCreated) _exportQuery.Dispose(); _hasQuery = false; }
        internal void TickInventory()
        {
            if(!_ready||_manager.isGameLoading||_manager.gameMode!=GameMode.Game||Settings.InventoryRefillMode==InventoryMode.Off)return;
            try{_inventory?.Tick(Settings);}
            catch(Exception e){Settings.InventoryRefillMode=InventoryMode.Off;Log.Warn("Inventory refill disabled: "+e);}
        }
        public void OnDispose()
        {
            _ready = false;
            if (_subscribed && _manager != null) { _manager.onGamePreload -= OnPreload; _manager.onGameLoadingComplete -= OnLoaded; }
            DisposeLocalEconomy();
            try { DisposeDirectTrade(); _controller?.RestoreAll(); DisposeQuery(); _fullLoading?.Dispose(); _electricity?.Dispose(); } catch (Exception e) { Log?.Warn("Dispose: " + e); }
            _controller?.Dispose();
            _virtualJobHarmony?.UnpatchAll(VirtualJobHarmonyId);
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                try { TaxHooks.ResetCache(_world); }
                catch (Exception e) { Log?.Warn("Tax cache reset: " + e.Message); }
            }
            _inventory?.Dispose();_inventory=null;
            Settings?.UnregisterInOptionsUI();
            if (_manager?.localizationManager != null && _sources != null)
                foreach (var pair in _sources) try { _manager.localizationManager.RemoveSource(pair.Key, pair.Value); } catch { }
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }

    public sealed class SettingsApplySystem : GameSystemBase
    {
        protected override void OnUpdate() => Mod.Current?.ApplyQueuedSettings();
    }
}
