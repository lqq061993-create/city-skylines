namespace CityTransit.Core;

internal sealed class Zone
{
	public int Id;

	public Vec2 Center;

	public double Residents;

	public double Destinations;

	public double ShopAppeal;

	public double LeisureAppeal;

	public SnapResult Snap;

	public int AccessNode = -1;

	public double Value;

	public bool Covered;

	public double Weight => Residents + Destinations;
}
