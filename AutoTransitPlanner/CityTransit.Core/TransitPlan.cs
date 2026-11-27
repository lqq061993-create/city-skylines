using System.Collections.Generic;

namespace CityTransit.Core;

public sealed class TransitPlan
{
	public List<PlannedStop> Stops { get; } = new List<PlannedStop>();


	public List<PlannedLine> Lines { get; } = new List<PlannedLine>();


	public double TotalDemand { get; set; }

	public double CoveredDemand { get; set; }

	public double Coverage
	{
		get
		{
			if (!(TotalDemand <= 0.0))
			{
				return CoveredDemand / TotalDemand;
			}
			return 0.0;
		}
	}

	public double ResidentCoverage { get; set; }

	public double DirectTripShare { get; set; }

	public double ReachableTripShare { get; set; }

	public int Zones { get; set; }

	public int UsableNodes { get; set; }

	public int Nodes { get; set; }

	public int FleetWanted { get; set; }

	public int ComfortStops { get; set; }

	public int NarrowRoadStops { get; set; }

	public int FullLines { get; set; }

	public int FullFleet { get; set; }

	public int FullMinFleet { get; set; }

	public bool FleetLimited { get; set; }

	public bool StoppedByFleet { get; set; }

	public List<string> Notes { get; } = new List<string>();

}
