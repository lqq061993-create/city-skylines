namespace CityTransit.Core;

public readonly struct SnapResult
{
	public readonly int Segment;

	public readonly double Offset;

	public readonly double Distance;

	public bool Valid => Segment >= 0;

	public SnapResult(int segment, double offset, double distance)
	{
		Segment = segment;
		Offset = offset;
		Distance = distance;
	}
}
