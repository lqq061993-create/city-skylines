using Unity.Entities;
using Unity.Mathematics;

namespace AutoTransitPlanner.Transit;

public sealed class StopStyle
{
	public Entity Prefab;

	public string Name;

	public int ThemeRank;

	public float Comfort;

	public float Loading;

	public float AccessDistance;

	public float BoardingTime;

	public uint Cost;

	public float3 Size;

	public bool Placeable;

	public override string ToString()
	{
		return string.Format("{0} ({1}, theme rank {2}, comfort {3:F2}, loading {4:F2}, access {5:F0} m, boarding {6:F1} s, cost {7}, size {8:F1}x{9:F1} m)", Name, Placeable ? "in toolbar" : "internal", ThemeRank, Comfort, Loading, AccessDistance, BoardingTime, Cost, Size.x, Size.z);
	}
}
