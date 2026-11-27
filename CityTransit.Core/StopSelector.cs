using System;
using System.Collections.Generic;
using System.Linq;

namespace CityTransit.Core;

internal sealed class StopSelector
{
	private readonly RoadNetwork _net;

	private readonly ZoneModel _zones;

	private readonly CoverageIndex _coverage;

	private readonly PlannerSettings _s;

	private readonly Dictionary<(int, bool), List<PlannedStop>> _registry;

	private readonly int[] _mark;

	private int _stamp;

	public StopSelector(RoadNetwork net, ZoneModel zones, CoverageIndex coverage, PlannerSettings s, Dictionary<(int, bool), List<PlannedStop>> registry)
	{
		_net = net;
		_zones = zones;
		_coverage = coverage;
		_s = s;
		_registry = registry;
		_mark = new int[zones.Zones.Count];
	}

	public List<StopCandidate> BuildCandidates(IReadOnlyList<RouteArc> route, out double total)
	{
		List<StopCandidate> list = new List<StopCandidate>();
		double num = 0.0;
		foreach (RouteArc item2 in route)
		{
			RoadSegment roadSegment = _net.Segments[item2.Segment];
			double length = roadSegment.Length;
			double num2 = length - 2.0 * _s.IntersectionClearance;
			if (!_net.IsCoreSegment(item2.Segment))
			{
				// Never put a stop on a road the bus can only enter and leave by turning around.
				num += length;
				continue;
			}
			if (roadSegment.AllowStops && num2 >= 0.0)
			{
				int num3 = Math.Max(1, (int)Math.Floor(num2 / _s.CandidateSpacing) + 1);
				for (int i = 0; i < num3; i++)
				{
					double num4 = ((num3 == 1) ? (length / 2.0) : (_s.IntersectionClearance + num2 * (double)i / (double)(num3 - 1)));
					double offset = (item2.Forward ? num4 : (length - num4));
					StopCandidate item = MakeCandidate(item2, num + num4, offset);
					list.Add(item);
				}
			}
			num += length;
		}
		total = num;
		return list;
	}

	private StopCandidate MakeCandidate(RouteArc arc, double s, double offset)
	{
		Vec2 direction;
		Vec2 position = _net.Segments[arc.Segment].PointAt(offset, out direction);
		StopCandidate stopCandidate = new StopCandidate
		{
			S = s,
			Segment = arc.Segment,
			Offset = offset,
			Forward = arc.Forward,
			Position = position,
			Direction = (arc.Forward ? direction : (-direction))
		};
		if (_registry.TryGetValue((arc.Segment, arc.Forward), out var value))
		{
			PlannedStop plannedStop = null;
			double num = _s.StopShareDistance;
			foreach (PlannedStop item in value)
			{
				double num2 = Math.Abs(item.Offset - offset);
				if (num2 <= num)
				{
					num = num2;
					plannedStop = item;
				}
			}
			if (plannedStop != null)
			{
				stopCandidate.Existing = plannedStop;
				stopCandidate.Offset = plannedStop.Offset;
				stopCandidate.Position = plannedStop.Position;
				stopCandidate.Direction = plannedStop.Direction;
			}
		}
		stopCandidate.Reach = _coverage.Reach(stopCandidate.Segment, stopCandidate.Offset);
		return stopCandidate;
	}

	private double NewValue(StopCandidate c, StopCandidate prev)
	{
		_stamp++;
		int[] reach;
		if (prev != null)
		{
			reach = prev.Reach;
			foreach (int num in reach)
			{
				_mark[num] = _stamp;
			}
		}
		double num2 = 0.0;
		reach = c.Reach;
		foreach (int num3 in reach)
		{
			if (_mark[num3] != _stamp)
			{
				num2 += _zones.Zones[num3].Value;
			}
		}
		return num2;
	}

	private double Cost(StopCandidate c)
	{
		if (c.Existing == null)
		{
			return _s.StopCost;
		}
		return 0.0;
	}

	public StopSelection Select(IReadOnlyList<RouteArc> route)
	{
		double total;
		List<StopCandidate> list = BuildCandidates(route, out total);
		StopSelection stopSelection = new StopSelection
		{
			RouteLength = total
		};
		int count = list.Count;
		if (count < _s.MinStopsPerLine)
		{
			return null;
		}
		double[] array = new double[count];
		int[] array2 = new int[count];
		int[] array3 = new int[count];
		int[] array4 = new int[count];
		for (int i = 0; i < count; i++)
		{
			array[i] = NewValue(list[i], null) - Cost(list[i]);
			array2[i] = -1;
			array3[i] = i;
			array4[i] = 1;
			bool flag = false;
			for (int num = i - 1; num >= 0; num--)
			{
				double num2 = list[i].S - list[num].S;
				if (!(num2 < _s.MinStopSpacing))
				{
					if (num2 > _s.MaxStopSpacing && flag)
					{
						break;
					}
					double num3 = array[num] + NewValue(list[i], list[num]) - Cost(list[i]);
					if (array4[num] + 1 <= _s.MaxStopsPerLine && num3 > array[i])
					{
						array[i] = num3;
						array2[i] = num;
						array3[i] = array3[num];
						array4[i] = array4[num] + 1;
					}
					flag = true;
					if (num2 > _s.MaxStopSpacing)
					{
						break;
					}
				}
			}
		}
		int num4 = -1;
		double num5 = double.MinValue;
		for (int j = 0; j < count; j++)
		{
			if (array4[j] >= _s.MinStopsPerLine && !(total - list[j].S + list[array3[j]].S < _s.MinStopSpacing * 0.6) && array[j] > num5)
			{
				num5 = array[j];
				num4 = j;
			}
		}
		if (num4 < 0)
		{
			return null;
		}
		List<StopCandidate> list2 = new List<StopCandidate>();
		for (int num6 = num4; num6 >= 0; num6 = array2[num6])
		{
			list2.Add(list[num6]);
		}
		list2.Reverse();
		HashSet<PlannedStop> seenStops = new HashSet<PlannedStop>();
		list2 = list2.Where((StopCandidate c) => c.Existing == null || seenStops.Add(c.Existing)).ToList();
		if (list2.Count < _s.MinStopsPerLine)
		{
			return null;
		}
		stopSelection.Chosen = list2;
		double num7 = 0.0;
		for (int k = 0; k < list2.Count; k++)
		{
			double num8 = ((k + 1 < list2.Count) ? (list2[k + 1].S - list2[k].S) : (total - list2[k].S + list2[0].S));
			if (num8 > _s.MaxStopSpacing)
			{
				num7 += num8 - _s.MaxStopSpacing;
			}
		}
		stopSelection.Unserved = num7;
		stopSelection.SharedFraction = (double)list2.Count((StopCandidate c) => c.Existing != null) / (double)list2.Count;
		foreach (StopCandidate item2 in list2)
		{
			int[] reach = item2.Reach;
			foreach (int item in reach)
			{
				stopSelection.Zones.Add(item);
			}
		}
		stopSelection.Value = stopSelection.Zones.Sum((int z) => _zones.Zones[z].Value) - ((IEnumerable<StopCandidate>)list2).Sum((Func<StopCandidate, double>)Cost);
		return stopSelection;
	}
}
