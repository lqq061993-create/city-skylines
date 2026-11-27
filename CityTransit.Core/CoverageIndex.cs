using System;
using System.Collections.Generic;
using System.Linq;

namespace CityTransit.Core;

internal sealed class CoverageIndex
{
	private readonly RoadNetwork _net;

	private readonly ZoneModel _zones;

	private readonly double _radius;

	private readonly Dictionary<(int, int), int[]> _cache = new Dictionary<(int, int), int[]>();

	public CoverageIndex(RoadNetwork net, ZoneModel zones, double radius)
	{
		_net = net;
		_zones = zones;
		_radius = radius;
	}

	public int[] Reach(int segment, double offset)
	{
		(int, int) key = (segment, (int)Math.Round(offset / 15.0));
		if (_cache.TryGetValue(key, out var value))
		{
			return value;
		}
		Dictionary<int, double> found = new Dictionary<int, double>();
		if (_zones.BySegment.TryGetValue(segment, out var value2))
		{
			foreach (int item in value2)
			{
				Consider(item, Math.Abs(_zones.Zones[item].Snap.Offset - offset));
			}
		}
		foreach (KeyValuePair<int, double> item2 in _net.WalkNodes(segment, offset, _radius))
		{
			foreach (var (num, flag) in _net.SegmentsAt(item2.Key))
			{
				if (!_zones.BySegment.TryGetValue(num, out var value3))
				{
					continue;
				}
				double length = _net.Segments[num].Length;
				foreach (int item3 in value3)
				{
					double num2 = (flag ? _zones.Zones[item3].Snap.Offset : (length - _zones.Zones[item3].Snap.Offset));
					Consider(item3, item2.Value + num2);
				}
			}
		}
		int[] array = found.Keys.OrderBy((int k) => k).ToArray();
		_cache[key] = array;
		return array;
		void Consider(int zoneId, double walk)
		{
			double num3 = walk + _zones.Zones[zoneId].Snap.Distance;
			if (!(num3 > _radius) && (!found.TryGetValue(zoneId, out var value4) || num3 < value4))
			{
				found[zoneId] = num3;
			}
		}
	}
}
