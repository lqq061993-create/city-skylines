namespace CityTransit.Core;

public readonly struct DemandPoint
{
	public readonly Vec2 Position;

	public readonly double Weight;

	public DemandPoint(Vec2 position, double weight)
	{
		Position = position;
		Weight = weight;
	}
}
