using System.Collections.Generic;
using CityTransit.Core;

namespace AutoTransitPlanner.Transit;

public static class AutoTransitConfig
{
	public static bool Enabled = false;

	public static float FirstRunDelay = 20f;

	public static float Interval = 600f;

	public static bool RemoveOld = false;

	public static int WalkRadius = 350;

	public static int MaxLines = 60;

	public static int MaxLineLength = 9000;

	public static bool Shopping = true;

	public static bool Leisure = true;

	public static float StopSideOffset = 1.5f;

	public static bool SizeFleet = true;

	public static bool FitToDepots = true;

	public const string AutomaticStopStyle = "Automatic";

	public static string StopStyle = "Automatic";

	public static List<string> AvailableStopStyles = new List<string>();

	public static PlannerSettings Planner => new PlannerSettings
	{
		WalkRadius = WalkRadius,
		MaxLines = MaxLines,
		MaxLineLength = MaxLineLength,
		ShoppingRate = (Shopping ? new PlannerSettings().ShoppingRate : 0.0),
		LeisureRate = (Leisure ? new PlannerSettings().LeisureRate : 0.0)
	};
}
