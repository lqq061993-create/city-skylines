using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Buildings;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace IndustryMaster
{
    public sealed class InventorySystem:GameSystemBase
    {protected override void OnUpdate(){Mod.Current?.TickInventory();}}
    internal sealed class InventoryRefill:IDisposable
    {
        private readonly World _world;
        private readonly EntityQuery _companies;
        private readonly HashSet<Resource> _physical;
        // One scratch set per controller. Tick runs on the game thread.
        private readonly Resource[] _kinds = new Resource[3];
        private readonly int[] _counts = new int[3];
        private readonly int[] _planned = new int[3];
        private readonly int[] _firstIndex = new int[3];
        private DateTime _next;
        internal long GeneratedKg,ChangedCompanies,Passes;
        internal InventoryRefill(World world)
        {
            _world=world;var em=world.EntityManager;
            _companies=em.CreateEntityQuery(new EntityQueryDesc{
                All=new[]{ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadWrite<Resources>(),ComponentType.ReadOnly<PropertyRenter>()},
                Any=new[]{ComponentType.ReadOnly<Game.Companies.CommercialCompany>(),ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),ComponentType.ReadOnly<Game.Companies.ExtractorCompany>()},
                None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Created>(),ComponentType.ReadOnly<Game.Tools.Temp>()}
            });
            _physical=new HashSet<Resource>();
            var resources=world.GetExistingSystemManaged<ResourceSystem>();
            var prefabs=resources.GetPrefabs();
            foreach(Resource r in Enum.GetValues(typeof(Resource)))
            {
                ulong bits=(ulong)r;if(bits==0||(bits&(bits-1))!=0||r==Resource.Money||r==Resource.Last)continue;
                Entity prefab=prefabs[r];
                if(prefab!=Entity.Null&&em.HasComponent<ResourceData>(prefab)&&em.GetComponentData<ResourceData>(prefab).m_Weight>0)_physical.Add(r);
            }
        }
        internal void Tick(MasterSettings settings)
        {
            if(settings.InventoryRefillMode==InventoryMode.Off||(!settings.RefillShops&&!settings.RefillFactories))return;
            var sim=_world.GetExistingSystemManaged<SimulationSystem>();if(sim==null||sim.selectedSpeed<=0)return;
            DateTime now=DateTime.UtcNow;if(now<_next)return;
            _next=now.AddSeconds(settings.InventoryRefillMode==InventoryMode.Full?.1:1);
            if(_companies.IsEmptyIgnoreFilter)return;
            var em=_world.EntityManager;
            em.CompleteDependencyBeforeRW<Resources>();
            bool full=settings.InventoryRefillMode==InventoryMode.Full;
            bool directImport = Mod.Current?.DirectFactoryImportsActive == true;
            bool directExport = Mod.Current?.DirectFactoryExportsActive == true;
            using(var entities=_companies.ToEntityArray(Allocator.Temp))
                foreach(var company in entities)
                {
                    bool shop=em.HasComponent<Game.Companies.CommercialCompany>(company);
                    if((shop&&!settings.RefillShops)||(!shop&&!settings.RefillFactories))continue;
                    Entity prefab=em.GetComponentData<PrefabRef>(company).m_Prefab;
                    if(!em.HasComponent<IndustrialProcessData>(prefab)||!em.HasComponent<StorageLimitData>(prefab))continue;
                    int capacity=em.GetComponentData<StorageLimitData>(prefab).m_Limit;if(capacity<=0)continue;
                    var process=em.GetComponentData<IndustrialProcessData>(prefab);
                    bool directFactory = !shop && (directImport || directExport) && _physical.Contains(process.m_Output.m_Resource)
                        && !em.HasComponent<Game.Companies.StorageCompany>(company) && !em.HasComponent<ServiceAvailable>(company);
                    if (directFactory && directImport && directExport) continue;
                    int kindCount=0;
                    Add(ref kindCount,process.m_Input1.m_Resource);
                    Add(ref kindCount,process.m_Input2.m_Resource);
                    Add(ref kindCount,process.m_Output.m_Resource);
                    if(kindCount==0)continue;

                    var goods=em.GetBuffer<Resources>(company,true);
                    for(int i=0;i<kindCount;i++)
                    {
                        _counts[i]=0;
                        _firstIndex[i]=-1;
                    }
                    long other=0;
                    for(int j=0;j<goods.Length;j++)
                    {
                        var item=goods[j];
                        int index=IndexOf(kindCount,item.m_Resource);
                        if(index<0)
                        {
                            if(_physical.Contains(item.m_Resource))other+=Math.Max(0,item.m_Amount);
                            continue;
                        }
                        _counts[index]=item.m_Amount;
                        if(_firstIndex[index]<0)_firstIndex[index]=j;
                    }
                    InventoryPolicy.FillTargets(capacity,_counts,kindCount,other,full,_planned);
                    bool changed=false;
                    for(int i=0;i<kindCount;i++)
                    {
                        if(_planned[i]<=_counts[i])continue;
                        Resource resource = _kinds[i];
                        if (directFactory && !DirectTradePolicy.ShouldRefill(directImport, directExport,
                            resource == process.m_Input1.m_Resource || resource == process.m_Input2.m_Resource,
                            resource == process.m_Output.m_Resource))
                            continue;
                        if(!changed)goods=em.GetBuffer<Resources>(company);
                        int index=_firstIndex[i];
                        if(index<0)goods.Add(new Resources{m_Resource=_kinds[i],m_Amount=_planned[i]});
                        else{var item=goods[index];item.m_Amount=_planned[i];goods[index]=item;}
                        GeneratedKg+=(long)_planned[i]-_counts[i];changed=true;
                    }
                    if(changed)ChangedCompanies++;
                }
            Passes++;
        }
        private void Add(ref int count,Resource resource)
        {
            if(_physical.Contains(resource)&&IndexOf(count,resource)<0)
                _kinds[count++]=resource;
        }
        private int IndexOf(int count,Resource resource)
        {
            for(int i=0;i<count;i++)
                if(_kinds[i]==resource)return i;
            return -1;
        }
        public void Dispose(){if(_world.IsCreated)_companies.Dispose();}
    }
}
