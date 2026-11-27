namespace CityTransit.Core;

public readonly struct Trip
{
	public readonly Vec2 From;

	public readonly Vec2 To;

	public readonly double Weight;

	public Trip(Vec2 from, Vec2 to, double weight)
	{
		From = from;
		To = to;
		Weight = weight;
	}
}
