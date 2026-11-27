using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CityTransit.Core;

public static class TransitPlanner
{
	private struct FleetNeed
	{
		public int Min;

		public int Wanted;

		public double RoundTrip;
	}

	public static TransitPlan Plan(CityModel city, PlannerSettings settings = null)
	{
		settings = settings ?? new PlannerSettings();
		TransitPlan transitPlan = new TransitPlan();
		if (city.Roads.Count == 0)
		{
			transitPlan.Notes.Add("no roads");
			return transitPlan;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		RoadNetwork net = new RoadNetwork(city.Roads);
		long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
		transitPlan.Nodes = net.NodeCount;
		transitPlan.UsableNodes = Enumerable.Range(0, net.NodeCount).Count((int n) => net.IsUsable(n));
		transitPlan.Notes.Add($"through roads: {net.CoreNodeCount} of {net.NodeCount} nodes can be driven into and out of without turning around" + ((net.CoreNodeCount == 0) ? " (the road network has no loop a bus can follow)" : ""));
		ZoneModel zones = ZoneModel.Build(city, net, settings);
		long elapsedMilliseconds2 = stopwatch.ElapsedMilliseconds;
		transitPlan.Zones = zones.Zones.Count;
		transitPlan.TotalDemand = zones.TotalWeight;
		if (zones.Zones.Count == 0)
		{
			transitPlan.Notes.Add("no demand");
			return transitPlan;
		}
		if (transitPlan.UsableNodes == 0 || net.CoreNodeCount == 0)
		{
			transitPlan.Notes.Add("no drivable loop in the road network: every road is a dead end or a one-way pocket");
		}
		CoverageIndex coverage = new CoverageIndex(net, zones, settings.WalkRadius);
		Dictionary<(int, bool), List<PlannedStop>> registry = new Dictionary<(int, bool), List<PlannedStop>>();
		StopSelector stops = new StopSelector(net, zones, coverage, settings, registry);
		LineBuilder lineBuilder = new LineBuilder(net, zones, stops, settings);
		MarkExistingCoverage(city, net, zones, coverage, settings);
		List<HashSet<int>> list = new List<HashSet<int>>();
		int num = 0;
		while (transitPlan.Lines.Count < settings.MaxLines && CoveredShare(zones) < settings.TargetCoverage)
		{
			LineProposal lineProposal = lineBuilder.FindBest();
			if (lineProposal == null)
			{
				if (lineBuilder.StopReason != null)
				{
					transitPlan.Notes.Add(lineBuilder.StopReason);
				}
				break;
			}
			if (settings.FleetBudget > 0)
			{
				double people = lineProposal.Stops.Zones.Sum((int z) => zones.Zones[z].Weight);
				FleetNeed fleetNeed = NeedFor(lineProposal.Length, lineProposal.Stops.Chosen.Count, people, settings);
				if (num + fleetNeed.Min > settings.FleetBudget)
				{
					transitPlan.StoppedByFleet = true;
					break;
				}
				num += fleetNeed.Min;
			}
			list.Add(Commit(lineProposal, transitPlan, net, zones, registry, lineBuilder, settings));
		}
		FitFleet(transitPlan, settings.FleetBudget);
		if (settings.FleetBudget > 0)
		{
			transitPlan.Notes.Add($"fleet: {transitPlan.Lines.Sum((PlannedLine l) => l.SuggestedVehicles)} of {settings.FleetBudget} buses in use, the lines would like {transitPlan.FleetWanted}" + (transitPlan.StoppedByFleet ? "; more lines were left out for lack of buses" : ""));
		}
		if (transitPlan.Lines.Count >= settings.MaxLines)
		{
			transitPlan.Notes.Add("stopped at the maximum number of lines");
		}
		if (CoveredShare(zones) >= settings.TargetCoverage)
		{
			transitPlan.Notes.Add("target coverage reached");
		}
		AddUncoveredNotes(transitPlan, zones, net);
		AddTripNote(transitPlan, zones, list);
		int num2 = (from n in Enumerable.Range(0, net.NodeCount)
			where net.IsUsable(n)
			select net.SccOf[n]).Distinct().Count();
		transitPlan.Notes.Add($"drivable road groups: {num2}");
		transitPlan.Notes.Add($"timing: network {elapsedMilliseconds} ms, zones {elapsedMilliseconds2 - elapsedMilliseconds} ms, lines {stopwatch.ElapsedMilliseconds - elapsedMilliseconds2} ms");
		AssignStopTypes(transitPlan, city, settings);
		AddWholeNetwork(transitPlan, city, settings);
		transitPlan.CoveredDemand = zones.Zones.Where((Zone z) => z.Covered).Sum((Zone z) => z.Weight);
		double num3 = zones.Zones.Sum((Zone z) => z.Residents);
		transitPlan.ResidentCoverage = ((num3 <= 0.0) ? 0.0 : (zones.Zones.Where((Zone z) => z.Covered).Sum((Zone z) => z.Residents) / num3));
		return transitPlan;
	}

	private static void AssignStopTypes(TransitPlan plan, CityModel city, PlannerSettings s)
	{
		if (plan.Stops.Count == 0)
		{
			return;
		}
		Dictionary<int, double> width = new Dictionary<int, double>();
		foreach (RoadSegment road in city.Roads)
		{
			width[road.Id] = road.Width;
		}
		List<PlannedStop> list = plan.Stops.Where(Fits).ToList();
		int count = (int)Math.Ceiling((double)list.Count * s.ComfortStopShare);
		foreach (PlannedStop item in (from st in list
			orderby st.CoveredWeight * (double)st.LineCount descending, st.Id
			select st).Take(count))
		{
			item.Comfort = true;
		}
		foreach (PlannedStop item2 in list.Where((PlannedStop st) => st.LineCount >= 2))
		{
			item2.Comfort = true;
		}
		plan.ComfortStops = plan.Stops.Count((PlannedStop st) => st.Comfort);
		plan.NarrowRoadStops = plan.Stops.Count - list.Count;
		plan.Notes.Add($"stop types: {plan.ComfortStops} comfortable, {plan.Stops.Count - plan.ComfortStops} basic" + ((plan.NarrowRoadStops > 0) ? $" ({plan.NarrowRoadStops} stops on roads too narrow for the comfortable type)" : ""));
		bool Fits(PlannedStop st)
		{
			if (!(s.MinComfortRoadWidth <= 0.0) && width.TryGetValue(st.SegmentId, out var value) && !(value <= 0.0))
			{
				return value >= s.MinComfortRoadWidth;
			}
			return true;
		}
	}

	private static void AddWholeNetwork(TransitPlan plan, CityModel city, PlannerSettings settings)
	{
		if (settings.FleetBudget > 0 && plan.FleetLimited)
		{
			PlannerSettings plannerSettings = settings.Clone();
			plannerSettings.FleetBudget = 0;
			TransitPlan transitPlan = Plan(city, plannerSettings);
			plan.FullLines = transitPlan.Lines.Count;
			plan.FullFleet = transitPlan.FleetWanted;
			plan.FullMinFleet = transitPlan.Lines.Sum((PlannedLine l) => l.MinVehicles);
		}
		else
		{
			plan.FullLines = plan.Lines.Count;
			plan.FullFleet = plan.FleetWanted;
			plan.FullMinFleet = plan.Lines.Sum((PlannedLine l) => l.MinVehicles);
		}
		plan.Notes.Add($"whole network: {plan.FullLines} lines, {plan.FullFleet} buses wanted (at least {plan.FullMinFleet})");
	}

	private static void AddUncoveredNotes(TransitPlan plan, ZoneModel zones, RoadNetwork net)
	{
		List<Zone> list = zones.Zones.Where((Zone z) => !z.Covered).ToList();
		if (list.Count == 0)
		{
			return;
		}
		double total = zones.TotalWeight;
		IEnumerable<string> values = from z in list
			group z by z.Snap.Valid ? ((z.AccessNode < 0) ? 1 : 2) : 0 into g
			orderby g.Key
			select string.Format("{0} {1}", g.Count(), (g.Key == 0) ? "without a road nearby" : ((g.Key == 1) ? "on roads outside any drivable loop" : "on drivable roads but unserved")) + $" ({g.Sum((Zone z) => z.Weight) / total:P0})";
		plan.Notes.Add($"{list.Count} zones not covered ({list.Sum((Zone z) => z.Residents):F0} residents, {list.Sum((Zone z) => z.Destinations):F0} workplace/school seats): " + string.Join(", ", values));
		foreach (Zone item in list.OrderByDescending((Zone z) => z.Weight).Take(3))
		{
			plan.Notes.Add($"uncovered zone at ({item.Center.X:F0}, {item.Center.Y:F0}): weight {item.Weight:F0} " + $"(residents {item.Residents:F0}), {Reason(item)}");
		}
		int GroupSize(Zone z)
		{
			int num = net.FromNode(z.Snap.Segment);
			int num2 = net.ToNode(z.Snap.Segment);
			return Math.Max(net.SccSize[net.SccOf[num]], net.SccSize[net.SccOf[num2]]);
		}
		string Reason(Zone z)
		{
			if (z.Snap.Valid)
			{
				if (z.AccessNode >= 0)
				{
					return "drivable road, but no line was worth building";
				}
				return $"dead-end or one-way street, no loop a bus can drive (group of {GroupSize(z)} nodes)";
			}
			return "no walkable road nearby";
		}
	}

	private static double CoveredShare(ZoneModel zones)
	{
		double totalWeight = zones.TotalWeight;
		if (!(totalWeight <= 0.0))
		{
			return zones.Zones.Where((Zone z) => z.Covered).Sum((Zone z) => z.Weight) / totalWeight;
		}
		return 1.0;
	}

	private static void MarkExistingCoverage(CityModel city, RoadNetwork net, ZoneModel zones, CoverageIndex coverage, PlannerSettings s)
	{
		foreach (Vec2 existingStop in city.ExistingStops)
		{
			SnapResult snapResult = net.Snap(existingStop, 80.0, walkableOnly: true);
			if (snapResult.Valid)
			{
				int[] array = coverage.Reach(snapResult.Segment, snapResult.Offset);
				foreach (int index in array)
				{
					MarkCovered(zones.Zones[index], s);
				}
			}
		}
	}

	private static void MarkCovered(Zone z, PlannerSettings s)
	{
		z.Covered = true;
		z.Value = Math.Min(z.Value, z.Weight * s.CoveredZoneValue);
	}

	private static void AddTripNote(TransitPlan plan, ZoneModel zones, List<HashSet<int>> lineZones)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		for (int i = 0; i < zones.FlowKeys.Length; i++)
		{
			int a = ZoneModel.KeyA(zones.FlowKeys[i]);
			int b = ZoneModel.KeyB(zones.FlowKeys[i]);
			double num4 = zones.FlowValue[i];
			num += num4;
			if (zones.Zones[a].Covered && zones.Zones[b].Covered)
			{
				num3 += num4;
			}
			if (lineZones.Any((HashSet<int> l) => l.Contains(a) && l.Contains(b)))
			{
				num2 += num4;
			}
		}
		if (!(num <= 0.0))
		{
			plan.DirectTripShare = num2 / num;
			plan.ReachableTripShare = num3 / num;
			plan.Notes.Add($"trips: {plan.DirectTripShare:P0} ride a single line, {plan.ReachableTripShare:P0} have a stop near both ends " + "(the difference needs a transfer)");
		}
	}

	private static HashSet<int> Commit(LineProposal p, TransitPlan plan, RoadNetwork net, ZoneModel zones, Dictionary<(int, bool), List<PlannedStop>> registry, LineBuilder builder, PlannerSettings s)
	{
		PlannedLine plannedLine = new PlannedLine
		{
			Length = p.Length,
			Gain = p.Gain,
			UnservedLength = p.Stops.Unserved
		};
		foreach (StopCandidate item in p.Stops.Chosen)
		{
			PlannedStop plannedStop = item.Existing ?? FindNearby(registry, item, s);
			if (plannedStop == null)
			{
				plannedStop = new PlannedStop
				{
					Id = plan.Stops.Count,
					SegmentId = net.Segments[item.Segment].Id,
					Offset = item.Offset,
					Position = item.Position,
					Direction = item.Direction,
					Forward = item.Forward,
					CoveredWeight = item.Reach.Sum((int z) => zones.Zones[z].Weight)
				};
				plan.Stops.Add(plannedStop);
				if (!registry.TryGetValue((item.Segment, item.Forward), out var value))
				{
					value = (registry[(item.Segment, item.Forward)] = new List<PlannedStop>());
				}
				value.Add(plannedStop);
			}
			if (!plannedLine.Stops.Contains(plannedStop))
			{
				plannedLine.Stops.Add(plannedStop);
				plannedStop.LineCount++;
			}
		}
		HashSet<int> zones2 = p.Stops.Zones;
		double num = 0.0;
		foreach (int item2 in zones2)
		{
			num += zones.Zones[item2].Weight;
		}
		builder.MarkServed(zones2, s.ServedFlowLeft);
		foreach (int item3 in zones2)
		{
			MarkCovered(zones.Zones[item3], s);
		}
		SizeFleet(plannedLine, num, s);
		plan.Lines.Add(plannedLine);
		return zones2;
	}

	private static PlannedStop FindNearby(Dictionary<(int, bool), List<PlannedStop>> registry, StopCandidate c, PlannerSettings s)
	{
		if (!registry.TryGetValue((c.Segment, c.Forward), out var value))
		{
			return null;
		}
		return value.FirstOrDefault((PlannedStop x) => Math.Abs(x.Offset - c.Offset) <= s.StopShareDistance);
	}

	private static FleetNeed NeedFor(double length, int stopCount, double people, PlannerSettings s)
	{
		double num = Math.Max(30.0, length / 1000.0 * s.SecondsPerKm + (double)stopCount * s.SecondsPerStop);
		double num2 = people * 0.05;
		double num3 = s.SecondsPerHour / num;
		int val = (int)Math.Ceiling(num2 / (30.0 * num3 * 0.8));
		int num4 = Math.Max(1, (int)Math.Round(num / s.MaxHeadway));
		int val2 = Math.Max(1, (int)Math.Round(num / s.MinHeadway));
		int val3 = Math.Max(num4, val);
		val3 = Math.Min(Math.Max(1, val3), Math.Max(val2, num4));
		FleetNeed result = default(FleetNeed);
		result.Min = num4;
		result.Wanted = val3;
		result.RoundTrip = num;
		return result;
	}

	private static void SizeFleet(PlannedLine line, double people, PlannerSettings s)
	{
		FleetNeed fleetNeed = NeedFor(line.Length, line.Stops.Count, people, s);
		line.MinVehicles = fleetNeed.Min;
		line.WantedVehicles = fleetNeed.Wanted;
		line.People = people;
		line.RoundTrip = fleetNeed.RoundTrip;
		line.SuggestedVehicles = fleetNeed.Wanted;
		line.SuggestedHeadway = fleetNeed.RoundTrip / (double)fleetNeed.Wanted;
	}

	private static void FitFleet(TransitPlan plan, int budget)
	{
		plan.FleetWanted = plan.Lines.Sum((PlannedLine l) => l.WantedVehicles);
		if (budget <= 0 || plan.Lines.Count == 0)
		{
			return;
		}
		foreach (PlannedLine line in plan.Lines)
		{
			line.SuggestedVehicles = line.MinVehicles;
		}
		for (int num = budget - plan.Lines.Sum((PlannedLine l) => l.MinVehicles); num > 0; num--)
		{
			PlannedLine plannedLine = null;
			double num2 = 0.0;
			foreach (PlannedLine line2 in plan.Lines)
			{
				if (line2.SuggestedVehicles < line2.WantedVehicles)
				{
					double num3 = line2.People / (double)line2.SuggestedVehicles;
					if (num3 > num2)
					{
						num2 = num3;
						plannedLine = line2;
					}
				}
			}
			if (plannedLine == null)
			{
				break;
			}
			plannedLine.SuggestedVehicles++;
		}
		foreach (PlannedLine line3 in plan.Lines)
		{
			line3.SuggestedHeadway = line3.RoundTrip / (double)line3.SuggestedVehicles;
		}
		plan.FleetLimited = plan.StoppedByFleet || plan.Lines.Any((PlannedLine l) => l.SuggestedVehicles < l.WantedVehicles);
	}
}
