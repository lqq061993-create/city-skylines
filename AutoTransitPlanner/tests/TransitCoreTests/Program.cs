using System;
using System.Collections.Generic;
using System.Linq;
using AutoTransitPlanner.Transit;
using CityTransit.Core;

internal static class Program
{
	private static int _passed;

	private static int _failed;

	private static int Main(string[] args)
	{
		TestCoreDetection();
		TestPlannerStaysOnCore();
		TestDeadEndCityPlansNoLine();
		TestRouteRules();
		TestRepairSearch();
		Console.WriteLine($"{(args.Length > 0 ? args[0] : "core")}: {_passed} passed, {_failed} failed");
		return (_failed == 0) ? 0 : 1;
	}

	private static void TestCoreDetection()
	{
		CityModel city = RingCity(out int spurSegment);
		RoadNetwork net = new RoadNetwork(city.Roads);
		Check("ring: 4 nodes are core", net.CoreNodeCount == 4, $"CoreNodeCount={net.CoreNodeCount}");
		Check("spur node is not core", !net.IsCore(4), "node 4");
		Check("spur segment is not core", !net.IsCoreSegment(spurSegment), $"segment {spurSegment}");
		Check("ring segments are core", net.IsCoreSegment(0) && net.IsCoreSegment(1) && net.IsCoreSegment(2) && net.IsCoreSegment(3), "segments 0..3");
		Check("NearestCoreNode avoids the spur", net.NearestCoreNode(new Vec2(620.0, -280.0), 350.0) >= 0, "nearest core node");
		Check("SnapToCore never returns the spur", net.SnapToCore(new Vec2(600.0, -280.0), 400.0, walkableOnly: true).Segment != spurSegment, "SnapToCore segment");

		CityModel deadEnd = new CityModel();
		deadEnd.Roads.Add(new RoadSegment(0, 0, 1, new Vec2[2]
		{
			new Vec2(0.0, 0.0),
			new Vec2(500.0, 0.0)
		}));
		RoadNetwork deadEndNet = new RoadNetwork(deadEnd.Roads);
		Check("dead-end street has no core", !deadEndNet.HasCore && deadEndNet.CoreNodeCount == 0, $"CoreNodeCount={deadEndNet.CoreNodeCount}");

		CityModel pocket = new CityModel();
		pocket.Roads.Add(new RoadSegment(0, 0, 1, new Vec2[2]
		{
			new Vec2(0.0, 0.0),
			new Vec2(500.0, 0.0)
		}));
		pocket.Roads.Add(new RoadSegment(1, 1, 2, new Vec2[2]
		{
			new Vec2(500.0, 0.0),
			new Vec2(500.0, -300.0)
		}, allowForward: true, allowBackward: false));
		RoadNetwork pocketNet = new RoadNetwork(pocket.Roads);
		Check("one-way pocket node is pruned", !pocketNet.IsCore(2), "node 2");
		Check("one-way pocket segment is not core", !pocketNet.IsCoreSegment(1), "segment 1");
	}

	private static void TestPlannerStaysOnCore()
	{
		CityModel city = RingCity(out int spurSegment);
		PlannerSettings settings = TestSettings();
		TransitPlan plan = TransitPlanner.Plan(city, settings);
		Check("ring city plans at least one line", plan.Lines.Count >= 1, $"lines={plan.Lines.Count}, notes={string.Join(" | ", plan.Notes)}");
		Check("no planned stop sits on the spur", plan.Stops.All((PlannedStop s) => s.SegmentId != spurSegment), $"stops={plan.Stops.Count}");
		Check("no line stop sits on the spur", plan.Lines.All((PlannedLine l) => l.Stops.All((PlannedStop s) => s.SegmentId != spurSegment)), "line stops");
		Check("done-end note is not reported when a loop exists", !plan.Notes.Any((string n) => n.Contains("no drivable loop")), string.Join(" | ", plan.Notes));

		RoadNetwork net = new RoadNetwork(city.Roads);
		ZoneModel zones = ZoneModel.Build(city, net, settings);
		CoverageIndex coverage = new CoverageIndex(net, zones, settings.WalkRadius);
		Dictionary<(int, bool), List<PlannedStop>> registry = new Dictionary<(int, bool), List<PlannedStop>>();
		StopSelector selector = new StopSelector(net, zones, coverage, settings, registry);
		LineBuilder builder = new LineBuilder(net, zones, selector, settings);
		int proposals = 0;
		for (int i = 0; i < 4; i++)
		{
			LineProposal proposal = builder.FindBest();
			if (proposal == null)
			{
				break;
			}
			proposals++;
			Check($"proposal {i} stays on core segments", proposal.Route.All((RouteArc a) => net.IsCoreSegment(a.Segment)), "route arcs");
			Check($"proposal {i} has no turnaround", RouteRules.IsAcceptableLoop(net, proposal.Route), "IsAcceptableLoop");
			builder.MarkServed(proposal.Stops.Zones, settings.ServedFlowLeft);
		}
		Check("builder produced proposals", proposals > 0, $"proposals={proposals}");
	}

	private static void TestDeadEndCityPlansNoLine()
	{
		CityModel city = new CityModel();
		city.Roads.Add(new RoadSegment(0, 0, 1, new Vec2[2]
		{
			new Vec2(0.0, 0.0),
			new Vec2(600.0, 0.0)
		}));
		city.Residents.Add(new DemandPoint(new Vec2(60.0, 20.0), 400.0));
		city.Residents.Add(new DemandPoint(new Vec2(540.0, 20.0), 400.0));
		city.Destinations.Add(new DemandPoint(new Vec2(60.0, -20.0), 400.0));
		city.Destinations.Add(new DemandPoint(new Vec2(540.0, -20.0), 400.0));
		TransitPlan plan = TransitPlanner.Plan(city, TestSettings());
		Check("dead-end city plans no line", plan.Lines.Count == 0, $"lines={plan.Lines.Count}");
		Check("dead-end city reports why", plan.Notes.Any((string n) => n.Contains("through roads: 0") && n.Contains("no loop a bus can follow")), string.Join(" | ", plan.Notes));
	}

	private static void TestRouteRules()
	{
		CityModel city = RingCity(out _);
		RoadNetwork net = new RoadNetwork(city.Roads);
		List<RouteArc> loop = new List<RouteArc>
		{
			new RouteArc(0, true),
			new RouteArc(1, true),
			new RouteArc(2, true),
			new RouteArc(3, true)
		};
		Check("ring loop is acceptable", RouteRules.IsAcceptableLoop(net, loop), "loop");
		List<RouteArc> uTurn = new List<RouteArc>
		{
			new RouteArc(0, true),
			new RouteArc(0, false)
		};
		Check("out-and-back on one segment is a turnaround", !RouteRules.IsAcceptableLoop(net, uTurn), "u-turn");
		List<RouteArc> spur = new List<RouteArc>
		{
			new RouteArc(0, true),
			new RouteArc(4, true),
			new RouteArc(4, false),
			new RouteArc(0, false)
		};
		Check("route into a dead-end spur is rejected", !RouteRules.IsAcceptableLoop(net, spur), "spur");
		List<RouteArc> bridge = new List<RouteArc>
		{
			new RouteArc(0, true),
			new RouteArc(5, true),
			new RouteArc(5, false),
			new RouteArc(0, false)
		};
		Check("out-and-back across a bridge segment is a turnaround too", !RouteRules.IsAcceptableLoop(net, bridge), "bridge");
		List<RouteArc> closed = new List<RouteArc>
		{
			new RouteArc(0, true),
			new RouteArc(1, true),
			new RouteArc(2, true),
			new RouteArc(3, true),
			new RouteArc(0, true)
		};
		Check("loop that repeats its first segment is not simple", !RouteRules.IsAcceptableLoop(net, closed), "closed duplicate");
	}

	private static void TestRepairSearch()
	{
		RouteRepairSearch via = new RouteRepairSearch(new bool[4] { true, false, true, true });
		int[] first = via.Next(new int[4] { 0, 1, 2, 3 }, new int[1] { 1 });
		Check("repair removes the via waypoint first", first != null && first.SequenceEqual(new int[3] { 0, 2, 3 }), Format(first));
		RouteRepairSearch tiny = new RouteRepairSearch(new bool[2] { true, true });
		Check("repair refuses to drop below two stops", tiny.Next(new int[2] { 0, 1 }, new int[1] { 0 }) == null, "two-stop line");
		RouteRepairSearch single = new RouteRepairSearch(new bool[4] { true, true, true, true });
		int[] a = single.Next(new int[4] { 0, 1, 2, 3 }, new int[1] { 0 });
		int[] b = single.Next(new int[4] { 0, 1, 2, 3 }, new int[1] { 3 });
		Check("repair drops the far end of the failed leg", a != null && a.SequenceEqual(new int[3] { 0, 2, 3 }), Format(a));
		Check("repair drops the near end next", b != null && b.SequenceEqual(new int[3] { 1, 2, 3 }), Format(b));
		RouteRepairSearch pairs = new RouteRepairSearch(Enumerable.Repeat(true, 12).ToArray());
		int[] candidate = Enumerable.Range(0, 12).ToArray();
		List<int> lengths = new List<int>();
		for (int i = 0; i < 24; i++)
		{
			int[] next = pairs.Next(candidate, new int[1] { 0 });
			if (next == null)
			{
				break;
			}
			CheckTimes($"repair candidate {i} stays ordered", next.SequenceEqual(next.OrderBy((int v) => v)), Format(next), 3);
			lengths.Add(next.Length);
			candidate = next;
		}
		Check("repair tries dropping both ends of a failed leg", lengths.Contains(10), string.Join(",", lengths));
		Check("repair stays inside its preview budget", lengths.Count <= 24, $"previews={lengths.Count}");
	}

	private static int _orderedChecks;

	private static void CheckTimes(string name, bool ok, string detail, int max)
	{
		if (++_orderedChecks > max)
		{
			return;
		}
		Check(name, ok, detail);
	}

	private static CityModel RingCity(out int spurSegment)
	{
		spurSegment = 4;
		CityModel city = new CityModel();
		List<Vec2> corners = new List<Vec2>
		{
			new Vec2(0.0, 0.0),
			new Vec2(600.0, 0.0),
			new Vec2(600.0, 600.0),
			new Vec2(0.0, 600.0)
		};
		for (int i = 0; i < 4; i++)
		{
			city.Roads.Add(new RoadSegment(i, i, (i + 1) % 4, new Vec2[2]
			{
				corners[i],
				corners[(i + 1) % 4]
			}, allowForward: true, allowBackward: true, allowStops: true, allowWalking: true, costFactor: 1.0, width: 16.0));
		}
		city.Roads.Add(new RoadSegment(spurSegment, 1, 4, new Vec2[2]
		{
			new Vec2(600.0, 0.0),
			new Vec2(600.0, -300.0)
		}, allowForward: true, allowBackward: true, allowStops: true, allowWalking: true, costFactor: 1.0, width: 12.0));
		city.Roads.Add(new RoadSegment(5, 0, 5, new Vec2[2]
		{
			new Vec2(0.0, 0.0),
			new Vec2(0.0, -300.0)
		}, allowForward: true, allowBackward: true, allowStops: true, allowWalking: true, costFactor: 1.0, width: 12.0));
		city.Residents.Add(new DemandPoint(new Vec2(120.0, 120.0), 600.0));
		city.Residents.Add(new DemandPoint(new Vec2(480.0, 120.0), 600.0));
		city.Residents.Add(new DemandPoint(new Vec2(480.0, 480.0), 600.0));
		city.Residents.Add(new DemandPoint(new Vec2(120.0, 480.0), 600.0));
		city.Residents.Add(new DemandPoint(new Vec2(620.0, -280.0), 600.0));
		city.Destinations.Add(new DemandPoint(new Vec2(300.0, 40.0), 400.0));
		city.Destinations.Add(new DemandPoint(new Vec2(300.0, 560.0), 400.0));
		city.Destinations.Add(new DemandPoint(new Vec2(40.0, 300.0), 400.0));
		city.Destinations.Add(new DemandPoint(new Vec2(560.0, 300.0), 400.0));
		return city;
	}

	private static PlannerSettings TestSettings()
	{
		return new PlannerSettings
		{
			WalkRadius = 350.0,
			ZoneSize = 200.0,
			MinStopSpacing = 200.0,
			MaxStopSpacing = 700.0,
			CandidateSpacing = 40.0,
			IntersectionClearance = 30.0,
			StopCost = 5.0,
			MinStopsPerLine = 3,
			MaxStopsPerLine = 24,
			MinLineLength = 400.0,
			MaxLineLength = 6000.0,
			MaxLines = 3,
			SeedsPerLine = 14,
			MaxDetours = 2,
			LengthCostFactor = 0.0,
			MinLineZones = 1.0,
			TargetCoverage = 0.9,
			TripBonus = 1.0,
			FleetBudget = 0
		};
	}

	private static string Format(int[] values)
	{
		return (values == null) ? "null" : "{" + string.Join(",", values) + "}";
	}

	private static void Check(string name, bool ok, string detail)
	{
		if (ok)
		{
			_passed++;
			return;
		}
		_failed++;
		Console.WriteLine($"FAIL {name}: {detail}");
	}
}
