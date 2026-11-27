using System.Collections.Generic;

namespace CityTransit.Core;

public sealed class CityModel
{
	public List<RoadSegment> Roads { get; } = new List<RoadSegment>();


	public List<DemandPoint> Residents { get; } = new List<DemandPoint>();


	public List<DemandPoint> Destinations { get; } = new List<DemandPoint>();


	public List<DemandPoint> Shops { get; } = new List<DemandPoint>();


	public List<DemandPoint> Leisure { get; } = new List<DemandPoint>();


	public List<Trip> Trips { get; } = new List<Trip>();


	public List<Vec2> ExistingStops { get; } = new List<Vec2>();

}
