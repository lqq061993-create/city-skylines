namespace CityTransit.Core;

public sealed class PlannedStop
{
	public int Id { get; set; }

	public int SegmentId { get; set; }

	public double Offset { get; set; }

	public Vec2 Position { get; set; }

	public Vec2 Direction { get; set; }

	public bool Forward { get; set; }

	public double CoveredWeight { get; set; }

	public int LineCount { get; set; }

	public bool Comfort { get; set; }
}
