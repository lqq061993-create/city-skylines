using System.Collections.Generic;

namespace CityTransit.Core;

public sealed class PlannedLine
{
	public List<PlannedStop> Stops { get; } = new List<PlannedStop>();


	public double Length { get; set; }

	public double Gain { get; set; }

	public double UnservedLength { get; set; }

	public int SuggestedVehicles { get; set; }

	public double SuggestedHeadway { get; set; }

	public int MinVehicles { get; set; }

	public int WantedVehicles { get; set; }

	public double People { get; set; }

	public double RoundTrip { get; set; }
}
