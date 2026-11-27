using System;
using System.Collections.Generic;
using Colossal.Entities;
using Game.Agents;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace IndustryMaster
{
    // Rebuilt from the native Worker and Employee data after deserialization.
    internal struct VirtualCitizenJob : IComponentData { }

    internal sealed class VirtualJobs : IDisposable
    {
        private readonly World _world;
        private readonly EntityQuery _candidates;
        private readonly EntityQuery _rosters;
        private readonly EntityQuery _seekers;

        internal VirtualJobs(World world)
        {
            _world = world;
            EntityManager manager = world.EntityManager;
            _candidates = manager.CreateEntityQuery(
                ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<HouseholdMember>(),
                ComponentType.Exclude<Worker>(), ComponentType.Exclude<Employee>(),
                ComponentType.Exclude<VirtualCitizenJob>(),
                ComponentType.Exclude<Student>(), ComponentType.Exclude<HasSchoolSeeker>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            _rosters = CreateRosterQuery(manager);
            _seekers = manager.CreateEntityQuery(ComponentType.ReadOnly<JobSeeker>(), ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
        }

        internal int Assign()
        {
            EntityManager manager = _world.EntityManager;
            using var candidates = _candidates.ToEntityArray(Allocator.TempJob);
            using var commands = new EntityCommandBuffer(Allocator.TempJob);
            var hired = new HashSet<Entity>();
            foreach (Entity person in candidates)
            {
                Citizen citizen = manager.GetComponentData<Citizen>(person);
                if (!IsResidentWorker(manager, person, citizen) || HasWorkTrip(manager, person)) continue;

                byte level = (byte)citizen.GetEducationLevel();
                // A citizen is the save-persistent employer for their own virtual job.
                // Native payroll matches the employee against this one-entry roster.
                commands.AddBuffer<Employee>(person).Add(new Employee { m_Worker = person, m_Level = level });
                commands.AddComponent(person, new Worker { m_Workplace = person, m_Level = level, m_Shift = Workshift.Day });
                commands.AddComponent<VirtualCitizenJob>(person);
                citizen.m_UnemploymentCounter = 0;
                citizen.m_UnemploymentTimeCounter = 0;
                commands.SetComponent(person, citizen);
                if (manager.TryGetComponent(person, out HasJobSeeker seeker))
                {
                    seeker.m_Seeker = Entity.Null;
                    commands.SetComponent(person, seeker);
                    commands.SetComponentEnabled<HasJobSeeker>(person, false);
                }
                hired.Add(person);
            }

            if (hired.Count == 0) return 0;
            using var seekers = _seekers.ToEntityArray(Allocator.TempJob);
            foreach (Entity request in seekers)
            {
                if (hired.Contains(manager.GetComponentData<Owner>(request).m_Owner))
                    commands.AddComponent<Deleted>(request);
            }
            commands.Playback(manager);
            return hired.Count;
        }

        internal int Release() => RemoveJobs(_world.EntityManager, _rosters, releaseAll: true);

        internal static EntityQuery CreateRosterQuery(EntityManager manager) => manager.CreateEntityQuery(
            ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<VirtualCitizenJob>(),
            ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());

        internal static EntityQuery CreateExpiredRosterQuery(EntityManager manager)
        {
            var required = new[] { ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<VirtualCitizenJob>() };
            var deletedOrTemporary = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() };
            return manager.CreateEntityQuery(
                new EntityQueryDesc { All = required, None = deletedOrTemporary, Any = new[] { ComponentType.ReadOnly<HealthProblem>(), ComponentType.ReadOnly<Student>() } },
                new EntityQueryDesc { All = required, None = new[] { deletedOrTemporary[0], deletedOrTemporary[1], ComponentType.ReadOnly<Worker>() } },
                new EntityQueryDesc { All = required, None = new[] { deletedOrTemporary[0], deletedOrTemporary[1], ComponentType.ReadOnly<Employee>() } });
        }

        internal static void RestoreSavedJobs(World world)
        {
            EntityManager manager = world.EntityManager;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<Employee>(),
                ComponentType.Exclude<VirtualCitizenJob>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            if (query.IsEmptyIgnoreFilter) return;

            using var rosters = query.ToEntityArray(Allocator.TempJob);
            using var commands = new EntityCommandBuffer(Allocator.TempJob);
            foreach (Entity person in rosters)
            {
                DynamicBuffer<Employee> employees = manager.GetBuffer<Employee>(person, isReadOnly: true);
                if (employees.Length != 1 || employees[0].m_Worker != person) continue;
                if (manager.TryGetComponent(person, out Worker worker) && worker.m_Workplace == person)
                    commands.AddComponent<VirtualCitizenJob>(person);
            }
            commands.Playback(manager);
        }

        internal static int RemoveJobs(EntityManager manager, EntityQuery query, bool releaseAll)
        {
            if (query.IsEmptyIgnoreFilter) return 0;
            using var rosters = query.ToEntityArray(Allocator.TempJob);
            using var commands = new EntityCommandBuffer(Allocator.TempJob);
            int removed = 0;
            bool changed = false;
            foreach (Entity person in rosters)
            {
                bool virtualWorker = manager.TryGetComponent(person, out Worker worker) && worker.m_Workplace == person;
                bool hasRoster = manager.HasBuffer<Employee>(person);
                DynamicBuffer<Employee> employees = hasRoster ? manager.GetBuffer<Employee>(person, isReadOnly: true) : default;
                bool selfRoster = hasRoster && employees.Length == 1 && employees[0].m_Worker == person;
                if (!releaseAll && virtualWorker && selfRoster && !manager.HasComponent<Student>(person)
                    && IsResidentWorker(manager, person, manager.GetComponentData<Citizen>(person))) continue;

                if (virtualWorker)
                {
                    commands.RemoveComponent<Worker>(person);
                    if (manager.TryGetComponent(person, out HasJobSeeker seeker)
                        && !manager.IsComponentEnabled<HasJobSeeker>(person))
                    {
                        seeker.m_LastJobSeekFrameIndex = 0;
                        commands.SetComponent(person, seeker);
                    }
                    removed++;
                }
                if (hasRoster) RemoveRoster(commands, person, employees);
                commands.RemoveComponent<VirtualCitizenJob>(person);
                changed = true;
            }
            if (changed) commands.Playback(manager);
            return removed;
        }

        private static void RemoveRoster(EntityCommandBuffer commands, Entity person, DynamicBuffer<Employee> employees)
        {
            // School enrollment removes the employee entry before removing Worker.
            if (employees.Length == 0 || (employees.Length == 1 && employees[0].m_Worker == person))
            {
                commands.RemoveComponent<Employee>(person);
                return;
            }

            // Keep any entries that another mod may have added to an active roster.
            for (int i = 0; i < employees.Length; i++)
            {
                if (employees[i].m_Worker != person) continue;
                DynamicBuffer<Employee> kept = commands.SetBuffer<Employee>(person);
                for (int j = 0; j < employees.Length; j++)
                {
                    if (employees[j].m_Worker != person) kept.Add(employees[j]);
                }
                return;
            }
        }

        private static bool IsResidentWorker(EntityManager manager, Entity person, Citizen citizen)
        {
            if ((citizen.m_State & (CitizenFlags.ValidCitizen | CitizenFlags.MovingAwayReachOC)) != CitizenFlags.ValidCitizen
                || !CitizenUtils.IsWorkableCitizen(citizen, citizen.GetAge(), isStudent: false, CitizenUtils.IsDead(manager, person))
                || !manager.TryGetComponent(person, out HouseholdMember member)) return false;

            Entity home = member.m_Household;
            return manager.TryGetComponent(home, out Household household)
                && (household.m_Flags & (HouseholdFlags.MovedIn | HouseholdFlags.Commuter | HouseholdFlags.Tourist)) == HouseholdFlags.MovedIn
                && !manager.HasComponent<MovingAway>(home) && !manager.HasComponent<Deleted>(home);
        }

        private static bool HasWorkTrip(EntityManager manager, Entity person)
        {
            if (manager.TryGetComponent(person, out TravelPurpose purpose) && IsWork(purpose.m_Purpose)) return true;
            if (!manager.HasBuffer<TripNeeded>(person)) return false;
            DynamicBuffer<TripNeeded> trips = manager.GetBuffer<TripNeeded>(person, isReadOnly: true);
            for (int i = 0; i < trips.Length; i++)
            {
                if (IsWork(trips[i].m_Purpose)) return true;
            }
            return false;
        }

        private static bool IsWork(Purpose purpose) => purpose == Purpose.GoingToWork || purpose == Purpose.Working;

        public void Dispose()
        {
            if (!_world.IsCreated) return;
            _candidates.Dispose();
            _rosters.Dispose();
            _seekers.Dispose();
        }
    }
}
