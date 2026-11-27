using System.Collections.Generic;
using CityTransit.Core;
using Unity.Entities;

namespace AutoTransitPlanner.Transit;

public sealed class CityScan
{
	public readonly CityModel Model = new CityModel();

	public readonly List<Entity> SegmentEntities = new List<Entity>();

	public Entity StopPrefab;

	public List<StopStyle> StopStyles = new List<StopStyle>();

	public List<string> IgnoredStopModels = new List<string>();

	public Entity LinePrefab;

	public int BusDepots;

	public int BusDepotCapacity;

	public int DepotCapacityEach;

	public int Commuters;

	public int ShopBuildings;

	public int LeisureBuildings;

	public int OneWaySegments;

	public bool Usable
	{
		get
		{
			if (Model.Roads.Count > 0 && Model.Residents.Count > 0 && StopPrefab != Entity.Null)
			{
				return LinePrefab != Entity.Null;
			}
			return false;
		}
	}
}
