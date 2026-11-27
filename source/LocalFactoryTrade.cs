using System;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using OutsideConnection = Game.Objects.OutsideConnection;

namespace IndustryMaster
{
    internal sealed class LocalFactoryTrade : IDisposable
    {
        private static readonly ComponentTypeSet OrderComponents = new ComponentTypeSet(
            ComponentType.ReadWrite<ResourceBuyer>(), ComponentType.ReadWrite<PathInformation>(),
            ComponentType.ReadWrite<PathElement>());

        private readonly World _world;
        private readonly ResourceSystem _resources;
        private readonly EntityQuery _buyers;

        internal long Orders { get; private set; }
        internal long Amount { get; private set; }

        internal LocalFactoryTrade(World world)
        {
            _world = world;
            _resources = world.GetExistingSystemManaged<ResourceSystem>()
                ?? throw new InvalidOperationException("Resource system unavailable");
            _buyers = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ResourceBuyer>(), ComponentType.ReadOnly<PathInformation>(),
                    ComponentType.ReadOnly<PathElement>(), ComponentType.ReadOnly<TripNeeded>(),
                    ComponentType.ReadOnly<Game.Companies.CommercialCompany>(), ComponentType.ReadOnly<BuyingCompany>(),
                    ComponentType.ReadOnly<PropertyRenter>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Resources>() },
                None = new[] { ComponentType.ReadOnly<ResourceBought>(), ComponentType.ReadOnly<ResourceExporter>(),
                    ComponentType.ReadOnly<TravelPurpose>(), ComponentType.ReadOnly<Game.Companies.StorageCompany>(),
                    ComponentType.ReadOnly<OutsideConnection>(), ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Destroyed>(), ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Temp>() }
            });
        }

        internal void SettleOrders()
        {
            if (_buyers.IsEmptyIgnoreFilter)
                return;

            var em = _world.EntityManager;
            var prefabs = _resources.GetPrefabs();
            using var buyers = _buyers.ToEntityArray(Allocator.Temp);
            foreach (Entity buyer in buyers)
            {
                var path = em.GetComponentData<PathInformation>(buyer);
                if ((path.m_State & (PathFlags.Pending | PathFlags.Failed | PathFlags.Obsolete)) != 0
                    || path.m_Destination == buyer || !IsFactory(em, path.m_Destination))
                    continue;

                var order = em.GetComponentData<ResourceBuyer>(buyer);
                Resource resource = order.m_ResourceNeeded;
                Entity seller = path.m_Destination;
                if (order.m_Payer != buyer || order.m_AmountNeeded <= 0 || (order.m_Flags & SetupTargetFlags.Industrial) == 0
                    || (order.m_Flags & SetupTargetFlags.BuildingUpkeep) != 0 || !HasLocalProperty(em, buyer)
                    || !TryGetPhysicalResource(em, prefabs, resource, out var data))
                    continue;

                Entity sellerPrefab = em.GetComponentData<PrefabRef>(seller).m_Prefab;
                if (!em.HasComponent<IndustrialProcessData>(sellerPrefab))
                    continue;
                var process = em.GetComponentData<IndustrialProcessData>(sellerPrefab);
                if (process.m_Output.m_Resource != resource || process.m_Output.m_Amount <= 0)
                    continue;

                var sellerStock = em.GetBuffer<Resources>(seller, true);
                int stock = EconomyUtils.GetResources(resource, sellerStock);
                // The native buyer already retries a local match below this stock threshold.
                if (stock <= 0 || stock < order.m_AmountNeeded / 2)
                    continue;

                var buyerStock = em.GetBuffer<Resources>(buyer, true);
                long room = GetStorageRoom(em, prefabs, buyer, buyerStock);
                long paymentRoom = Math.Min((long)EconomyUtils.GetResources(Resource.Money, buyerStock) - int.MinValue,
                    (long)int.MaxValue - EconomyUtils.GetResources(Resource.Money, sellerStock));
                float sellerBuyCost = em.HasBuffer<TradeCost>(seller)
                    ? EconomyUtils.GetTradeCost(resource, em.GetBuffer<TradeCost>(seller, true)).m_BuyCost : 0f;
                float basePrice = data.m_Price.x;
                if (basePrice < 0f || sellerBuyCost < 0f || !float.IsFinite(basePrice) || !float.IsFinite(sellerBuyCost))
                    continue;

                // The native buyer has not queued a sale or a trip while this order and path are present.
                // Consume just this order, then reacquire buffers after the archetype change.
                var invoice = PlanInvoice(order.m_AmountNeeded, stock, room, paymentRoom, basePrice, sellerBuyCost);
                em.RemoveComponent(buyer, in OrderComponents);
                if (invoice.Amount <= 0)
                    continue;

                Commit(em, buyer, seller, resource, data, path.m_Distance, invoice);
                Orders++;
                Amount += invoice.Amount;
            }
        }

        private static bool IsFactory(EntityManager em, Entity entity)
        {
            return em.Exists(entity)
                && (em.HasComponent<Game.Companies.ProcessingCompany>(entity) || em.HasComponent<Game.Companies.ExtractorCompany>(entity))
                && !em.HasComponent<Game.Companies.CommercialCompany>(entity) && !em.HasComponent<ServiceAvailable>(entity)
                && !em.HasComponent<Game.Companies.StorageCompany>(entity) && !em.HasComponent<Game.Buildings.CargoTransportStation>(entity)
                && !em.HasComponent<OutsideConnection>(entity) && !em.HasComponent<Deleted>(entity)
                && !em.HasComponent<Destroyed>(entity) && !em.HasComponent<Created>(entity) && !em.HasComponent<Temp>(entity)
                && em.HasComponent<PrefabRef>(entity) && em.HasBuffer<Resources>(entity)
                && HasLocalProperty(em, entity);
        }

        private static bool HasLocalProperty(EntityManager em, Entity company)
        {
            if (!em.HasComponent<PropertyRenter>(company))
                return false;
            Entity property = em.GetComponentData<PropertyRenter>(company).m_Property;
            return em.Exists(property) && !em.HasComponent<OutsideConnection>(property)
                && !em.HasComponent<Deleted>(property) && !em.HasComponent<Destroyed>(property)
                && !em.HasComponent<Abandoned>(property) && !em.HasComponent<Condemned>(property)
                && !em.HasComponent<Temp>(property)
                && (!em.HasComponent<Building>(property)
                    || !BuildingUtils.CheckOption(em.GetComponentData<Building>(property), BuildingOption.Inactive));
        }

        private static long GetStorageRoom(EntityManager em, ResourcePrefabs prefabs, Entity buyer, DynamicBuffer<Resources> stock)
        {
            Entity prefab = em.GetComponentData<PrefabRef>(buyer).m_Prefab;
            long room = em.HasComponent<StorageLimitData>(prefab) ? em.GetComponentData<StorageLimitData>(prefab).m_Limit : int.MaxValue;
            foreach (var item in stock)
                if (item.m_Amount > 0 && TryGetPhysicalResource(em, prefabs, item.m_Resource, out _))
                    room -= item.m_Amount;

            // Older native deliveries already ordered by this shop can still arrive.
            if (em.HasBuffer<CurrentTrading>(buyer))
                foreach (var trade in em.GetBuffer<CurrentTrading>(buyer, true))
                    if (trade.m_TradingResourceAmount > 0 && TryGetPhysicalResource(em, prefabs, trade.m_TradingResource, out _))
                        room -= trade.m_TradingResourceAmount;

            return room;
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
            return data.m_Weight > 0f && !float.IsInfinity(data.m_Weight);
        }

        private static DirectTradeInvoice PlanInvoice(int requested, int stock, long room, long paymentRoom, float basePrice, float buyCost)
        {
            if (room <= 0)
                return default;

            int amount = (int)Math.Min(Math.Min(requested, stock), room);
            // The game's business invoices permit debt, so bound only against balances overflowing.
            long limit = Math.Min(paymentRoom, Policy.MaximumTradeInvoice);
            double payment = Math.Round(basePrice * amount + buyCost * amount);
            if (payment > limit)
            {
                int low = 0, high = amount - 1;
                while (low < high)
                {
                    int middle = low + (high - low + 1) / 2;
                    if (Math.Round(basePrice * middle + buyCost * middle) <= limit)
                        low = middle;
                    else
                        high = middle - 1;
                }
                amount = low;
                payment = Math.Round(basePrice * amount + buyCost * amount);
            }

            return new DirectTradeInvoice(amount, (int)payment);
        }

        private static void Commit(EntityManager em, Entity buyer, Entity seller, Resource resource,
            ResourceData data, float distance, DirectTradeInvoice invoice)
        {
            var buyerStock = em.GetBuffer<Resources>(buyer);
            var sellerStock = em.GetBuffer<Resources>(seller);
            EconomyUtils.AddResources(resource, -invoice.Amount, sellerStock);
            EconomyUtils.AddResources(resource, invoice.Amount, buyerStock);
            if (invoice.Payment > 0)
            {
                EconomyUtils.AddResources(Resource.Money, -invoice.Payment, buyerStock);
                EconomyUtils.AddResources(Resource.Money, invoice.Payment, sellerStock);
            }

            var buyingCompany = em.GetComponentData<BuyingCompany>(buyer);
            buyingCompany.m_LastTradePartner = seller;
            em.SetComponentData(buyer, buyingCompany);

            if (em.HasComponent<CompanyStatisticData>(seller))
            {
                var statistics = em.GetComponentData<CompanyStatisticData>(seller);
                statistics.m_CurrentNumberOfCustomers = DirectTradePolicy.AddSaturated(statistics.m_CurrentNumberOfCustomers, 1);
                em.SetComponentData(seller, statistics);
            }
            if (em.HasComponent<CompanyStatisticData>(buyer))
            {
                var statistics = em.GetComponentData<CompanyStatisticData>(buyer);
                statistics.m_CurrentCostOfBuyingResources = DirectTradePolicy.AddSaturated(statistics.m_CurrentCostOfBuyingResources, invoice.Payment);
                em.SetComponentData(buyer, statistics);
            }

            UpdateTradeCosts(em, buyer, seller, resource, data.m_Weight, distance, invoice.Amount);
        }

        private static void UpdateTradeCosts(EntityManager em, Entity buyer, Entity seller, Resource resource,
            float weight, float distance, int amount)
        {
            if (!em.HasBuffer<TradeCost>(seller) || !float.IsFinite(distance) || distance < 0f)
                return;

            var sellerCosts = em.GetBuffer<TradeCost>(seller);
            var sellerCost = EconomyUtils.GetTradeCost(resource, sellerCosts);
            bool hasBuyerCosts = em.HasBuffer<TradeCost>(buyer);
            var buyerCosts = hasBuyerCosts ? em.GetBuffer<TradeCost>(buyer) : default;
            var buyerCost = hasBuyerCosts ? EconomyUtils.GetTradeCost(resource, buyerCosts) : default;
            float routeCost = EconomyUtils.GetTransportCost(distance, resource, amount, weight) / (1f + amount);
            float sellerEstimate = math.lerp(sellerCost.m_SellCost, routeCost + buyerCost.m_SellCost, 0.5f);
            if (float.IsFinite(sellerEstimate))
            {
                sellerCost.m_SellCost = sellerEstimate;
                EconomyUtils.SetTradeCost(resource, sellerCost, sellerCosts, keepLastTime: true);
            }
            if (hasBuyerCosts)
            {
                float estimate = routeCost + sellerCost.m_BuyCost;
                float buyerEstimate = estimate < buyerCost.m_BuyCost ? estimate : math.lerp(buyerCost.m_BuyCost, estimate, 0.5f);
                if (float.IsFinite(buyerEstimate))
                {
                    buyerCost.m_BuyCost = buyerEstimate;
                    EconomyUtils.SetTradeCost(resource, buyerCost, buyerCosts, keepLastTime: true);
                }
            }
        }

        public void Dispose()
        {
            if (_world.IsCreated)
                _buyers.Dispose();
        }
    }
}
