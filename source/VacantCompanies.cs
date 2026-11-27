using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Logging;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using LoadPurpose = Colossal.Serialization.Entities.Purpose;

namespace IndustryMaster
{
    public sealed class VacantCompanies : GameSystemBase
    {
        private const int BatchSize = 24;
        private const int ScanSize = 128;
        private const int MaxInitializationWaits = 3;

        private enum Business { Commercial, Manufacturing, Office, Storage }

        private readonly List<Template> _templates = new List<Template>();
        private readonly List<Entity> _targets = new List<Entity>();
        private readonly List<(Entity Property, Entity Company, Entity Template, int Waits)> _starting =
            new List<(Entity, Entity, Entity, int)>(BatchSize);
        private readonly List<(Entity Property, Entity Company)> _awaitingRent = new List<(Entity, Entity)>(BatchSize);
        private EntityQuery _properties, _companyPrefabs;
        private PropertyProcessingSystem _propertyProcessing;
        private SimulationSystem _simulation;
        private int _cursor, _templateCursor;
        private bool _scanned;

        internal bool IsRunning { get; private set; }
        internal int Targets { get; private set; }
        internal int Queued { get; private set; }
        internal int Filled { get; private set; }
        internal int Skipped { get; private set; }
        internal bool Failed { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            _propertyProcessing = World.GetOrCreateSystemManaged<PropertyProcessingSystem>();
            _simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            RequireForUpdate<EconomyParameterData>();
            _properties = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Renter>(), ComponentType.ReadOnly<PropertyOnMarket>()
                },
                Any = new[] { ComponentType.ReadOnly<CommercialProperty>(), ComponentType.ReadOnly<IndustrialProperty>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Destroyed>(), ComponentType.ReadOnly<Abandoned>(), ComponentType.ReadOnly<Condemned>(),
                    ComponentType.ReadOnly<UnderConstruction>(), ComponentType.ReadOnly<Placeholder>(), ComponentType.ReadOnly<Signature>(),
                    ComponentType.ReadOnly<ResidentialProperty>(), ComponentType.ReadOnly<ExtractorProperty>(),
                    ComponentType.ReadOnly<Game.Objects.OutsideConnection>(), ComponentType.ReadOnly<Game.Buildings.ServiceUpgrade>(),
                    ComponentType.ReadOnly<PropertyToBeOnMarket>()
                }
            });
            _companyPrefabs = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ArchetypeData>(),
                    ComponentType.ReadOnly<IndustrialProcessData>(), ComponentType.ReadOnly<CompanyBrandElement>()
                },
                Any = new[] { ComponentType.ReadOnly<CommercialCompanyData>(), ComponentType.ReadOnly<IndustrialCompanyData>() },
                None = new[] { ComponentType.ReadOnly<ExtractorCompanyData>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        // Registered immediately before PropertyProcessingSystem. The matching interval inherits its offset.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;

        protected override void OnGamePreload(LoadPurpose purpose, GameMode mode)
        {
            Cancel();
            base.OnGamePreload(purpose, mode);
        }

        internal void Start()
        {
            if (IsRunning) return;
            Cancel();
            IsRunning = true;
        }

        internal void Cancel()
        {
            Finish();
            _scanned = false;
            _cursor = _templateCursor = 0;
            Targets = Queued = Filled = Skipped = 0;
            Failed = false;
        }

        protected override void OnUpdate()
        {
            if (!IsRunning || Mod.Current?.CityEconomyReady != true || _simulation.selectedSpeed <= 0f
                || !_propertyProcessing.Enabled) return;

            try
            {
                EntityManager manager = EntityManager;
                // The rent processor consumes this queue and writes Renter buffers. Its queue producers
                // have separate handles, which GetRentActionQueue returns.
                manager.CompleteDependencyBeforeRO<Renter>();
                NativeQueue<RentAction> rents = _propertyProcessing.GetRentActionQueue(out var writers);
                writers.Complete();
                var pendingProperties = new HashSet<Entity>();
                var pendingCompanies = new HashSet<Entity>();
                using (var pending = rents.ToArray(Allocator.Temp))
                {
                    foreach (RentAction action in pending)
                    {
                        pendingProperties.Add(action.m_Property);
                        pendingCompanies.Add(action.m_Renter);
                    }
                }

                if (!_scanned) Scan(manager, pendingProperties);

                foreach (var assignment in _awaitingRent)
                {
                    if (HasCompany(manager, assignment.Property)) Filled++;
                    else Skipped++;
                    RetireUnusedCompany(manager, assignment.Company, pendingCompanies);
                }
                _awaitingRent.Clear();

                int stillStarting = 0;
                for (int i = 0; i < _starting.Count; i++)
                {
                    var assignment = _starting[i];
                    if (HasCompany(manager, assignment.Property))
                    {
                        Filled++;
                        RetireUnusedCompany(manager, assignment.Company, pendingCompanies);
                        continue;
                    }

                    // The game can submit a lease for either entity while the new company initializes.
                    // Preserve its order and avoid adding a second lease for the same renter or property.
                    if (pendingProperties.Contains(assignment.Property))
                    {
                        _awaitingRent.Add((assignment.Property, assignment.Company));
                        continue;
                    }

                    if (!TryGetVacancy(manager, assignment.Property, out Business business, out BuildingPropertyData propertyData)
                        || !manager.Exists(assignment.Company) || manager.HasComponent<Deleted>(assignment.Company)
                        || manager.HasComponent<Temp>(assignment.Company) || manager.HasComponent<PropertyRenter>(assignment.Company)
                        || pendingCompanies.Contains(assignment.Company)
                        || !manager.TryGetComponent(assignment.Company, out PrefabRef companyRef) || companyRef.m_Prefab != assignment.Template
                        || !TryGetTemplate(manager, assignment.Template, out Template template) || !template.Fits(business, propertyData))
                    {
                        Skipped++;
                        RetireUnusedCompany(manager, assignment.Company, pendingCompanies);
                        continue;
                    }

                    if (manager.HasComponent<Created>(assignment.Company)
                        || !manager.TryGetComponent(assignment.Company, out CompanyData companyData) || companyData.m_RandomSeed.state == 0)
                    {
                        // Modification5 runs once per rendered frame, outside the simulation steps.
                        if (assignment.Waits < MaxInitializationWaits)
                        {
                            assignment.Waits++;
                            _starting[stillStarting++] = assignment;
                        }
                        else
                        {
                            Skipped++;
                            RetireUnusedCompany(manager, assignment.Company, pendingCompanies);
                        }
                        continue;
                    }

                    rents.Enqueue(new RentAction
                    {
                        m_Property = assignment.Property,
                        m_Renter = assignment.Company,
                        m_Flags = business == Business.Storage ? RentActionFlags.Storage : default
                    });
                    _awaitingRent.Add((assignment.Property, assignment.Company));
                    pendingProperties.Add(assignment.Property);
                    pendingCompanies.Add(assignment.Company);
                    Queued++;
                }
                _starting.RemoveRange(stillStarting, _starting.Count - stillStarting);

                for (int scanned = 0; _cursor < _targets.Count && scanned < ScanSize && _starting.Count < BatchSize; scanned++)
                {
                    Entity property = _targets[_cursor++];
                    if (HasCompany(manager, property))
                    {
                        Filled++;
                        continue;
                    }
                    if (pendingProperties.Contains(property))
                    {
                        _awaitingRent.Add((property, Entity.Null));
                        continue;
                    }
                    if (!TryGetVacancy(manager, property, out Business business, out BuildingPropertyData propertyData)
                        || !ChooseTemplate(business, propertyData, out Template template)
                        || !TryGetTemplate(manager, template.Prefab, out template) || !template.Fits(business, propertyData))
                    {
                        Skipped++;
                        continue;
                    }

                    // The native spawn systems create this archetype and set only PrefabRef. Modification5
                    // initializes its company data before the next property-processing step.
                    Entity company = manager.CreateEntity(template.Archetype);
                    try
                    {
                        manager.SetComponentData(company, new PrefabRef { m_Prefab = template.Prefab });
                    }
                    catch
                    {
                        manager.DestroyEntity(company);
                        throw;
                    }
                    _starting.Add((property, company, template.Prefab, 0));
                }

                if (_cursor == _targets.Count && _starting.Count == 0 && _awaitingRent.Count == 0) Finish();
            }
            catch (Exception error)
            {
                Finish();
                Failed = true;
                LogManager.GetLogger("IndustryMaster").Error(error);
            }
        }

        private void Scan(EntityManager manager, HashSet<Entity> pendingProperties)
        {
            using (var prefabs = _companyPrefabs.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity prefab in prefabs)
                {
                    if (TryGetTemplate(manager, prefab, out Template template)) _templates.Add(template);
                }
            }
            using (var properties = _properties.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity property in properties)
                {
                    if (!pendingProperties.Contains(property) && TryGetVacancy(manager, property, out _, out _))
                        _targets.Add(property);
                }
            }
            Targets = _targets.Count;
            _scanned = true;
        }

        private static bool TryGetVacancy(EntityManager manager, Entity property, out Business business, out BuildingPropertyData data)
        {
            business = default;
            data = default;
            if (!manager.Exists(property) || !manager.HasComponent<PropertyOnMarket>(property)
                || manager.HasComponent<Created>(property) || manager.HasComponent<Deleted>(property)
                || manager.HasComponent<Destroyed>(property) || manager.HasComponent<Abandoned>(property)
                || manager.HasComponent<Condemned>(property) || manager.HasComponent<Temp>(property)
                || manager.HasComponent<UnderConstruction>(property) || manager.HasComponent<Disabled>(property)
                || manager.HasComponent<PropertyToBeOnMarket>(property) || manager.HasComponent<Placeholder>(property)
                || manager.HasComponent<Signature>(property) || manager.HasComponent<ResidentialProperty>(property)
                || manager.HasComponent<ExtractorProperty>(property) || manager.HasComponent<Game.Objects.OutsideConnection>(property)
                || manager.HasComponent<Game.Buildings.ServiceUpgrade>(property)
                || !manager.TryGetComponent(property, out Building building) || !manager.Exists(building.m_RoadEdge)
                || !manager.TryGetBuffer(property, true, out DynamicBuffer<Renter> renters) || renters.Length != 0
                || !manager.TryGetComponent(property, out PrefabRef prefab)
                || !manager.HasComponent<BuildingData>(prefab.m_Prefab)
                || !manager.TryGetComponent(prefab.m_Prefab, out SpawnableBuildingData spawnable)
                || !manager.TryGetComponent(spawnable.m_ZonePrefab, out ZoneData zone)
                || !manager.TryGetComponent(prefab.m_Prefab, out data) || data.m_ResidentialProperties != 0
                || data.CountProperties() != 1) return false;

            if (zone.m_AreaType == AreaType.Commercial)
            {
                business = Business.Commercial;
                return manager.HasComponent<CommercialProperty>(property) && data.m_AllowedSold != Resource.NoResource;
            }
            if (zone.m_AreaType != AreaType.Industrial || !manager.HasComponent<IndustrialProperty>(property)) return false;
            if (zone.IsOffice())
            {
                business = Business.Office;
                return manager.HasComponent<OfficeProperty>(property) && data.m_AllowedManufactured != Resource.NoResource;
            }
            if (manager.HasComponent<StorageProperty>(property))
            {
                business = Business.Storage;
                return data.m_AllowedStored != Resource.NoResource;
            }
            business = Business.Manufacturing;
            return !manager.HasComponent<OfficeProperty>(property) && data.m_AllowedManufactured != Resource.NoResource;
        }

        private static bool HasCompany(EntityManager manager, Entity property)
        {
            if (!manager.Exists(property) || manager.HasComponent<Deleted>(property)
                || !manager.TryGetBuffer(property, true, out DynamicBuffer<Renter> renters)) return false;

            foreach (Renter renter in renters)
            {
                Entity company = renter.m_Renter;
                if (manager.HasComponent<CompanyData>(company) && !manager.HasComponent<Deleted>(company)
                    && manager.TryGetComponent(company, out PropertyRenter lease) && lease.m_Property == property) return true;
            }
            return false;
        }

        private static bool TryGetTemplate(EntityManager manager, Entity prefab, out Template template)
        {
            template = default;
            if (!manager.Exists(prefab) || manager.HasComponent<Deleted>(prefab)
                || manager.HasComponent<Disabled>(prefab) || manager.HasComponent<ExtractorCompanyData>(prefab)
                || !manager.TryGetComponent(prefab, out ArchetypeData archetype) || !archetype.m_Archetype.Valid
                || !manager.TryGetComponent(prefab, out IndustrialProcessData process) || process.m_IsImport != 0) return false;

            Resource output = process.m_Output.m_Resource;
            Business business;
            if (manager.TryGetComponent(prefab, out StorageCompanyData storage))
            {
                if ((storage.m_StoredResources & output) != output) return false;
                business = Business.Storage;
            }
            else if (manager.HasComponent<CommercialCompanyData>(prefab))
            {
                if (!manager.HasComponent<ServiceCompanyData>(prefab)) return false;
                business = Business.Commercial;
            }
            else if (manager.HasComponent<IndustrialCompanyData>(prefab))
                business = EconomyUtils.IsOfficeResource(output) ? Business.Office : Business.Manufacturing;
            else return false;

            if (output == Resource.NoResource || output == Resource.Money) return false;

            template = new Template(prefab, archetype.m_Archetype, business, output,
                process.m_Input1.m_Resource | process.m_Input2.m_Resource);
            return true;
        }

        private static void RetireUnusedCompany(EntityManager manager, Entity company, HashSet<Entity> pendingCompanies)
        {
            if (!manager.Exists(company) || pendingCompanies.Contains(company) || manager.HasComponent<Deleted>(company)
                || !manager.HasComponent<CompanyData>(company) || manager.HasComponent<PropertyRenter>(company)
                || HasEntries<Employee>(manager, company) || HasEntries<OwnedVehicle>(manager, company)
                || HasEntries<GuestVehicle>(manager, company) || HasEntries<TripNeeded>(manager, company)) return;

            // Native company AI also uses Deleted for a propertyless company that is discarded.
            manager.AddComponent<Deleted>(company);
        }

        private static bool HasEntries<T>(EntityManager manager, Entity entity) where T : unmanaged, IBufferElementData =>
            manager.TryGetBuffer(entity, true, out DynamicBuffer<T> buffer) && buffer.Length != 0;

        private bool ChooseTemplate(Business business, BuildingPropertyData data, out Template result)
        {
            for (int i = 0; i < _templates.Count; i++)
            {
                int index = (_templateCursor + i) % _templates.Count;
                Template template = _templates[index];
                if (!template.Fits(business, data)) continue;
                _templateCursor = (index + 1) % _templates.Count;
                result = template;
                return true;
            }
            result = default;
            return false;
        }

        private void Finish()
        {
            IsRunning = false;
            _templates.Clear();
            _targets.Clear();
            _starting.Clear();
            _awaitingRent.Clear();
        }

        private readonly struct Template
        {
            public readonly Entity Prefab;
            public readonly EntityArchetype Archetype;
            private readonly Business _business;
            private readonly Resource _output, _input;

            public Template(Entity prefab, EntityArchetype archetype, Business business, Resource output, Resource input)
            {
                Prefab = prefab;
                Archetype = archetype;
                _business = business;
                _output = output;
                _input = input;
            }

            public bool Fits(Business business, BuildingPropertyData property)
            {
                if (business != _business || (_input & property.m_AllowedInput) != _input) return false;
                Resource allowed = business == Business.Commercial ? property.m_AllowedSold
                    : business == Business.Storage ? property.m_AllowedStored : property.m_AllowedManufactured;
                return (_output & allowed) == _output;
            }
        }
    }
}
