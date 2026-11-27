using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal.PSI.Environment;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using StorageLimit = Game.Prefabs.StorageLimit;

namespace IndustryMaster
{
    internal enum IntKind { Storage, Fleet, Truck }
    internal sealed class PrefabController : IDisposable
    {
        private readonly World _world;
        private readonly Dictionary<Entity, IntSnapshot>[] _integers;
        private readonly Dictionary<Entity, Recipe> _recipes;
        private readonly HashSet<Entity> _factories;
        private readonly HashSet<Entity> _producers;
        private readonly Dictionary<Entity, int> _maximumStocks = new Dictionary<Entity, int>();
        private EntityQuery _storageQuery;
        private bool _hasStorageQuery;
        internal bool HasOverstock { get; private set; }
        internal int ProducerCount => _producers.Count;
        internal string Counts => _factories.Count + "/" + _integers[1].Count + "/" + _integers[2].Count;
        internal PrefabController(World world)
        {
            _world = world;
            _integers = new[] { new Dictionary<Entity, IntSnapshot>(), new Dictionary<Entity, IntSnapshot>(), new Dictionary<Entity, IntSnapshot>() };
            _recipes = new Dictionary<Entity, Recipe>();
            _factories = new HashSet<Entity>();
            _producers = new HashSet<Entity>();
        }
        internal bool IsProducer(Entity prefab) => _producers.Contains(prefab);

        internal void Apply(MasterSettings settings)
        {
            var em = _world.EntityManager;
            em.CompleteAllTrackedJobs();
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<IndustrialProcessData>());
            try
            {
                using (var entities = query.ToEntityArray(Allocator.Temp))
                    foreach (Entity entity in entities)
                    {
                        if (em.HasComponent<Deleted>(entity) || em.HasComponent<StorageCompanyData>(entity)
                            || em.HasComponent<CommercialCompanyData>(entity)) continue;
                        bool extractor = em.HasComponent<ExtractorCompanyData>(entity);
                        if (!extractor && !em.HasComponent<IndustrialCompanyData>(entity)) continue;
                        var data = em.GetComponentData<IndustrialProcessData>(entity);
                        if (data.m_Output.m_Resource == Resource.NoResource || data.m_Output.m_Resource == Resource.Money || data.m_Output.m_Amount <= 0) continue;
                        if (data.m_Input1.m_Resource == data.m_Output.m_Resource && data.m_Input2.m_Resource == Resource.NoResource
                            && data.m_Input1.m_Amount == data.m_Output.m_Amount) continue;
                        _producers.Add(entity);
                        if (!EconomyUtils.IsOfficeResource(data.m_Output.m_Resource) && em.HasComponent<StorageLimitData>(entity)) _factories.Add(entity);
                        Recipe recipe;
                        if (!_recipes.TryGetValue(entity, out recipe))
                        {
                            recipe = new Recipe { Original = data, Applied = data };
                            // Managed asset recipes were not altered by the old Production Master.
                            PrefabBase prefab;
                            Game.Prefabs.ProcessingCompany component;
                            if (Prefabs.TryGetPrefab<PrefabBase>(entity, out prefab) && prefab != null && prefab.TryGet<Game.Prefabs.ProcessingCompany>(out component))
                            {
                                recipe.Original.m_Output.m_Amount = component.process.m_Output.m_Amount;
                                recipe.Original.m_Input1.m_Amount = component.process.m_Input1.m_Amount;
                                recipe.Original.m_Input2.m_Amount = component.process.m_Input2.m_Amount;
                            }
                            _recipes.Add(entity, recipe);
                        }
                        float multiplier = settings.ProductionEnabled ? settings.Multiplier(data.m_Output.m_Resource, extractor) : 1;
                        bool scaleInputs = settings.ProductionEnabled && settings.ScaleInputs;
                        int output = Policy.Scale(recipe.Original.m_Output.m_Amount, multiplier);
                        int input1 = scaleInputs ? Policy.Scale(recipe.Original.m_Input1.m_Amount, multiplier) : recipe.Original.m_Input1.m_Amount;
                        int input2 = scaleInputs ? Policy.Scale(recipe.Original.m_Input2.m_Amount, multiplier) : recipe.Original.m_Input2.m_Amount;
                        if (data.m_Output.m_Amount != output || data.m_Input1.m_Amount != input1 || data.m_Input2.m_Amount != input2)
                        {
                            data.m_Output.m_Amount = output;
                            data.m_Input1.m_Amount = input1;
                            data.m_Input2.m_Amount = input2;
                            em.SetComponentData(entity, data);
                        }
                        recipe.Applied = data;
                    }
            }
            finally { query.Dispose(); }
            ApplyStorage(settings);
            if (settings.FleetEnabled)
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<TransportCompanyData>());
                try
                {
                    using (var entities = query.ToEntityArray(Allocator.Temp))
                        foreach (Entity entity in entities)
                        {
                            CompanyPrefab prefab;
                            if (em.HasComponent<Deleted>(entity) || !Prefabs.TryGetPrefab<CompanyPrefab>(entity, out prefab) || prefab == null) continue;
                            int original = em.GetComponentData<TransportCompanyData>(entity).m_MaxTransports;
                            if (original > 0) Set(entity, IntKind.Fleet, Policy.VehicleSlots(original));
                        }
                }
                finally { query.Dispose(); }
            }
            else Restore(IntKind.Fleet);
            bool truckChanged = false;
            if (settings.TruckEnabled)
            {
                query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<CarData>(), ComponentType.ReadOnly<DeliveryTruckData>());
                try
                {
                    using (var entities = query.ToEntityArray(Allocator.Temp))
                        foreach (Entity entity in entities)
                        {
                            if (em.HasComponent<Deleted>(entity)) continue;
                            var data = em.GetComponentData<DeliveryTruckData>(entity);
                            if (data.m_CargoCapacity > 0 && data.m_TransportedResources != Resource.NoResource)
                                truckChanged |= Set(entity, IntKind.Truck, Policy.TruckKg(settings.TruckTonnes));
                        }
                }
                finally { query.Dispose(); }
            }
            else truckChanged = Restore(IntKind.Truck);
            if (truckChanged) Mod.Current.RefreshDispatch();
        }

        internal void ApplyStorage(MasterSettings settings)
        {
            HasOverstock = false;
            if (!settings.StorageEnabled) { Restore(IntKind.Storage); return; }
            var em = _world.EntityManager;
            foreach (Entity facility in FindFacilities()) if (em.HasComponent<StorageLimitData>(facility)) Set(facility, IntKind.Storage, 0);
            ProtectStorage();
        }

        internal void ProtectStorage()
        {
            HasOverstock = false;
            if (_factories.Count == 0) return;
            var em = _world.EntityManager;
            if (!_hasStorageQuery)
            {
                _storageQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Resources>() },
                    Any = new[] { ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(), ComponentType.ReadOnly<Game.Companies.ExtractorCompany>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
                });
                _hasStorageQuery = true;
            }
            _maximumStocks.Clear();
            em.CompleteDependencyBeforeRO<Resources>();
            using (var entities = _storageQuery.ToEntityArray(Allocator.Temp))
                foreach (Entity entity in entities)
                {
                    Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (!_factories.Contains(prefab)) continue;
                    int stock = EconomyUtils.GetTotalStorageUsed(em.GetBuffer<Resources>(entity, true));
                    if (!_maximumStocks.TryGetValue(prefab, out int previous) || stock > previous) _maximumStocks[prefab] = stock;
                }
            foreach (Entity entity in _factories)
            {
                _maximumStocks.TryGetValue(entity, out int stock);
                int capacity = Math.Max(Policy.StorageKg, stock);
                HasOverstock |= capacity > Policy.StorageKg;
                Set(entity, IntKind.Storage, capacity);
            }
        }

        public void Dispose()
        {
            if (_hasStorageQuery && _world.IsCreated) _storageQuery.Dispose();
            _hasStorageQuery = false;
        }

        private PrefabSystem Prefabs => _world.GetExistingSystemManaged<PrefabSystem>();
        private int Read(Entity entity, IntKind kind)
        {
            var em = _world.EntityManager;
            switch (kind)
            {
                case IntKind.Storage: return em.GetComponentData<StorageLimitData>(entity).m_Limit;
                case IntKind.Fleet: return em.GetComponentData<TransportCompanyData>(entity).m_MaxTransports;
                default: return em.GetComponentData<DeliveryTruckData>(entity).m_CargoCapacity;
            }
        }
        private void Write(Entity entity, IntKind kind, int value)
        {
            var em = _world.EntityManager;
            switch (kind)
            {
                case IntKind.Storage: var storage = em.GetComponentData<StorageLimitData>(entity); storage.m_Limit = value; em.SetComponentData(entity, storage); break;
                case IntKind.Fleet: var fleet = em.GetComponentData<TransportCompanyData>(entity); fleet.m_MaxTransports = value; em.SetComponentData(entity, fleet); break;
                default: var truck = em.GetComponentData<DeliveryTruckData>(entity); truck.m_CargoCapacity = value; em.SetComponentData(entity, truck); break;
            }
        }
        private bool Set(Entity entity, IntKind kind, int target)
        {
            int before = Read(entity, kind);
            IntSnapshot snapshot;
            if (!_integers[(int)kind].TryGetValue(entity, out snapshot))
            {
                snapshot = new IntSnapshot { Original = before, Applied = before, ManagedOriginal = before };
                PrefabBase prefab;
                if (Prefabs.TryGetPrefab<PrefabBase>(entity, out prefab) && prefab != null)
                {
                    StorageLimit storage; DeliveryTruck truck;
                    Game.Prefabs.ProcessingCompany processing; Game.Prefabs.StorageCompany warehouse;
                    if (kind == IntKind.Storage && prefab.TryGet<StorageLimit>(out storage))
                    { snapshot.ManagedOriginal = storage.storageLimit; snapshot.Setter = x => storage.storageLimit = x; }
                    else if (kind == IntKind.Truck && prefab.TryGet<DeliveryTruck>(out truck))
                    { snapshot.ManagedOriginal = truck.m_CargoCapacity; snapshot.Setter = x => truck.m_CargoCapacity = x; }
                    else if (kind == IntKind.Fleet && prefab.TryGet<Game.Prefabs.ProcessingCompany>(out processing))
                    { snapshot.ManagedOriginal = processing.transports; snapshot.Setter = x => processing.transports = x; }
                    else if (kind == IntKind.Fleet && prefab.TryGet<Game.Prefabs.StorageCompany>(out warehouse))
                    { snapshot.ManagedOriginal = warehouse.transports; snapshot.Setter = x => warehouse.transports = x; }
                }
                if ((kind == IntKind.Storage && (before == 500000000 || before == 0)) || (kind == IntKind.Truck && before == 1000000))
                    snapshot.Original = snapshot.ManagedOriginal;
                _integers[(int)kind].Add(entity, snapshot);
            }
            snapshot.Applied = target;
            snapshot.Setter?.Invoke(target);
            if (before == target) return false;
            Write(entity, kind, target);
            return true;
        }
        private bool Has(Entity entity, IntKind kind)
        {
            var em = _world.EntityManager;
            return em.Exists(entity) && (kind == IntKind.Storage ? em.HasComponent<StorageLimitData>(entity)
                : kind == IntKind.Fleet ? em.HasComponent<TransportCompanyData>(entity) : em.HasComponent<DeliveryTruckData>(entity));
        }
        private bool Restore(IntKind kind)
        {
            bool changed = false;
            foreach (var pair in _integers[(int)kind])
            {
                if (Has(pair.Key, kind) && Read(pair.Key, kind) == pair.Value.Applied)
                { Write(pair.Key, kind, pair.Value.Original); changed = true; }
                pair.Value.Setter?.Invoke(pair.Value.ManagedOriginal);
            }
            _integers[(int)kind].Clear();
            return changed;
        }
        internal void RestoreAll()
        {
            if (!_world.IsCreated) return;
            var em = _world.EntityManager;
            em.CompleteAllTrackedJobs();
            Restore(IntKind.Storage); Restore(IntKind.Fleet);
            bool trucksChanged = Restore(IntKind.Truck);
            foreach (var pair in _recipes)
                if (em.Exists(pair.Key) && em.HasComponent<IndustrialProcessData>(pair.Key))
                {
                    var data = em.GetComponentData<IndustrialProcessData>(pair.Key);
                    if (data.m_Output.m_Amount == pair.Value.Applied.m_Output.m_Amount) data.m_Output.m_Amount = pair.Value.Original.m_Output.m_Amount;
                    if (data.m_Input1.m_Amount == pair.Value.Applied.m_Input1.m_Amount) data.m_Input1.m_Amount = pair.Value.Original.m_Input1.m_Amount;
                    if (data.m_Input2.m_Amount == pair.Value.Applied.m_Input2.m_Amount) data.m_Input2.m_Amount = pair.Value.Original.m_Input2.m_Amount;
                    em.SetComponentData(pair.Key, data);
                }
            _recipes.Clear(); _producers.Clear(); _factories.Clear();
            if (trucksChanged) Mod.Current.RefreshDispatch();
        }

        private HashSet<Entity> FindFacilities()
        {
            var em = _world.EntityManager;
            var result = new HashSet<Entity>();
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>());
            try
            {
                using (var entities = query.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity entity in entities)
                        if (!em.HasComponent<Deleted>(entity) && (em.HasComponent<ExtractorFacilityData>(entity)
                            || (em.HasComponent<PlaceholderBuildingData>(entity) && em.GetComponentData<PlaceholderBuildingData>(entity).m_Type == BuildingType.ExtractorBuilding))) result.Add(entity);
                    bool changed;
                    do
                    {
                        changed = false;
                        foreach (Entity entity in entities)
                        {
                            if (result.Contains(entity) || !em.HasBuffer<ServiceUpgradeBuilding>(entity)) continue;
                            bool any = false, all = true;
                            foreach (var building in em.GetBuffer<ServiceUpgradeBuilding>(entity, true))
                            { any |= result.Contains(building.m_Building); all &= result.Contains(building.m_Building); }
                            if (any && all) { result.Add(entity); changed = true; }
                        }
                    } while (changed);
                }
            }
            finally { query.Dispose(); }
            return result;
        }

        internal void Audit()
        {
            string dir = Path.Combine(EnvPath.kUserDataPath, "ModsData", "IndustryMaster");
            Directory.CreateDirectory(dir);
            var report = new StringBuilder("Prefab\tKind\tOriginal\tCurrent\n");
            for (int k = 0; k < 3; k++) foreach (var pair in _integers[k])
                if (Has(pair.Key, (IntKind)k)) report.Append(Name(pair.Key)).Append('\t').Append((IntKind)k).Append('\t').Append(pair.Value.Original).Append('\t').Append(Read(pair.Key, (IntKind)k)).Append('\n');
            File.WriteAllText(Path.Combine(dir, "capacities.tsv"), report.ToString(), Encoding.UTF8);
            report = new StringBuilder("Prefab\tResource\tOriginalOutput\tCurrentOutput\tOriginalInput1\tCurrentInput1\tOriginalInput2\tCurrentInput2\n");
            foreach (var pair in _recipes)
            {
                var data = _world.EntityManager.GetComponentData<IndustrialProcessData>(pair.Key);
                report.Append(Name(pair.Key)).Append('\t').Append(data.m_Output.m_Resource).Append('\t').Append(pair.Value.Original.m_Output.m_Amount).Append('\t').Append(data.m_Output.m_Amount)
                    .Append('\t').Append(pair.Value.Original.m_Input1.m_Amount).Append('\t').Append(data.m_Input1.m_Amount).Append('\t').Append(pair.Value.Original.m_Input2.m_Amount).Append('\t').Append(data.m_Input2.m_Amount).Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "recipes.tsv"), report.ToString(), Encoding.UTF8);
            var selection = _world.GetExistingSystemManaged<VehicleCapacitySystem>().GetDeliveryTruckSelectData();
            report = new StringBuilder("Resource\tMinimumKg\tMaximumKg\n");
            foreach (Resource resource in Enum.GetValues(typeof(Resource)))
            {
                ulong bits = (ulong)resource;
                if (bits == 0 || (bits & (bits - 1)) != 0 || resource == Resource.Money) continue;
                int min, max; selection.GetCapacityRange(resource, out min, out max);
                if (max > 0) report.Append(resource).Append('\t').Append(min).Append('\t').Append(max).Append('\n');
            }
            File.WriteAllText(Path.Combine(dir, "dispatch-capacities.tsv"), report.ToString(), Encoding.UTF8);
        }
        private string Name(Entity entity)
        { PrefabBase prefab; return Prefabs.TryGetPrefab<PrefabBase>(entity, out prefab) && prefab != null ? prefab.name : entity.ToString(); }
        private sealed class IntSnapshot { public int Original, Applied, ManagedOriginal; public Action<int> Setter; }
        private sealed class Recipe { public IndustrialProcessData Original, Applied; }
    }
}
