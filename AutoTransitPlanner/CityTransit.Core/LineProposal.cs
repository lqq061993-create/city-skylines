using System.Collections.Generic;

namespace CityTransit.Core;

internal sealed class LineProposal
{
	public List<RouteArc> Route;

	public StopSelection Stops;

	public double Length;

	public double TripGain;

	public double Gain;

	public double Net;

	public double GainPerKm
	{
		get
		{
			if (!(Length <= 0.0))
			{
				return Gain / (Length / 1000.0);
			}
			return 0.0;
		}
	}
}
