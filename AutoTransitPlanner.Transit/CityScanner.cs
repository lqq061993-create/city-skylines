using System;
using System.Collections.Generic;
using System.Linq;
using CityTransit.Core;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AutoTransitPlanner.Transit;

public static class CityScanner
{
	private const int CurveSamples = 8;

	private const double MinRoadLength = 1.0;

	private const int MinCommutersForRealData = 25;

	public static CityScan Scan(EntityManager em, Entity theme)
	{
		CityScan cityScan = new CityScan();
		ScanRoads(em, cityScan);
		ScanPeople(em, cityScan);
		ScanAttractions(em, cityScan);
		ScanStations(em, cityScan);
		cityScan.StopStyles = FindBusStopStyles(em, theme, cityScan.IgnoredStopModels);
		cityScan.StopPrefab = ((cityScan.StopStyles.Count > 0) ? cityScan.StopStyles[0].Prefab : Entity.Null);
		cityScan.LinePrefab = FindBusLinePrefab(em);
		CountBusDepots(em, cityScan);
		return cityScan;
	}

	private static void ScanRoads(EntityManager em, CityScan scan)
	{
		EntityQuery entityQuery = em.CreateEntityQuery(new EntityQueryDesc
		{
			All = new ComponentType[5]
			{
				ComponentType.ReadOnly<Edge>(),
				ComponentType.ReadOnly<Curve>(),
				ComponentType.ReadOnly<Road>(),
				ComponentType.ReadOnly<PrefabRef>(),
				ComponentType.ReadOnly<Game.Net.SubLane>()
			},
			None = new ComponentType[3]
			{
				ComponentType.ReadOnly<Deleted>(),
				ComponentType.ReadOnly<Temp>(),
				ComponentType.ReadOnly<Owner>()
			}
		});
		List<(Entity, Edge, List<Vec2>, bool, bool, bool, bool, float, float)> list = new List<(Entity, Edge, List<Vec2>, bool, bool, bool, bool, float, float)>();
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item4 in nativeArray)
			{
				Entity prefab = em.GetComponentData<PrefabRef>(item4).m_Prefab;
				bool item = false;
				float item2 = 0f;
				float item3 = (em.HasComponent<NetGeometryData>(prefab) ? em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth : 0f);
				if (em.HasComponent<RoadData>(prefab))
				{
					RoadData componentData = em.GetComponentData<RoadData>(prefab);
					item = (componentData.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
					item2 = componentData.m_SpeedLimit;
				}
				Bezier4x3 bezier = em.GetComponentData<Curve>(item4).m_Bezier;
				if (DrivingDirections(em, item4, bezier, out var forward, out var backward))
				{
					List<Vec2> list2 = new List<Vec2>(9);
					for (int i = 0; i <= 8; i++)
					{
						float3 @float = MathUtils.Position(bezier, (float)i / 8f);
						list2.Add(new Vec2(@float.x, @float.z));
					}
					if (!(new RoadSegment(0, 0, 1, list2).Length < 1.0))
					{
						list.Add((item4, em.GetComponentData<Edge>(item4), list2, forward, backward, item, em.HasComponent<Game.Net.Elevation>(item4), item2, item3));
					}
				}
			}
		}
		List<float> list3 = (from r in list
			select r.Rest.Item1 into v
			where v > 0f
			orderby v
			select v).ToList();
		double num = ((list3.Count == 0) ? 0f : list3[list3.Count / 2]);
		foreach (var item5 in list)
		{
			double costFactor = ((num > 0.0 && item5.Rest.Item1 > 0f) ? Math.Max(0.5, Math.Min(2.5, num / (double)item5.Rest.Item1)) : 1.0);
			int count = scan.Model.Roads.Count;
			scan.Model.Roads.Add(new RoadSegment(count, item5.Item2.m_Start.Index, item5.Item2.m_End.Index, item5.Item3, item5.Item4, item5.Item5, !item5.Item6 && !item5.Item7, !item5.Item6, costFactor, item5.Rest.Item2));
			scan.SegmentEntities.Add(item5.Item1);
			if (item5.Item4 != item5.Item5)
			{
				scan.OneWaySegments++;
			}
		}
		entityQuery.Dispose();
	}

	private static bool DrivingDirections(EntityManager em, Entity edge, Bezier4x3 edgeCurve, out bool forward, out bool backward)
	{
		forward = false;
		backward = false;
		float3 @float = MathUtils.Tangent(edgeCurve, 0.5f);
		DynamicBuffer<Game.Net.SubLane> buffer = em.GetBuffer<Game.Net.SubLane>(edge, isReadOnly: true);
		for (int i = 0; i < buffer.Length; i++)
		{
			Entity subLane = buffer[i].m_SubLane;
			if (em.HasComponent<Game.Net.CarLane>(subLane) && em.HasComponent<Curve>(subLane) && !em.HasComponent<MasterLane>(subLane))
			{
				float3 float2 = MathUtils.Tangent(em.GetComponentData<Curve>(subLane).m_Bezier, 0.5f);
				if (float2.x * @float.x + float2.z * @float.z >= 0f)
				{
					forward = true;
				}
				else
				{
					backward = true;
				}
			}
		}
		return forward | backward;
	}

	private static void ScanPeople(EntityManager em, CityScan scan)
	{
		Dictionary<Entity, Vec2> positions = new Dictionary<Entity, Vec2>();
		Dictionary<Entity, int> dictionary = new Dictionary<Entity, int>();
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.ReadOnly<PropertyRenter>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				Entity property = em.GetComponentData<PropertyRenter>(item).m_Property;
				int length = em.GetBuffer<HouseholdCitizen>(item, isReadOnly: true).Length;
				if (length != 0)
				{
					dictionary.TryGetValue(property, out var value);
					dictionary[property] = value + length;
				}
			}
		}
		entityQuery.Dispose();
		Dictionary<(Entity, Entity), int> trips = new Dictionary<(Entity, Entity), int>();
		Dictionary<Entity, int> destinationCount = new Dictionary<Entity, int>();
		EntityQuery entityQuery2 = em.CreateEntityQuery(ComponentType.ReadOnly<Worker>(), ComponentType.ReadOnly<HouseholdMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray2 = entityQuery2.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item2 in nativeArray2)
			{
				CountTrip(item2, em.GetComponentData<Worker>(item2).m_Workplace);
			}
		}
		entityQuery2.Dispose();
		EntityQuery entityQuery3 = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Citizens.Student>(), ComponentType.ReadOnly<HouseholdMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray3 = entityQuery3.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item3 in nativeArray3)
			{
				CountTrip(item3, em.GetComponentData<Game.Citizens.Student>(item3).m_School);
			}
		}
		entityQuery3.Dispose();
		foreach (KeyValuePair<Entity, int> item4 in dictionary)
		{
			Vec2? vec = PositionOf(item4.Key);
			if (vec.HasValue)
			{
				scan.Model.Residents.Add(new DemandPoint(vec.Value, item4.Value));
			}
		}
		if (scan.Commuters >= 25)
		{
			foreach (KeyValuePair<Entity, int> item5 in destinationCount)
			{
				Vec2? vec2 = PositionOf(item5.Key);
				if (vec2.HasValue)
				{
					scan.Model.Destinations.Add(new DemandPoint(vec2.Value, item5.Value));
				}
			}
			{
				foreach (KeyValuePair<(Entity, Entity), int> item6 in trips)
				{
					Vec2? vec3 = PositionOf(item6.Key.Item1);
					Vec2? vec4 = PositionOf(item6.Key.Item2);
					if (vec3.HasValue && vec4.HasValue)
					{
						scan.Model.Trips.Add(new Trip(vec3.Value, vec4.Value, item6.Value));
					}
				}
				return;
			}
		}
		scan.Commuters = 0;
		AddBuildingBasedDemand(em, scan, PositionOf);
		Entity BuildingOf(Entity place)
		{
			if (place == Entity.Null || !em.Exists(place))
			{
				return Entity.Null;
			}
			if (em.HasComponent<PropertyRenter>(place))
			{
				return em.GetComponentData<PropertyRenter>(place).m_Property;
			}
			return place;
		}
		void CountTrip(Entity citizen, Entity place)
		{
			Entity entity = HomeOf(citizen);
			Entity entity2 = BuildingOf(place);
			if (!(entity == Entity.Null) && !(entity2 == Entity.Null) && !(entity == entity2))
			{
				trips.TryGetValue((entity, entity2), out var value4);
				trips[(entity, entity2)] = value4 + 1;
				destinationCount.TryGetValue(entity2, out var value5);
				destinationCount[entity2] = value5 + 1;
				scan.Commuters++;
			}
		}
		Entity HomeOf(Entity citizen)
		{
			if (!em.HasComponent<HouseholdMember>(citizen))
			{
				return Entity.Null;
			}
			Entity household = em.GetComponentData<HouseholdMember>(citizen).m_Household;
			if (household == Entity.Null || !em.Exists(household) || !em.HasComponent<PropertyRenter>(household))
			{
				return Entity.Null;
			}
			return em.GetComponentData<PropertyRenter>(household).m_Property;
		}
		Vec2? PositionOf(Entity building)
		{
			if (positions.TryGetValue(building, out var value2))
			{
				return value2;
			}
			if (building == Entity.Null || !em.Exists(building) || !em.HasComponent<Transform>(building))
			{
				return null;
			}
			float3 position = em.GetComponentData<Transform>(building).m_Position;
			Vec2 value3 = new Vec2(position.x, position.z);
			positions[building] = value3;
			return value3;
		}
	}

	private static void AddBuildingBasedDemand(EntityManager em, CityScan scan, Func<Entity, Vec2?> positionOf)
	{
		if (scan.Model.Residents.Count == 0)
		{
			EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<Renter>(), ComponentType.ReadOnly<ResidentialProperty>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
			using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
			{
				foreach (Entity item in nativeArray)
				{
					int length = em.GetBuffer<Renter>(item, isReadOnly: true).Length;
					Vec2? vec = positionOf(item);
					if (length > 0 && vec.HasValue)
					{
						scan.Model.Residents.Add(new DemandPoint(vec.Value, length * 3));
					}
				}
			}
			entityQuery.Dispose();
		}
		EntityQuery entityQuery2 = em.CreateEntityQuery(new EntityQueryDesc
		{
			All = new ComponentType[3]
			{
				ComponentType.ReadOnly<Building>(),
				ComponentType.ReadOnly<Transform>(),
				ComponentType.ReadOnly<Renter>()
			},
			None = new ComponentType[3]
			{
				ComponentType.ReadOnly<ResidentialProperty>(),
				ComponentType.ReadOnly<Deleted>(),
				ComponentType.ReadOnly<Temp>()
			}
		});
		using (NativeArray<Entity> nativeArray2 = entityQuery2.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item2 in nativeArray2)
			{
				int length2 = em.GetBuffer<Renter>(item2, isReadOnly: true).Length;
				Vec2? vec2 = positionOf(item2);
				if (length2 > 0 && vec2.HasValue)
				{
					scan.Model.Destinations.Add(new DemandPoint(vec2.Value, length2 * 8));
				}
			}
		}
		entityQuery2.Dispose();
	}

	private static void ScanAttractions(EntityManager em, CityScan scan)
	{
		Dictionary<Entity, double> dictionary = new Dictionary<Entity, double>();
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CommercialCompany>(), ComponentType.ReadOnly<PropertyRenter>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				Entity property = em.GetComponentData<PropertyRenter>(item).m_Property;
				double val = (em.HasBuffer<Employee>(item) ? em.GetBuffer<Employee>(item, isReadOnly: true).Length : 0);
				dictionary.TryGetValue(property, out var value);
				dictionary[property] = value + Math.Max(1.0, val);
			}
		}
		entityQuery.Dispose();
		foreach (KeyValuePair<Entity, double> item2 in dictionary)
		{
			Vec2? vec = PositionOf(item2.Key);
			if (vec.HasValue)
			{
				scan.Model.Shops.Add(new DemandPoint(vec.Value, item2.Value));
				scan.ShopBuildings++;
			}
		}
		Dictionary<Entity, double> dictionary2 = new Dictionary<Entity, double>();
		EntityQuery entityQuery2 = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.LeisureProvider>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray2 = entityQuery2.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item3 in nativeArray2)
			{
				Entity prefab = em.GetComponentData<PrefabRef>(item3).m_Prefab;
				double val2 = ((!em.HasComponent<LeisureProviderData>(prefab)) ? 1 : em.GetComponentData<LeisureProviderData>(prefab).m_Efficiency);
				Entity key = BuildingOf(item3);
				dictionary2.TryGetValue(key, out var value2);
				dictionary2[key] = value2 + Math.Max(1.0, val2);
			}
		}
		entityQuery2.Dispose();
		EntityQuery entityQuery3 = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.Park>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray3 = entityQuery3.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item4 in nativeArray3)
			{
				Entity prefab2 = em.GetComponentData<PrefabRef>(item4).m_Prefab;
				double val3 = (em.HasComponent<AttractionData>(prefab2) ? em.GetComponentData<AttractionData>(prefab2).m_Attractiveness : 0);
				dictionary2.TryGetValue(item4, out var value3);
				dictionary2[item4] = value3 + Math.Max(1.0, val3);
			}
		}
		entityQuery3.Dispose();
		double num = ((dictionary2.Count == 0) ? 1.0 : dictionary2.Values.Average());
		foreach (KeyValuePair<Entity, double> item5 in dictionary2)
		{
			Vec2? vec2 = PositionOf(item5.Key);
			if (vec2.HasValue)
			{
				scan.Model.Leisure.Add(new DemandPoint(vec2.Value, item5.Value / num));
				scan.LeisureBuildings++;
			}
		}
		Entity BuildingOf(Entity place)
		{
			if (place == Entity.Null || !em.Exists(place))
			{
				return Entity.Null;
			}
			if (em.HasComponent<PropertyRenter>(place))
			{
				return em.GetComponentData<PropertyRenter>(place).m_Property;
			}
			return place;
		}
		Vec2? PositionOf(Entity building)
		{
			if (building == Entity.Null || !em.Exists(building) || !em.HasComponent<Transform>(building))
			{
				return null;
			}
			float3 position = em.GetComponentData<Transform>(building).m_Position;
			return new Vec2(position.x, position.z);
		}
	}

	private static void ScanStations(EntityManager em, CityScan scan)
	{
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Owner>());
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				if (em.HasComponent<Deleted>(item) || em.HasComponent<Temp>(item))
				{
					continue;
				}
				Entity prefab = em.GetComponentData<PrefabRef>(item).m_Prefab;
				if (em.HasComponent<TransportStopData>(prefab))
				{
					TransportStopData componentData = em.GetComponentData<TransportStopData>(prefab);
					if (componentData.m_TransportType == TransportType.Bus && componentData.m_PassengerTransport)
					{
						float3 position = em.GetComponentData<Transform>(item).m_Position;
						scan.Model.ExistingStops.Add(new Vec2(position.x, position.z));
					}
				}
			}
		}
		entityQuery.Dispose();
	}

	private static void CountBusDepots(EntityManager em, CityScan scan)
	{
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.TransportDepot>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				Entity prefab = em.GetComponentData<PrefabRef>(item).m_Prefab;
				if (em.HasComponent<TransportDepotData>(prefab))
				{
					TransportDepotData componentData = em.GetComponentData<TransportDepotData>(prefab);
					if (componentData.m_TransportType == TransportType.Bus)
					{
						scan.BusDepots++;
						scan.BusDepotCapacity += componentData.m_VehicleCapacity;
					}
				}
			}
		}
		entityQuery.Dispose();
		if (scan.BusDepots > 0)
		{
			scan.DepotCapacityEach = scan.BusDepotCapacity / scan.BusDepots;
			return;
		}
		EntityQuery entityQuery2 = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<TransportDepotData>(), ComponentType.ReadOnly<BuildingData>());
		using (NativeArray<Entity> nativeArray2 = entityQuery2.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item2 in nativeArray2)
			{
				TransportDepotData componentData2 = em.GetComponentData<TransportDepotData>(item2);
				if (componentData2.m_TransportType == TransportType.Bus && componentData2.m_VehicleCapacity > scan.DepotCapacityEach)
				{
					scan.DepotCapacityEach = componentData2.m_VehicleCapacity;
				}
			}
		}
		entityQuery2.Dispose();
	}

	public static List<Entity> FindRoadsideBusStops(EntityManager em)
	{
		List<Entity> list = new List<Entity>();
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>());
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				if (em.HasComponent<Deleted>(item) || em.HasComponent<Temp>(item) || em.HasComponent<Owner>(item))
				{
					continue;
				}
				Entity prefab = em.GetComponentData<PrefabRef>(item).m_Prefab;
				if (em.HasComponent<TransportStopData>(prefab))
				{
					TransportStopData componentData = em.GetComponentData<TransportStopData>(prefab);
					if (componentData.m_TransportType == TransportType.Bus && componentData.m_PassengerTransport)
					{
						list.Add(item);
					}
				}
			}
		}
		entityQuery.Dispose();
		return list;
	}

	public static List<Entity> FindBusLines(EntityManager em)
	{
		List<Entity> list = new List<Entity>();
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<PrefabRef>());
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				if (em.HasComponent<Deleted>(item) || em.HasComponent<Temp>(item))
				{
					continue;
				}
				Entity prefab = em.GetComponentData<PrefabRef>(item).m_Prefab;
				if (em.HasComponent<TransportLineData>(prefab))
				{
					TransportLineData componentData = em.GetComponentData<TransportLineData>(prefab);
					if (componentData.m_TransportType == TransportType.Bus && componentData.m_PassengerTransport)
					{
						list.Add(item);
					}
				}
			}
		}
		entityQuery.Dispose();
		return list;
	}

	public static List<StopStyle> FindBusStopStyles(EntityManager em, Entity theme, List<string> ignored = null)
	{
		EntityQuery entityQuery = em.CreateEntityQuery(new EntityQueryDesc
		{
			All = new ComponentType[3]
			{
				ComponentType.ReadOnly<PrefabData>(),
				ComponentType.ReadOnly<TransportStopData>(),
				ComponentType.ReadOnly<ObjectData>()
			},
			None = new ComponentType[1] { ComponentType.ReadOnly<BuildingData>() }
		});
		PrefabSystem existingSystemManaged = em.World.GetExistingSystemManaged<PrefabSystem>();
		List<StopStyle> list = new List<StopStyle>();
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				TransportStopData componentData = em.GetComponentData<TransportStopData>(item);
				if (componentData.m_TransportType == TransportType.Bus && componentData.m_PassengerTransport)
				{
					StopStyle obj = new StopStyle
					{
						Prefab = item
					};
					string text = existingSystemManaged.GetPrefabName(item);
					if (text == null)
					{
						int index = item.Index;
						text = index.ToString();
					}
					obj.Name = text;
					obj.ThemeRank = ThemeRank(em, item, theme);
					obj.Comfort = componentData.m_ComfortFactor;
					obj.Loading = componentData.m_LoadingFactor;
					obj.AccessDistance = componentData.m_AccessDistance;
					obj.BoardingTime = componentData.m_BoardingTime;
					obj.Placeable = em.HasComponent<UIObjectData>(item) && em.GetComponentData<UIObjectData>(item).m_Group != Entity.Null;
					obj.Cost = (em.HasComponent<PlaceableObjectData>(item) ? em.GetComponentData<PlaceableObjectData>(item).m_ConstructionCost : 0u);
					obj.Size = (em.HasComponent<ObjectGeometryData>(item) ? em.GetComponentData<ObjectGeometryData>(item).m_Size : default(float3));
					list.Add(obj);
				}
			}
		}
		entityQuery.Dispose();
		if (list.Any((StopStyle x) => x.Placeable))
		{
			ignored?.AddRange((from x in list
				where !x.Placeable
				select x.Name).OrderBy((string x) => x, StringComparer.Ordinal));
			list.RemoveAll((StopStyle x) => !x.Placeable);
		}
		return list.OrderBy((StopStyle x) => x.ThemeRank).ThenBy((StopStyle x) => x.Name, StringComparer.Ordinal).ToList();
	}

	private static int ThemeRank(EntityManager em, Entity prefab, Entity theme)
	{
		if (!em.HasBuffer<ObjectRequirementElement>(prefab))
		{
			return 1;
		}
		DynamicBuffer<ObjectRequirementElement> buffer = em.GetBuffer<ObjectRequirementElement>(prefab, isReadOnly: true);
		if (buffer.Length == 0)
		{
			return 1;
		}
		for (int i = 0; i < buffer.Length; i++)
		{
			if (buffer[i].m_Requirement == theme)
			{
				return 0;
			}
		}
		return 2;
	}

	public static Entity FindBusLinePrefab(EntityManager em)
	{
		EntityQuery entityQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<TransportLineData>(), ComponentType.ReadOnly<RouteData>());
		Entity result = Entity.Null;
		using (NativeArray<Entity> nativeArray = entityQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity item in nativeArray)
			{
				TransportLineData componentData = em.GetComponentData<TransportLineData>(item);
				if (componentData.m_TransportType == TransportType.Bus && componentData.m_PassengerTransport)
				{
					result = item;
					break;
				}
			}
		}
		entityQuery.Dispose();
		return result;
	}
}
