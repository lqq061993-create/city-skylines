using System;
using System.Collections.Generic;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace IndustryMaster
{
    internal sealed class DirectFactoryTrade : IDisposable
    {
        private const SetupTargetFlags FactoryImportFlags = SetupTargetFlags.Industrial | SetupTargetFlags.Import;
        private const CityProductionStatisticSystem.CityResourceUsage.Consumer ImportExport =
            CityProductionStatisticSystem.CityResourceUsage.Consumer.ImportExport;

        private readonly World _world;
        private readonly ResourceSystem _resources;
        private readonly CityStatisticsSystem _statistics;
        private readonly CityProductionStatisticSystem _productionStatistics;
        private readonly TradePrices _prices;
        private readonly EntityQuery _buyers;
        private readonly EntityQuery _exporters;
        private readonly List<Settlement> _settlements = new List<Settlement>();

        internal long ImportOrders { get; private set; }
        internal long ExportOrders { get; private set; }
        internal long ImportAmount { get; private set; }
        internal long ExportAmount { get; private set; }

        private readonly struct Settlement
        {
            internal readonly Entity Company;
            internal readonly Resource Resource;
            internal readonly DirectTradeInvoice Invoice;
            internal readonly bool CustomPrice;

            internal Settlement(Entity company, Resource resource, DirectTradeInvoice invoice, bool customPrice)
            {
                Company = company;
                Resource = resource;
                Invoice = invoice;
                CustomPrice = customPrice;
            }
        }

        internal DirectFactoryTrade(World world, TradePrices prices)
        {
            _world = world;
            _prices = prices;
            _resources = world.GetExistingSystemManaged<ResourceSystem>() ?? throw new InvalidOperationException("Resource system unavailable");
            _statistics = world.GetExistingSystemManaged<CityStatisticsSystem>() ?? throw new InvalidOperationException("Statistics system unavailable");
            _productionStatistics = world.GetExistingSystemManaged<CityProductionStatisticSystem>() ?? throw new InvalidOperationException("Resource statistics system unavailable");
            _buyers = CreateQuery(ComponentType.ReadOnly<ResourceBuyer>(), ComponentType.ReadOnly<TravelPurpose>());
            try
            {
                _exporters = CreateQuery(ComponentType.ReadOnly<ResourceExporter>(), ComponentType.ReadOnly<ResourceBuyer>());
            }
            catch
            {
                _buyers.Dispose();
                throw;
            }
        }

        private EntityQuery CreateQuery(ComponentType order, ComponentType excluded)
        {
            return _world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { order, ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadOnly<TaxPayer>(),
                    ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Resources>(), ComponentType.ReadOnly<TripNeeded>() },
                Any = new[] { ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(), ComponentType.ReadOnly<Game.Companies.ExtractorCompany>() },
                None = new[] { excluded, ComponentType.ReadOnly<CommercialCompany>(), ComponentType.ReadOnly<ServiceAvailable>(),
                    ComponentType.ReadOnly<Game.Companies.StorageCompany>(), ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                    ComponentType.ReadOnly<PathInformation>(), ComponentType.ReadOnly<PathElement>(),
                    ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Temp>() }
            });
        }

        internal void SettleOrders(bool importing, bool keepInputReserve)
        {
            EntityQuery query = importing ? _buyers : _exporters;
            if (query.IsEmptyIgnoreFilter)
                return;

            var em = _world.EntityManager;
            var prefabs = _resources.GetPrefabs();
            _settlements.Clear();
            using var companies = query.ToEntityArray(Allocator.Temp);
            using var handled = new NativeList<Entity>(companies.Length, Allocator.Temp);
            foreach (Entity company in companies)
            {
                if (!TryPlan(em, prefabs, company, importing, keepInputReserve, out var settlement))
                    continue;
                handled.Add(company);
                if (settlement.Invoice.Amount > 0)
                    _settlements.Add(settlement);
            }
            if (handled.IsEmpty)
                return;

            NativeQueue<StatisticsEvent> statistics = default;
            NativeArray<int> resourceUsage = default;
            if (_settlements.Count > 0)
            {
                if (!_statistics.Enabled)
                    return;
                statistics = _statistics.GetStatisticsEventQueue(out var statisticsDeps);
                resourceUsage = _productionStatistics.GetCityResourceUsageAccumulator(ImportExport, out var usageDeps);
                if (!statistics.IsCreated || !resourceUsage.IsCreated || resourceUsage.Length < EconomyUtils.ResourceCount)
                    return;
                JobHandle.CombineDependencies(statisticsDeps, usageDeps).Complete();
            }

            // Consume requests before posting any settlement. The native systems can
            // handle all other orders, including any truck or path already in progress.
            em.RemoveComponent(handled.AsArray(), importing ? ComponentType.ReadWrite<ResourceBuyer>() : ComponentType.ReadWrite<ResourceExporter>());
            foreach (var settlement in _settlements)
                Commit(em, statistics, resourceUsage, settlement, importing);
        }

        private bool TryPlan(EntityManager em, ResourcePrefabs resources, Entity company, bool importing,
            bool keepInputReserve, out Settlement settlement)
        {
            settlement = default;
            Entity prefab = em.GetComponentData<PrefabRef>(company).m_Prefab;
            if (!em.HasComponent<IndustrialProcessData>(prefab))
                return false;

            var process = em.GetComponentData<IndustrialProcessData>(prefab);
            if (!TryGetPhysicalResource(em, resources, process.m_Output.m_Resource, out _))
                return false;

            int requested;
            Resource resource;
            if (importing)
            {
                var order = em.GetComponentData<ResourceBuyer>(company);
                resource = order.m_ResourceNeeded;
                requested = order.m_AmountNeeded;
                if (order.m_Payer != company || (order.m_Flags & FactoryImportFlags) != FactoryImportFlags
                    || (resource != process.m_Input1.m_Resource && resource != process.m_Input2.m_Resource)
                    || !em.HasComponent<StorageLimitData>(prefab))
                    return false;
            }
            else
            {
                var order = em.GetComponentData<ResourceExporter>(company);
                resource = order.m_Resource;
                requested = order.m_Amount;
                if (resource != process.m_Output.m_Resource)
                    return false;
                foreach (var trip in em.GetBuffer<TripNeeded>(company, true))
                    if (trip.m_Purpose == Purpose.Exporting)
                        return true;
            }

            if (!TryGetPhysicalResource(em, resources, resource, out var data))
                return false;

            bool overridesPrice = _prices.TryUnitPrice(resource, importing, data.m_Price.x, out double price);
            if (double.IsNaN(price) || double.IsInfinity(price) || price < 0)
                return false;

            var stock = em.GetBuffer<Resources>(company, true);
            int amountInStock = EconomyUtils.GetResources(resource, stock);
            int cash = EconomyUtils.GetResources(Resource.Money, stock);
            DirectTradeInvoice invoice;
            if (importing)
            {
                long room = em.GetComponentData<StorageLimitData>(prefab).m_Limit;
                foreach (var item in stock)
                    if (item.m_Amount > 0 && TryGetPhysicalResource(em, resources, item.m_Resource, out _))
                        room -= item.m_Amount;

                // Reserve room for older purchases whose trucks have not arrived yet.
                if (em.HasBuffer<CurrentTrading>(company))
                    foreach (var pending in em.GetBuffer<CurrentTrading>(company, true))
                        if (pending.m_TradingResourceAmount > 0 && TryGetPhysicalResource(em, resources, pending.m_TradingResource, out _))
                            room -= pending.m_TradingResourceAmount;

                invoice = DirectTradePolicy.PlanImport(requested, amountInStock, room, cash, price);
            }
            else
            {
                long reserve = 0;
                if (keepInputReserve && process.m_Input1.m_Resource == resource)
                    reserve += Math.Max(0, process.m_Input1.m_Amount);
                if (keepInputReserve && process.m_Input2.m_Resource == resource)
                    reserve += Math.Max(0, process.m_Input2.m_Amount);
                invoice = DirectTradePolicy.PlanExport(requested, amountInStock, reserve, cash, price);
            }

            settlement = new Settlement(company, resource, invoice, overridesPrice);
            return true;
        }

        private static bool TryGetPhysicalResource(EntityManager em, ResourcePrefabs resources, Resource resource, out ResourceData data)
        {
            data = default;
            ulong bits = (ulong)resource;
            if (resource == Resource.Money || bits == 0 || (bits & (bits - 1)) != 0
                || (uint)EconomyUtils.GetResourceIndex(resource) >= (uint)EconomyUtils.ResourceCount)
                return false;
            Entity prefab = resources[resource];
            if (prefab == Entity.Null || !em.HasComponent<ResourceData>(prefab))
                return false;
            data = em.GetComponentData<ResourceData>(prefab);
            return data.m_Weight > 0;
        }

        private void Commit(EntityManager em, NativeQueue<StatisticsEvent> statistics, NativeArray<int> resourceUsage,
            Settlement settlement, bool importing)
        {
            int amount = settlement.Invoice.Amount;
            int payment = settlement.Invoice.Payment;
            int signedAmount = importing ? amount : -amount;
            var stock = em.GetBuffer<Resources>(settlement.Company);
            EconomyUtils.AddResources(settlement.Resource, signedAmount, stock);
            if (payment > 0)
            {
                int change = importing ? -payment : payment;
                int before = EconomyUtils.GetResources(Resource.Money, stock);
                EconomyUtils.AddResources(Resource.Money, DirectTradePolicy.AddSaturated(before, change) - before, stock);
            }

            if (importing)
            {
                if (payment > 0 && em.HasComponent<CompanyStatisticData>(settlement.Company))
                {
                    var companyData = em.GetComponentData<CompanyStatisticData>(settlement.Company);
                    companyData.m_CurrentCostOfBuyingResources = DirectTradePolicy.AddSaturated(companyData.m_CurrentCostOfBuyingResources, payment);
                    em.SetComponentData(settlement.Company, companyData);
                }
                ImportAmount += amount;
                ImportOrders++;
                if (settlement.CustomPrice)
                    _prices.ImportQuotes++;
            }
            else
            {
                ExportAmount += amount;
                ExportOrders++;
                if (settlement.CustomPrice)
                    _prices.ExportQuotes++;
            }

            // Match the game's signs: city trade history shows imports as positive,
            // while the production panel's net trade accumulator uses exports as positive.
            int index = EconomyUtils.GetResourceIndex(settlement.Resource);
            statistics.Enqueue(new StatisticsEvent { m_Statistic = StatisticType.Trade, m_Parameter = index, m_Change = signedAmount });
            resourceUsage[index] = DirectTradePolicy.AddSaturated(resourceUsage[index], -signedAmount);
        }

        public void Dispose()
        {
            if (!_world.IsCreated)
                return;
            _buyers.Dispose();
            _exporters.Dispose();
        }
    }
}
