namespace CityTransit.Core;

public sealed class PlannerSettings
{
	public double WalkRadius { get; set; } = 350.0;


	public double ZoneSize { get; set; } = 200.0;


	public double MinStopSpacing { get; set; } = 220.0;


	public double MaxStopSpacing { get; set; } = 650.0;


	public double CandidateSpacing { get; set; } = 60.0;


	public double IntersectionClearance { get; set; } = 30.0;


	public double StopShareDistance { get; set; } = 45.0;


	public double StopCost { get; set; } = 12.0;


	public int MinStopsPerLine { get; set; } = 3;


	public int MaxStopsPerLine { get; set; } = 24;


	public double MinLineLength { get; set; } = 1500.0;


	public double MaxLineLength { get; set; } = 9000.0;


	public int MaxLines { get; set; } = 60;


	public int SeedsPerLine { get; set; } = 14;


	public int MaxDetours { get; set; } = 4;


	public double RingDetourLimit { get; set; } = 1.4;


	public double TripBonus { get; set; } = 1.0;


	public double ServedFlowLeft { get; set; } = 0.05;


	public double MaxSharedStops { get; set; } = 0.5;


	public double CoveredZoneValue { get; set; } = 0.25;


	public double LengthCostFactor { get; set; } = 0.3;


	public double MinLineZones { get; set; } = 4.0;


	public double TargetCoverage { get; set; } = 0.97;


	public double CommuteWeight { get; set; } = 1.0;


	public double ShoppingRate { get; set; } = 0.35;


	public double LeisureRate { get; set; } = 0.25;


	public double MinErrandDistance { get; set; } = 450.0;


	public double MaxErrandDistance { get; set; } = 3500.0;


	public int GravityNeighbours { get; set; } = 10;


	public double MinTripDistance { get; set; } = 500.0;


	public double MaxTripDistance { get; set; } = 4500.0;


	public double SecondsPerKm { get; set; } = 36.0;


	public double SecondsPerStop { get; set; } = 4.4;


	public double SecondsPerHour { get; set; } = 182.0;


	public double MinHeadway { get; set; } = 45.0;


	public double MaxHeadway { get; set; } = 90.0;


	public int FleetBudget { get; set; }

	public double ComfortStopShare { get; set; } = 0.35;


	public double MinComfortRoadWidth { get; set; } = 10.0;


	public PlannerSettings Clone()
	{
		return (PlannerSettings)MemberwiseClone();
	}
}
