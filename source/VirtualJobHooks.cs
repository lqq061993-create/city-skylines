using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Colossal.Logging;
using Game;
using Game.Serialization;
using Game.Simulation;
using Game.UI.InGame;
using HarmonyLib;
using Unity.Entities;

namespace IndustryMaster
{
    internal static class VirtualJobHooks
    {
        private static readonly ConditionalWeakTable<WorkerSystem, WorkQueries> Work = new ConditionalWeakTable<WorkerSystem, WorkQueries>();
        private static readonly ConditionalWeakTable<PayWageSystem, PayrollQuery> Payroll = new ConditionalWeakTable<PayWageSystem, PayrollQuery>();

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(Target(typeof(WorkerSystem), "OnUpdate"),
                prefix: Hook(nameof(BeforeWork)), finalizer: Hook(nameof(RestoreWorkQueries)));
            harmony.Patch(Target(typeof(PayWageSystem), "OnUpdate"), prefix: Hook(nameof(BeforePayroll)));
            harmony.Patch(Target(typeof(SerializerSystem), "OnUpdate"), prefix: Hook(nameof(BeforeSerialize)));
            harmony.Patch(Target(typeof(CitizenUIUtils), nameof(CitizenUIUtils.GetWorkplaceEntity)), postfix: Hook(nameof(HideSelfLink)));
            harmony.Patch(Target(typeof(CitizenUIUtils), nameof(CitizenUIUtils.GetCompanyEntity)), postfix: Hook(nameof(HideSelfLink)));
        }

        private static MethodInfo Target(Type type, string name) => AccessTools.DeclaredMethod(type, name)
            ?? throw new MissingMethodException(type.FullName, name);

        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(VirtualJobHooks), name);

        private static void BeforeWork(WorkerSystem __instance, ref EntityQuery ___m_GotoWorkQuery,
            ref EntityQuery ___m_WorkerQuery, out WorkQueries __state)
        {
            __state = null;
            if (!Work.TryGetValue(__instance, out WorkQueries queries))
            {
                queries = new WorkQueries(__instance.World.EntityManager, ___m_GotoWorkQuery, ___m_WorkerQuery);
                Work.Add(__instance, queries);
            }

            // Keep the native Worker queries for other callers and filter only the
            // two jobs scheduled by this simulation system.
            queries.NativeCommutes = ___m_GotoWorkQuery;
            queries.NativeWorkers = ___m_WorkerQuery;
            __state = queries;
            ___m_GotoWorkQuery = queries.PhysicalCommutes;
            ___m_WorkerQuery = queries.PhysicalWorkers;
        }

        private static void RestoreWorkQueries(ref EntityQuery ___m_GotoWorkQuery, ref EntityQuery ___m_WorkerQuery, WorkQueries __state)
        {
            if (__state == null) return;
            ___m_GotoWorkQuery = __state.NativeCommutes;
            ___m_WorkerQuery = __state.NativeWorkers;
        }

        private static void BeforePayroll(PayWageSystem __instance)
        {
            World world = __instance.World;
            PayrollQuery queries = Payroll.GetValue(__instance, system => new PayrollQuery(system.World));
            uint frame = queries.Simulation.frameIndex;
            bool fullScan = !queries.Scanned || unchecked(frame - queries.LastScan) >= 8192;
            VirtualJobs.RemoveJobs(world.EntityManager, fullScan ? queries.Rosters : queries.ExpiredRosters, releaseAll: false);
            if (!fullScan) return;
            queries.LastScan = frame;
            queries.Scanned = true;
        }

        private static void BeforeSerialize(SerializerSystem __instance)
        {
            try
            {
                World world = __instance.World;
                if (world.GetExistingSystemManaged<UpdateSystem>().currentPhase != SystemUpdatePhase.Serialize) return;
                EntityManager manager = world.EntityManager;
                // Save complete native employment pairs even if the last payroll scan
                // preceded retirement or enrollment in school.
                using var rosters = VirtualJobs.CreateRosterQuery(manager);
                VirtualJobs.RemoveJobs(manager, rosters, releaseAll: false);
            }
            catch (Exception error)
            {
                LogManager.GetLogger("IndustryMaster").Warn("Virtual job cleanup before saving failed: " + error);
            }
        }

        private static void HideSelfLink(EntityManager __0, Entity __1, ref Entity __result)
        {
            if (__result == __1 && __0.HasComponent<VirtualCitizenJob>(__1)) __result = Entity.Null;
        }

        // These queries live with their systems' EntityManager across city loads.
        private sealed class WorkQueries
        {
            internal readonly EntityQuery PhysicalCommutes, PhysicalWorkers;
            internal EntityQuery NativeCommutes, NativeWorkers;

            internal WorkQueries(EntityManager manager, EntityQuery commutes, EntityQuery workers)
            {
                PhysicalCommutes = ExcludeVirtual(manager, commutes);
                PhysicalWorkers = ExcludeVirtual(manager, workers);
            }

            private static EntityQuery ExcludeVirtual(EntityManager manager, EntityQuery original)
            {
                EntityQueryDesc[] descriptions = original.GetEntityQueryDescs();
                foreach (EntityQueryDesc description in descriptions)
                {
                    var exclusions = new ComponentType[description.None.Length + 1];
                    description.None.CopyTo(exclusions, 0);
                    exclusions[exclusions.Length - 1] = ComponentType.ReadOnly<VirtualCitizenJob>();
                    description.None = exclusions;
                }
                return manager.CreateEntityQuery(descriptions);
            }
        }

        private sealed class PayrollQuery
        {
            internal readonly EntityQuery Rosters, ExpiredRosters;
            internal readonly SimulationSystem Simulation;
            internal uint LastScan;
            internal bool Scanned;

            internal PayrollQuery(World world)
            {
                Simulation = world.GetExistingSystemManaged<SimulationSystem>();
                Rosters = VirtualJobs.CreateRosterQuery(world.EntityManager);
                ExpiredRosters = VirtualJobs.CreateExpiredRosterQuery(world.EntityManager);
            }
        }
    }
}
