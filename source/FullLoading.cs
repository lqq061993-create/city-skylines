using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Common;
using Game.Buildings;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace IndustryMaster
{
    internal sealed class FullLoading : IDisposable
    {
        private readonly World _world;
        private readonly EntityQuery _trucks;
        private readonly Dictionary<Entity, Resource> _filled;
        private readonly Dictionary<Entity, int> _booked, _planned;
        private readonly HashSet<Entity> _boosted;
        private readonly List<Cleanup> _cleanup;
        private readonly HashSet<(Entity Vehicle, Entity Payer, Resource Resource)> _completed;
        private FieldInfo _deliveryQueue;
        private uint _lastCleanup;
        internal long FilledLoads, GeneratedKg;
        internal FullLoading(World world)
        {
            _world = world;
            _filled = new Dictionary<Entity, Resource>();
            _booked = new Dictionary<Entity, int>(); _planned = new Dictionary<Entity, int>(); _cleanup = new List<Cleanup>();
            _boosted = new HashSet<Entity>();
            _completed = new HashSet<(Entity, Entity, Resource)>();
            _trucks = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Vehicles.DeliveryTruck>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<UpdateFrame>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }
        internal void BeforeActions<T>(T job) where T : struct
        {
            if (_deliveryQueue == null) _deliveryQueue = typeof(T).GetField("m_DeliveredQueue", BindingFlags.Public | BindingFlags.Instance);
            if (_deliveryQueue == null) throw new MissingFieldException("Delivery action queue");
            var queue = (NativeQueue<DeliveryTruckAISystem.DeliveredStack>)_deliveryQueue.GetValue(job);
            // An actual pickup begins a new load cycle, including return loads of the
            // same resource. Partial deliveries alone never trigger another refill.
            _cleanup.Clear();
            _completed.Clear();
            using (var items = queue.ToArray(Allocator.Temp))
                foreach (var item in items)
                {
                    if (item.amount < 0)
                    { _filled.Remove(item.vehicle); _booked.Remove(item.vehicle); _boosted.Remove(item.vehicle); _planned[item.vehicle] = (int)Math.Min(int.MaxValue, -(long)item.amount); }
                    else if (item.amount > 0 && !item.buildingUpkeep && !item.moneyRefund && _filled.ContainsKey(item.vehicle))
                    {
                        var em = _world.EntityManager;
                        Entity payer = ResolveCompany(item.costPayer), target = ResolveCompany(item.target);
                        if (payer == Entity.Null || target == Entity.Null || !em.HasBuffer<CurrentTrading>(payer)) continue;
                        Entity root = em.HasComponent<Controller>(item.vehicle) ? em.GetComponentData<Controller>(item.vehicle).m_Controller : item.vehicle;
                        if (!_completed.Add((root, payer, item.resource))) continue;
                        long original = 0; int amount; bool boosted = _boosted.Contains(item.vehicle);
                        if (em.HasBuffer<LayoutElement>(root))
                            foreach (var part in em.GetBuffer<LayoutElement>(root, true))
                            { if (_booked.TryGetValue(part.m_Vehicle, out amount)) original += amount; boosted |= _boosted.Contains(part.m_Vehicle); }
                        if (original == 0 && _booked.TryGetValue(item.vehicle, out amount)) original = amount;
                        if (!boosted || original <= 0 || original > int.MaxValue) continue;
                        var records = em.GetBuffer<CurrentTrading>(payer, true);
                        foreach (var record in records)
                            if (record.m_TradingResource == item.resource && Math.Abs((long)record.m_TradingResourceAmount) == original)
                            {
                                int count = 0;
                                foreach (var other in records) if (SameRecord(record, other)) count++;
                                _cleanup.Add(new Cleanup { Company = payer, Record = record, CountBefore = count });
                                break;
                            }
                    }
                }
        }
        private Entity ResolveCompany(Entity entity)
        {
            var em = _world.EntityManager;
            if (em.HasBuffer<Resources>(entity)) return entity;
            if (em.HasBuffer<Renter>(entity)) foreach (var renter in em.GetBuffer<Renter>(entity, true))
                if (em.HasComponent<CompanyData>(renter.m_Renter) && em.HasBuffer<Resources>(renter.m_Renter)) return renter.m_Renter;
            return Entity.Null;
        }
        internal void FinishActions()
        {
            var em = _world.EntityManager;
            foreach (var item in _cleanup)
            {
                if (!em.Exists(item.Company) || !em.HasBuffer<CurrentTrading>(item.Company)) continue;
                var records = em.GetBuffer<CurrentTrading>(item.Company);
                int count = 0;
                foreach (var record in records) if (SameRecord(item.Record, record)) count++;
                // The native settlement may already have removed this booking. Never
                // remove a second same-sized order in that case.
                if (count < item.CountBefore) continue;
                for (int i = 0; i < records.Length; i++)
                    if (SameRecord(item.Record, records[i]))
                    { records.RemoveAt(i); break; }
            }
            _cleanup.Clear();
            FillCurrentBatch();
        }
        internal void FillCurrentBatch()
        {
            if (!Mod.FullLoadingActive || !_world.IsCreated) return;
            var em = _world.EntityManager;
            em.CompleteDependencyBeforeRW<Game.Vehicles.DeliveryTruck>();
            uint frame = _world.GetExistingSystemManaged<SimulationSystem>().frameIndex;
            _trucks.SetSharedComponentFilter(new UpdateFrame(frame % 16));
            try
            {
                using (var entities = _trucks.ToEntityArray(Allocator.Temp))
                    foreach (Entity entity in entities)
                    {
                        var truck = em.GetComponentData<Game.Vehicles.DeliveryTruck>(entity);
                        if ((truck.m_State & DeliveryTruckFlags.Loaded) == 0)
                        { _filled.Remove(entity); _booked.Remove(entity); _planned.Remove(entity); _boosted.Remove(entity); continue; }
                        if ((truck.m_State & DeliveryTruckFlags.UpdateSellerQuantity) != 0 || truck.m_Resource == Resource.NoResource || truck.m_Resource == Resource.Money) continue;
                        Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
                        if (!em.HasComponent<DeliveryTruckData>(prefab)) continue;
                        var data = em.GetComponentData<DeliveryTruckData>(prefab);
                        int capacity = data.m_CargoCapacity;
                        if (capacity <= 0 || (data.m_TransportedResources & truck.m_Resource) == 0) continue;
                        Resource filledResource;
                        if (_filled.TryGetValue(entity, out filledResource) && filledResource == truck.m_Resource) continue;
                        _filled[entity] = truck.m_Resource;
                        int booked;
                        _booked[entity] = _planned.TryGetValue(entity, out booked) ? booked : Math.Max(0, truck.m_Amount);
                        _planned.Remove(entity);
                        int extra = Math.Max(0, capacity - Math.Max(0, truck.m_Amount));
                        if (extra == 0) continue;
                        _boosted.Add(entity);
                        truck.m_Amount = capacity;
                        em.SetComponentData(entity, truck);
                        GeneratedKg += extra; FilledLoads++;
                    }
            }
            finally { _trucks.ResetFilter(); }
            if (unchecked(frame - _lastCleanup) > 4096)
            {
                _lastCleanup = frame;
                var deleted = new List<Entity>();
                foreach (Entity entity in _filled.Keys) if (!em.Exists(entity) || em.HasComponent<Deleted>(entity)) deleted.Add(entity);
                foreach (Entity entity in deleted) { _filled.Remove(entity); _booked.Remove(entity); _planned.Remove(entity); _boosted.Remove(entity); }
            }
        }
        public void Dispose() { if (_world.IsCreated) _trucks.Dispose(); _filled.Clear(); }
        private static bool SameRecord(CurrentTrading a, CurrentTrading b) => a.m_TradingResource == b.m_TradingResource
            && a.m_TradingResourceAmount == b.m_TradingResourceAmount && a.m_TradingStartFrameIndex == b.m_TradingStartFrameIndex
            && a.m_OutsideConnectionType == b.m_OutsideConnectionType;
        private sealed class Cleanup { public Entity Company; public CurrentTrading Record; public int CountBefore; }
    }
}
