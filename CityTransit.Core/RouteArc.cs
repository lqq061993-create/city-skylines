namespace CityTransit.Core;

public readonly struct RouteArc
{
	public readonly int Segment;

	public readonly bool Forward;

	public RouteArc(int segment, bool forward)
	{
		Segment = segment;
		Forward = forward;
	}
}
