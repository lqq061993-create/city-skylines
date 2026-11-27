using System;
using System.Collections.Generic;
using System.Linq;

namespace CityTransit.Core;

internal sealed class LineBuilder
{
	private sealed class Trees
	{
		public double[] Fwd;

		public double[] Rev;

		public int[] FwdPrev;

		public int[] FwdSeg;

		public int[] RevPrev;

		public int[] RevSeg;

		public bool[] FwdForward;

		public bool[] RevForward;
	}

	private const double MinDetourValue = 40.0;

	private const double MaxDetourLength = 1400.0;

	private const double DetourSearchRadius = 900.0;

	// Roads of the drivable core cost their normal price; anything that is only reachable by
	// turning around costs this much, so a route only takes it when there is no other way.
	private const double DeadEndFactor = 80.0;

	// Roads already used by the forward half of a loop cost this much on the way back.
	private const double ReuseFactor = 6.0;

	private readonly RoadNetwork _net;

	private readonly ZoneModel _zones;

	private readonly StopSelector _stops;

	private readonly PlannerSettings _s;

	private readonly double[] _left;

	private readonly int[] _mark;

	private int _stamp;

	private readonly HashSet<long> _exhausted = new HashSet<long>();

	private int _noPath;

	private int _tooLong;

	private int _noStops;

	private int _deadEnd;

	private readonly double _costPerKm;

	private readonly double _minGain;

	private const int MaxCachedTrees = 48;

	private readonly Dictionary<int, Trees> _trees = new Dictionary<int, Trees>();

	public string StopReason { get; private set; }

	public LineBuilder(RoadNetwork net, ZoneModel zones, StopSelector stops, PlannerSettings s)
	{
		_net = net;
		_zones = zones;
		_stops = stops;
		_s = s;
		_left = (double[])zones.FlowValue.Clone();
		_mark = new int[zones.Zones.Count];
		double num = 0.0;
		if (zones.Zones.Count > 0)
		{
			List<double> list = (from z in zones.Zones
				select z.Weight into w
				where w > 0.0
				orderby w
				select w).ToList();
			if (list.Count > 0)
			{
				num = list[list.Count / 2];
			}
		}
		_costPerKm = s.LengthCostFactor * num * 5.0;
		_minGain = s.MinLineZones * num;
	}

	public void MarkServed(HashSet<int> zones, double leftShare)
	{
		_stamp++;
		foreach (int zone in zones)
		{
			_mark[zone] = _stamp;
		}
		foreach (int zone2 in zones)
		{
			int[] array = _zones.AdjOther[zone2];
			int[] array2 = _zones.AdjFlow[zone2];
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] > zone2 && _mark[array[i]] == _stamp)
				{
					_left[array2[i]] *= leftShare;
				}
			}
		}
	}

	public LineProposal FindBest()
	{
		_noPath = (_tooLong = (_noStops = (_deadEnd = 0)));
		LineProposal lineProposal = null;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		LineProposal lineProposal2 = null;
		foreach (long item in PickSeeds().Concat(PickCoverageSeeds()).Distinct())
		{
			num++;
			LineProposal lineProposal3 = BuildFromSeed(item);
			if (lineProposal3 == null)
			{
				num2++;
				_exhausted.Add(item);
			}
			else if (!Acceptable(lineProposal3))
			{
				if (lineProposal3.Length < _s.MinLineLength)
				{
					num3++;
				}
				else if (lineProposal3.Stops.SharedFraction > _s.MaxSharedStops)
				{
					num6++;
				}
				else if (lineProposal3.Gain < _minGain)
				{
					num4++;
				}
				else
				{
					num5++;
				}
				if (lineProposal2 == null || lineProposal3.Net > lineProposal2.Net)
				{
					lineProposal2 = lineProposal3;
				}
				_exhausted.Add(item);
			}
			else if (lineProposal == null || lineProposal3.Net > lineProposal.Net)
			{
				lineProposal = lineProposal3;
			}
		}
		StopReason = ((lineProposal != null) ? null : ($"no acceptable line: seeds={num}, noRoute={num2} (noPath={_noPath}, noStops={_noStops}, tooLong={_tooLong}, deadEnds={_deadEnd}), tooShort={num3}, lowGain={num4}, unprofitable={num5}, duplicate={num6}" + ((lineProposal2 == null) ? "" : ($", best rejected gain={lineProposal2.Gain:F0} (min {_minGain:F0}), length={lineProposal2.Length:F0}m, " + $"net={lineProposal2.Net:F0}, cost/km={_costPerKm:F0}"))));
		return lineProposal;
	}

	private bool Acceptable(LineProposal p)
	{
		if (p.Stops != null && p.Length >= _s.MinLineLength && p.Gain >= _minGain && p.Net > 0.0)
		{
			return p.Stops.SharedFraction <= _s.MaxSharedStops;
		}
		return false;
	}

	private IEnumerable<long> PickSeeds()
	{
		List<(long, double)> list = new List<(long, double)>();
		for (int i = 0; i < _left.Length; i++)
		{
			long num = _zones.FlowKeys[i];
			if (_left[i] <= 0.0 || _exhausted.Contains(num))
			{
				continue;
			}
			Zone zone = _zones.Zones[ZoneModel.KeyA(num)];
			Zone zone2 = _zones.Zones[ZoneModel.KeyB(num)];
			if (zone.AccessNode >= 0 && zone2.AccessNode >= 0 && zone.AccessNode != zone2.AccessNode && _net.SccOf[zone.AccessNode] == _net.SccOf[zone2.AccessNode])
			{
				double num2 = Vec2.Distance(zone.Center, zone2.Center);
				if (!(num2 < _s.MinLineLength / 2.0) && !(num2 > _s.MaxLineLength / 2.6))
				{
					list.Add((num, _left[i] * Math.Sqrt(num2)));
				}
			}
		}
		return (from x in (from x in list
				orderby x.Item2 descending, x.Item1
				select x).Take(_s.SeedsPerLine)
			select x.Item1).ToList();
	}

	private IEnumerable<long> PickCoverageSeeds()
	{
		int count = Math.Max(2, _s.SeedsPerLine / 2);
		List<Zone> list = (from z in _zones.Zones
			where z.AccessNode >= 0 && !z.Covered && z.Value > 0.0
			orderby z.Value descending, z.Id
			select z).Take(count).ToList();
		List<long> list2 = new List<long>();
		foreach (Zone item2 in list)
		{
			Zone zone = null;
			double num = double.MinValue;
			foreach (Zone zone2 in _zones.Zones)
			{
				if (zone2.Id == item2.Id || zone2.AccessNode < 0 || zone2.AccessNode == item2.AccessNode || _net.SccOf[zone2.AccessNode] != _net.SccOf[item2.AccessNode])
				{
					continue;
				}
				double num2 = Vec2.Distance(item2.Center, zone2.Center);
				if (!(num2 < _s.MinLineLength / 2.0) && !(num2 > _s.MaxLineLength / 2.6))
				{
					double num3 = zone2.Value + 0.3 * zone2.Weight - num2 * 0.01;
					if (num3 > num)
					{
						num = num3;
						zone = zone2;
					}
				}
			}
			if (zone != null)
			{
				long item = ZoneModel.PairKey(item2.Id, zone.Id);
				if (!_exhausted.Contains(item))
				{
					list2.Add(item);
				}
			}
		}
		return list2;
	}

	private LineProposal BuildFromSeed(long key)
	{
		Zone zone = _zones.Zones[ZoneModel.KeyA(key)];
		Zone zone2 = _zones.Zones[ZoneModel.KeyB(key)];
		if (!_net.IsCore(zone.AccessNode) || !_net.IsCore(zone2.AccessNode))
		{
			return null;
		}
		List<RouteArc> list = BuildLoop(zone.AccessNode, zone2.AccessNode);
		if (list == null)
		{
			_noPath++;
			_deadEnd++;
			return null;
		}
		if (_net.Length(list) > _s.MaxLineLength)
		{
			_tooLong++;
			return null;
		}
		LineProposal lineProposal = Evaluate(list);
		if (lineProposal == null)
		{
			_noStops++;
			return null;
		}
		for (int i = 0; i < _s.MaxDetours; i++)
		{
			LineProposal lineProposal2 = TryDetour(lineProposal);
			if (lineProposal2 == null)
			{
				break;
			}
			lineProposal = lineProposal2;
		}
		return lineProposal;
	}

	private List<RouteArc> BuildLoop(int a, int b)
	{
		if (!_net.IsCore(a) || !_net.IsCore(b))
		{
			return null;
		}
		List<RouteArc> list = _net.ShortestPath(a, b, (int seg) => PathFactor(seg, null));
		if (list == null || list.Count == 0)
		{
			return null;
		}
		HashSet<int> used = new HashSet<int>(list.Select((RouteArc x) => x.Segment));
		List<RouteArc> list2 = _net.ShortestPath(b, a, (int seg) => PathFactor(seg, used));
		if (list2 == null || list2.Count == 0)
		{
			return null;
		}
		List<RouteArc> list4 = new List<RouteArc>(list);
		list4.AddRange(list2);
		return RouteRules.IsAcceptableLoop(_net, list4) ? list4 : null;
	}

	private double PathFactor(int segment, HashSet<int> used)
	{
		double factor = _net.IsCoreSegment(segment) ? 1.0 : DeadEndFactor;
		if (used != null && used.Contains(segment))
		{
			factor *= ReuseFactor;
		}
		return factor;
	}

	private LineProposal Evaluate(List<RouteArc> route)
	{
		StopSelection stopSelection = _stops.Select(route);
		if (stopSelection == null)
		{
			return null;
		}
		_stamp++;
		foreach (int zone in stopSelection.Zones)
		{
			_mark[zone] = _stamp;
		}
		double num = 0.0;
		foreach (int zone2 in stopSelection.Zones)
		{
			int[] array = _zones.AdjOther[zone2];
			int[] array2 = _zones.AdjFlow[zone2];
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] > zone2 && _mark[array[i]] == _stamp)
				{
					num += _left[array2[i]];
				}
			}
		}
		LineProposal lineProposal = new LineProposal
		{
			Route = route,
			Stops = stopSelection,
			Length = stopSelection.RouteLength,
			TripGain = num,
			Gain = stopSelection.Value + _s.TripBonus * num
		};
		lineProposal.Net = lineProposal.Gain - _costPerKm * (lineProposal.Length + stopSelection.Unserved) / 1000.0;
		return lineProposal;
	}

	private LineProposal TryDetour(LineProposal current)
	{
		List<RouteArc> route = current.Route;
		int scc = _net.SccOf[StartNode(route[0])];
		List<int> list = new List<int> { StartNode(route[0]) };
		foreach (RouteArc item in route)
		{
			list.Add(EndNode(item));
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (Vec2 item2 in SamplePoints(route))
		{
			foreach (Zone item3 in _zones.Near(item2, 900.0))
			{
				hashSet.Add(item3.Id);
			}
		}
		List<Zone> list2 = (from id in hashSet
			select _zones.Zones[id] into z
			where z.AccessNode >= 0 && _net.IsCore(z.AccessNode) && !current.Stops.Zones.Contains(z.Id) && z.Value >= 40.0 && _net.SccOf[z.AccessNode] == scc
			orderby z.Value descending, z.Id
			select z).Take(6).ToList();
		LineProposal lineProposal = null;
		foreach (Zone item4 in list2)
		{
			Trees trees = TreesFor(item4.AccessNode);
			double[] fwd = trees.Fwd;
			int[] fwdPrev = trees.FwdPrev;
			int[] fwdSeg = trees.FwdSeg;
			bool[] fwdForward = trees.FwdForward;
			double[] rev = trees.Rev;
			int[] revPrev = trees.RevPrev;
			int[] revSeg = trees.RevSeg;
			bool[] revForward = trees.RevForward;
			int num = -1;
			double num2 = double.MaxValue;
			for (int i = 0; i < route.Count; i++)
			{
				double num3 = rev[list[i]];
				double num4 = fwd[list[i + 1]];
				if (num3 != double.MaxValue && num4 != double.MaxValue)
				{
					double num5 = num3 + num4 - _net.ArcCost(route[i].Segment);
					if (num5 < num2)
					{
						num2 = num5;
						num = i;
					}
				}
			}
			if (num < 0)
			{
				continue;
			}
			List<RouteArc> list3 = new List<RouteArc>(route.Take(num));
			int num6 = list[num];
			while (revPrev[num6] != -1)
			{
				list3.Add(new RouteArc(revSeg[num6], revForward[num6]));
				num6 = revPrev[num6];
			}
			list3.AddRange(RoadNetwork.Rebuild(list[num + 1], fwdPrev, fwdSeg, fwdForward));
			list3.AddRange(route.Skip(num + 1));
			double num7 = _net.Length(list3);
			if (!(num7 > _s.MaxLineLength) && !(num7 - current.Length > 1400.0) && RouteRules.IsAcceptableLoop(_net, list3))
			{
				LineProposal lineProposal2 = Evaluate(list3);
				if (lineProposal2 != null && lineProposal2.Net > current.Net && (lineProposal == null || lineProposal2.Net > lineProposal.Net))
				{
					lineProposal = lineProposal2;
				}
			}
		}
		return lineProposal;
	}

	private Trees TreesFor(int node)
	{
		if (_trees.TryGetValue(node, out var value))
		{
			return value;
		}
		if (_trees.Count >= 48)
		{
			_trees.Clear();
		}
		Trees trees = new Trees();
		trees.Fwd = _net.Distances(node, reverse: false, 1800.0, out trees.FwdPrev, out trees.FwdSeg, out trees.FwdForward, (int seg) => (_net.IsCoreSegment(seg) ? 1.0 : DeadEndFactor));
		trees.Rev = _net.Distances(node, reverse: true, 1800.0, out trees.RevPrev, out trees.RevSeg, out trees.RevForward, (int seg) => (_net.IsCoreSegment(seg) ? 1.0 : DeadEndFactor));
		_trees[node] = trees;
		return trees;
	}

	private int StartNode(RouteArc a)
	{
		return RouteRules.StartNode(_net, a);
	}

	private int EndNode(RouteArc a)
	{
		return RouteRules.EndNode(_net, a);
	}

	private List<Vec2> SamplePoints(List<RouteArc> route)
	{
		List<Vec2> list = new List<Vec2>();
		foreach (RouteArc item in route)
		{
			RoadSegment roadSegment = _net.Segments[item.Segment];
			int num = Math.Max(1, (int)Math.Ceiling(roadSegment.Length / 150.0));
			for (int i = 0; i <= num; i++)
			{
				list.Add(roadSegment.PointAt(roadSegment.Length * (double)i / (double)num, out var _));
			}
		}
		return list;
	}
}



