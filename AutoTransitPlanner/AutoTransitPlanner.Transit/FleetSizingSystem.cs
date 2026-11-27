using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Pathfind;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.UI.InGame;
using Unity.Entities;
using Unity.Mathematics;

namespace AutoTransitPlanner.Transit;

[CompilerGenerated]
public class FleetSizingSystem : GameSystemBase
{
	private sealed class LineJob
	{
		public Entity Line;

		public int Number;

		public int PlannedVehicles;

		public int Waited;

		public bool Applied;

		public int VerifyAt;
	}

	private const int CheckEvery = 20;

	private const int VerifyAfter = 60;

	private const int GiveUpAfter = 3000;

	private static readonly List<LineJob> Queue = new List<LineJob>();

	private PrefabSystem _prefabs;

	private PoliciesUISystem _policies;

	private EntityQuery _config;

	private Entity _vehicleCountPolicy;

	private int _ticks;

	public static void Enqueue(Entity line, int number, int plannedVehicles)
	{
		List<LineJob> queue = Queue;
		LineJob lineJob = new LineJob();
		lineJob.Line = line;
		lineJob.Number = number;
		lineJob.PlannedVehicles = plannedVehicles;
		queue.Add(lineJob);
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		_prefabs = base.World.GetOrCreateSystemManaged<PrefabSystem>();
		_policies = base.World.GetOrCreateSystemManaged<PoliciesUISystem>();
		_config = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
	}

	protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
	{
		base.OnGameLoadingComplete(purpose, mode);
		Queue.Clear();
		_vehicleCountPolicy = Entity.Null;
	}

	protected override void OnUpdate()
	{
		if (Queue.Count == 0 || ++_ticks % 20 != 0)
		{
			return;
		}
		for (int num = Queue.Count - 1; num >= 0; num--)
		{
			bool flag;
			try
			{
				flag = Step(Queue[num]);
			}
			catch (Exception exception)
			{
				Mod.Log.Error(exception);
				flag = true;
			}
			if (flag)
			{
				Queue.RemoveAt(num);
			}
		}
	}

	private bool Step(LineJob job)
	{
		job.Waited += 20;
		if (!base.EntityManager.Exists(job.Line) || base.EntityManager.HasComponent<Deleted>(job.Line))
		{
			return true;
		}
		if (!TryGetTravelTime(job.Line, out var duration, out var lineData))
		{
			if (job.Waited < 3000)
			{
				return false;
			}
			Mod.Log.Warn($"AutoTransit: line {job.Number}: travel time never became known, keeping the game's default bus count");
			return true;
		}
		float defaultVehicleInterval = lineData.m_DefaultVehicleInterval;
		if (!job.Applied)
		{
			int num = TransportLineSystem.CalculateVehicleCount(defaultVehicleInterval, duration);
			int num2 = Math.Max(1, job.PlannedVehicles);
			if (!TryComputeAdjustment(lineData, duration, num2, out var adjustment))
			{
				Mod.Log.Warn($"AutoTransit: line {job.Number}: vehicle count policy not available, keeping the game's default");
				return true;
			}
			_policies.SetPolicy(job.Line, _vehicleCountPolicy, active: true, adjustment);
			int fewest;
			int most;
			string arg = (TryGetAllowedCounts(lineData, duration, out fewest, out most) ? $"{fewest}-{most}" : "?");
			Mod.Log.Info($"AutoTransit: line {job.Number}: travel time {duration:F0}, game default would run {num} buses " + $"(the slider allows {arg}), planner suggests {job.PlannedVehicles}, setting {num2}");
			job.Applied = true;
			job.VerifyAt = job.Waited + 60;
			return false;
		}
		if (job.Waited < job.VerifyAt)
		{
			return false;
		}
		float value = defaultVehicleInterval;
		if (base.EntityManager.TryGetBuffer(job.Line, isReadOnly: true, out DynamicBuffer<RouteModifier> buffer))
		{
			RouteUtils.ApplyModifier(ref value, buffer, RouteModifierType.VehicleInterval);
		}
		Mod.Log.Info($"AutoTransit: line {job.Number}: the game now plans {TransportLineSystem.CalculateVehicleCount(value, duration)} buses");
		return true;
	}

	private bool TryGetTravelTime(Entity line, out float duration, out TransportLineData lineData)
	{
		duration = 0f;
		lineData = default(TransportLineData);
		EntityManager entityManager = base.EntityManager;
		if (!entityManager.HasComponent<PrefabRef>(line) || !entityManager.HasBuffer<RouteWaypoint>(line) || !entityManager.HasBuffer<RouteSegment>(line))
		{
			return false;
		}
		Entity prefab = entityManager.GetComponentData<PrefabRef>(line).m_Prefab;
		if (!entityManager.HasComponent<TransportLineData>(prefab))
		{
			return false;
		}
		lineData = entityManager.GetComponentData<TransportLineData>(prefab);
		DynamicBuffer<RouteWaypoint> buffer = entityManager.GetBuffer<RouteWaypoint>(line, isReadOnly: true);
		DynamicBuffer<RouteSegment> buffer2 = entityManager.GetBuffer<RouteSegment>(line, isReadOnly: true);
		int length = buffer.Length;
		if (length < 2 || buffer2.Length != length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < length; i++)
		{
			if (entityManager.HasComponent<VehicleTiming>(buffer[i].m_Waypoint))
			{
				num = i;
				break;
			}
		}
		float num2 = 0f;
		for (int j = 0; j < length; j++)
		{
			int index = (num + j) % length;
			int index2 = (num + j + 1) % length;
			if (!entityManager.TryGetComponent<PathInformation>(buffer2[index].m_Segment, out var component) ||
				(component.m_State & (PathFlags.Pending | PathFlags.Failed | PathFlags.Scheduled | PathFlags.Obsolete)) != 0 ||
				!(component.m_Distance >= 0f))
			{
				return false;
			}
			num2 += component.m_Duration;
			if (entityManager.HasComponent<VehicleTiming>(buffer[index2].m_Waypoint))
			{
				num2 += lineData.m_StopDuration;
			}
		}
		duration = num2;
		return num2 > 0f;
	}

	private bool TryComputeAdjustment(TransportLineData lineData, float duration, int count, out float adjustment)
	{
		adjustment = 0f;
		if (!EnsurePolicy())
		{
			return false;
		}
		EntityManager entityManager = base.EntityManager;
		DynamicBuffer<RouteModifierData> buffer = entityManager.GetBuffer<RouteModifierData>(_vehicleCountPolicy, isReadOnly: true);
		PolicySliderData componentData = entityManager.GetComponentData<PolicySliderData>(_vehicleCountPolicy);
		float defaultVehicleInterval = lineData.m_DefaultVehicleInterval;
		float num = TransportLineSystem.CalculateVehicleInterval(duration, count);
		for (int i = 0; i < buffer.Length; i++)
		{
			RouteModifierData modifierData = buffer[i];
			if (modifierData.m_Type == RouteModifierType.VehicleInterval)
			{
				RouteModifier modifier = default(RouteModifier);
				if (modifierData.m_Mode == ModifierValueMode.Absolute)
				{
					modifier.m_Delta.x = num - defaultVehicleInterval;
				}
				else
				{
					modifier.m_Delta.y = (num - defaultVehicleInterval) / defaultVehicleInterval;
				}
				float deltaFromModifier = RouteModifierInitializeSystem.RouteModifierRefreshData.GetDeltaFromModifier(modifier, modifierData);
				adjustment = RouteModifierInitializeSystem.RouteModifierRefreshData.GetPolicyAdjustmentFromModifierDelta(modifierData, deltaFromModifier, componentData);
				return true;
			}
		}
		return false;
	}

	private bool TryGetAllowedCounts(TransportLineData lineData, float duration, out int fewest, out int most)
	{
		fewest = (most = 0);
		if (!EnsurePolicy())
		{
			return false;
		}
		EntityManager entityManager = base.EntityManager;
		DynamicBuffer<RouteModifierData> buffer = entityManager.GetBuffer<RouteModifierData>(_vehicleCountPolicy, isReadOnly: true);
		PolicySliderData slider = entityManager.GetComponentData<PolicySliderData>(_vehicleCountPolicy);
		for (int i = 0; i < buffer.Length; i++)
		{
			RouteModifierData item = buffer[i];
			if (item.m_Type == RouteModifierType.VehicleInterval)
			{
				int val = CountAt(slider.m_Range.min);
				int val2 = CountAt(slider.m_Range.max);
				fewest = Math.Min(val, val2);
				most = Math.Max(val, val2);
				return true;
			}
			int CountAt(float adjustment)
			{
				float t = ((slider.m_Range.max == slider.m_Range.min) ? 0f : math.saturate((adjustment - slider.m_Range.min) / (slider.m_Range.max - slider.m_Range.min)));
				float delta = math.lerp(item.m_Range.min, item.m_Range.max, t);
				RouteModifier modifier = default(RouteModifier);
				RouteModifierInitializeSystem.RouteModifierRefreshData.AddModifierData(ref modifier, item, delta);
				float num = lineData.m_DefaultVehicleInterval + modifier.m_Delta.x;
				return TransportLineSystem.CalculateVehicleCount(num + num * modifier.m_Delta.y, duration);
			}
		}
		return false;
	}

	private bool EnsurePolicy()
	{
		if (_vehicleCountPolicy != Entity.Null)
		{
			return true;
		}
		if (_config.IsEmptyIgnoreFilter)
		{
			return false;
		}
		UITransportConfigurationPrefab singletonPrefab = _prefabs.GetSingletonPrefab<UITransportConfigurationPrefab>(_config);
		if (singletonPrefab.m_VehicleCountPolicy == null)
		{
			return false;
		}
		_vehicleCountPolicy = _prefabs.GetEntity(singletonPrefab.m_VehicleCountPolicy);
		if (_vehicleCountPolicy != Entity.Null && base.EntityManager.HasBuffer<RouteModifierData>(_vehicleCountPolicy))
		{
			return base.EntityManager.HasComponent<PolicySliderData>(_vehicleCountPolicy);
		}
		return false;
	}
}
