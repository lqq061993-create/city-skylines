using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;

namespace IndustryMaster
{
    public sealed class VirtualCompanyIncomeSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        internal const int MaximumDailyIncome = 1_000_000;
        private EntityQuery _companies, _time;
        private SimulationSystem _simulation;
        private int? _lastDay, _paymentDay;
        private bool _faulted;
        private readonly Dictionary<Entity, (int Amount, bool Shop)> _latestPayments = new Dictionary<Entity, (int, bool)>();

        internal int LastFactories { get; private set; }
        internal int LastShops { get; private set; }
        internal long LastIncome { get; private set; }
        internal int LatestPayment(Entity company) => _latestPayments.TryGetValue(company, out var receipt) ? receipt.Amount : 0;

        internal string Description => _faulted
            ? Mod.Text("发放已暂停，请查看模组日志", "Payments paused. See the mod log.")
            : Mod.Text($"本游戏日已向 {LastFactories:N0} 家工厂和 {LastShops:N0} 家商家发放，共 {LastIncome:N0} 元",
                $"Paid {LastIncome:N0} to {LastFactories:N0} factories and {LastShops:N0} businesses today");

        public override int GetUpdateInterval(SystemUpdatePhase phase) => TimeSystem.kTicksPerDay / 256;

        protected override void OnCreate()
        {
            base.OnCreate();
            _simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            _time = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            _companies = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadWrite<Resources>() },
                Any = new[] { ComponentType.ReadOnly<Game.Companies.CommercialCompany>(),
                    ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(), ComponentType.ReadOnly<Game.Companies.ExtractorCompany>() },
                None = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<Game.Companies.OfficeCompany>(), ComponentType.ReadOnly<Game.Companies.StorageCompany>(),
                    ComponentType.ReadOnly<Game.Objects.OutsideConnection>() }
            });
            RequireForUpdate(_time);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode) => ResetIncome();

        public void SetDefaults(Context context) => ResetIncome();

        private void ResetIncome()
        {
            _lastDay = null;
            _faulted = false;
            ClearPayments(null);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            if (mode == GameMode.Game && !_time.IsEmptyIgnoreFilter)
            {
                _lastDay = CurrentDay();
                if (_paymentDay != _lastDay) ClearPayments(_lastDay);
            }
        }

        private int CurrentDay() => TimeSystem.GetDay(_simulation.frameIndex, _time.GetSingleton<TimeData>());

        protected override void OnUpdate()
        {
            var mod = Mod.Current;
            if (_faulted || mod?.CityEconomyReady != true) return;

            int day = CurrentDay();
            // Start following the clock on load so reopening a save never repeats the current day's payment.
            _lastDay ??= day;
            if (day <= _lastDay.Value) return;
            _lastDay = day;

            try { PayForDay(day, mod.Settings, onlyZeroIncome: false); }
            catch (Exception e)
            {
                _faulted = true;
                LogManager.GetLogger("IndustryMaster").Warn("Virtual company income paused: " + e);
            }
        }

        internal (int Companies, long Amount) PayZeroIncome(MasterSettings settings) => _time.IsEmptyIgnoreFilter
            ? default : PayForDay(CurrentDay(), settings, onlyZeroIncome: true);

        private (int Companies, long Amount) PayForDay(int day, MasterSettings settings, bool onlyZeroIncome)
        {
            bool receiptsCleared = _paymentDay != day && _latestPayments.Count != 0;
            if (_paymentDay != day) ClearPayments(day);
            int factoryRate = Math.Max(0, Math.Min(MaximumDailyIncome, settings.FactoryIncomePerDay));
            int shopRate = Math.Max(0, Math.Min(MaximumDailyIncome, settings.CommercialIncomePerDay));
            long previousIncome = LastIncome;
            int previousCount = _latestPayments.Count;
            try
            {
                if (factoryRate != 0 || shopRate != 0) PayCompanies(factoryRate, shopRate, onlyZeroIncome);
                return (_latestPayments.Count - previousCount, LastIncome - previousIncome);
            }
            finally
            {
                if (receiptsCleared || _latestPayments.Count != previousCount)
                    World.GetExistingSystemManaged<SelectedInfoUISystem>()?.RequestUpdate();
            }
        }

        private void ClearPayments(int? day)
        {
            _paymentDay = day;
            _latestPayments.Clear();
            LastFactories = LastShops = 0;
            LastIncome = 0;
        }

        private void PayCompanies(int factoryRate, int shopRate, bool onlyZeroIncome)
        {
            if (_companies.IsEmptyIgnoreFilter) return;

            var em = EntityManager;
            em.CompleteDependencyBeforeRW<Resources>();
            using var companies = _companies.ToEntityArray(Allocator.Temp);
            foreach (Entity company in companies)
            {
                if (_latestPayments.ContainsKey(company)) continue;
                if (onlyZeroIncome && (!em.TryGetComponent<CompanyStatisticData>(company, out var statistics) || statistics.m_Income != 0)) continue;

                bool shop = em.HasComponent<Game.Companies.CommercialCompany>(company);
                int rate = shop ? shopRate : factoryRate;
                if (rate == 0) continue;

                Entity property = em.GetComponentData<PropertyRenter>(company).m_Property;
                if (!em.Exists(property) || em.HasComponent<Deleted>(property) || em.HasComponent<Destroyed>(property)
                    || em.HasComponent<Abandoned>(property) || em.HasComponent<Condemned>(property)
                    || em.HasComponent<Game.Tools.Temp>(property) || em.HasComponent<Game.Objects.OutsideConnection>(property)) continue;
                if (em.HasComponent<Building>(property)
                    && BuildingUtils.CheckOption(em.GetComponentData<Building>(property), BuildingOption.Inactive)) continue;

                var account = em.GetBuffer<Resources>(company);
                int before = EconomyUtils.GetResources(Resource.Money, account);
                int payment = DirectTradePolicy.AddSaturated(before, rate) - before;
                if (payment == 0) continue;
                int received = EconomyUtils.AddResources(Resource.Money, payment, account) - before;
                if (received <= 0) continue;
                RecordPayment(company, received, shop);
            }
        }

        private void RecordPayment(Entity company, int amount, bool shop)
        {
            if (!_latestPayments.TryAdd(company, (amount, shop))) return;
            if (shop) LastShops++;
            else LastFactories++;
            LastIncome += amount;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(_paymentDay ?? int.MinValue);
            writer.Write(_latestPayments.Count);
            foreach (var record in _latestPayments)
            {
                writer.Write(record.Key);
                writer.Write(record.Value.Amount);
                writer.Write(record.Value.Shop);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int day);
            reader.Read(out int count);
            ClearPayments(day == int.MinValue ? (int?)null : day);
            for (int i = 0; i < count; i++)
            {
                reader.Read(out Entity company);
                reader.Read(out int amount);
                reader.Read(out bool shop);
                if (company != Entity.Null && amount > 0 && amount <= MaximumDailyIncome)
                    RecordPayment(company, amount, shop);
            }
        }
    }
}
