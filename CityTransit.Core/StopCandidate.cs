namespace CityTransit.Core;

internal sealed class StopCandidate
{
	public double S;

	public int Segment;

	public double Offset;

	public bool Forward;

	public Vec2 Position;

	public Vec2 Direction;

	public PlannedStop Existing;

	public int[] Reach;
}
