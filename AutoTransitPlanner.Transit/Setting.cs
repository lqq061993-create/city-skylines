using System.Collections.Generic;
using Game.Modding;
using Game.Settings;
using Game.UI.Localization;
using Game.UI.Widgets;

namespace AutoTransitPlanner.Transit;

[SettingsUIGroupOrder(new string[] { "Actions", "Automation", "Tuning" })]
[SettingsUIShowGroupName(new string[] { "Actions", "Automation", "Tuning" })]
public class Setting : ModSetting
{
	public const string kSection = "Main";

	public const string kActions = "Actions";

	public const string kAuto = "Automation";

	public const string kTuning = "Tuning";

	[SettingsUISection("Main", "Actions")]
	[SettingsUIButton]
	[SettingsUIDisplayName("AutoTransitPlanner.PlanNow", "Plan and build bus stops and lines now")]
	[SettingsUIDescription("AutoTransitPlanner.PlanNow.Desc", "Analyses the city and builds bus stops and lines. Existing stops and lines are kept by default.")]
	public bool PlanNow
	{
		set
		{
			AutoTransitController.RequestRun();
		}
	}

	[SettingsUISection("Main", "Actions")]
	[SettingsUIButton]
	[SettingsUIDisplayName("AutoTransitPlanner.RepairNow", "Repair existing bus lines with broken paths")]
	[SettingsUIDescription("AutoTransitPlanner.RepairNow.Desc", "Tries omitting unreachable waypoints from existing bus lines. Applies an edit only when the game verifies the complete route. The line keeps its name, number, color and settings.")]
	public bool RepairNow
	{
		set
		{
			AutoTransitController.RequestRepair();
		}
	}

	[SettingsUISection("Main", "Automation")]
	[SettingsUIDisplayName("AutoTransitPlanner.AutoRun", "Run automatically (every 10 minutes)")]
	[SettingsUIDescription("AutoTransitPlanner.AutoRun.Desc", "When on, the mod checks periodically and plans only when the city has no passenger bus lines.")]
	public bool AutoRun
	{
		get
		{
			return AutoTransitConfig.Enabled;
		}
		set
		{
			AutoTransitConfig.Enabled = value;
		}
	}

	[SettingsUISection("Main", "Automation")]
	[SettingsUIDisplayName("AutoTransitPlanner.RemoveOld", "Replace existing bus stops and lines on manual runs")]
	[SettingsUIDescription("AutoTransitPlanner.RemoveOld.Desc", "A manual run replaces the previous bus lines and unused roadside stops when all new lines pass game validation. Automatic runs keep existing routes.")]
	public bool RemoveOld
	{
		get
		{
			return AutoTransitConfig.RemoveOld;
		}
		set
		{
			AutoTransitConfig.RemoveOld = value;
		}
	}

	[SettingsUISection("Main", "Automation")]
	[SettingsUIDisplayName("AutoTransitPlanner.SizeFleet", "Set the number of buses per line")]
	[SettingsUIDescription("AutoTransitPlanner.SizeFleet.Desc", "Sets the number of buses on each line to the planner's suggestion based on line length and demand (the same way as the vehicle count slider in the line panel). When off, the game's default count is kept.")]
	public bool SizeFleet
	{
		get
		{
			return AutoTransitConfig.SizeFleet;
		}
		set
		{
			AutoTransitConfig.SizeFleet = value;
		}
	}

	[SettingsUISection("Main", "Automation")]
	[SettingsUIDisplayName("AutoTransitPlanner.FitToDepots", "Limit the plan to the bus depot capacity")]
	[SettingsUIDescription("AutoTransitPlanner.FitToDepots.Desc", "When on, lines and buses are planned to fit the number of buses your depots can hold, and every line keeps a usable frequency. Add depots and press the button again to grow the network. When off, the whole plan is built even if the depots cannot hold it.")]
	public bool FitToDepots
	{
		get
		{
			return AutoTransitConfig.FitToDepots;
		}
		set
		{
			AutoTransitConfig.FitToDepots = value;
		}
	}

	[SettingsUISection("Main", "Tuning")]
	[SettingsUIDropdown(typeof(Setting), "GetStopStyleItems")]
	[SettingsUIDisplayName("AutoTransitPlanner.StopStyle", "Bus stop type")]
	[SettingsUIDescription("AutoTransitPlanner.StopStyle.Desc", "Automatic picks the stop model for each location: busy stops and transfer points on wide enough roads get the most comfortable model (for example a shelter), all others the cheapest. Or use one model for every stop.")]
	public string StopStyle
	{
		get
		{
			return AutoTransitConfig.StopStyle;
		}
		set
		{
			AutoTransitConfig.StopStyle = value;
		}
	}

	[SettingsUISection("Main", "Tuning")]
	[SettingsUIDisplayName("AutoTransitPlanner.Shopping", "Include shopping trips")]
	[SettingsUIDescription("AutoTransitPlanner.Shopping.Desc", "Includes trips to shops and commercial areas in the planning.")]
	public bool Shopping
	{
		get
		{
			return AutoTransitConfig.Shopping;
		}
		set
		{
			AutoTransitConfig.Shopping = value;
		}
	}

	[SettingsUISection("Main", "Tuning")]
	[SettingsUIDisplayName("AutoTransitPlanner.Leisure", "Include leisure trips")]
	[SettingsUIDescription("AutoTransitPlanner.Leisure.Desc", "Includes trips to parks, restaurants and entertainment in the planning.")]
	public bool Leisure
	{
		get
		{
			return AutoTransitConfig.Leisure;
		}
		set
		{
			AutoTransitConfig.Leisure = value;
		}
	}

	[SettingsUISection("Main", "Tuning")]
	[SettingsUISlider(min = 200f, max = 700f, step = 25f)]
	[SettingsUIDisplayName("AutoTransitPlanner.WalkRadius", "Walking distance (m)")]
	[SettingsUIDescription("AutoTransitPlanner.WalkRadius.Desc", "How far passengers accept to walk to a stop. A smaller value means more stops.")]
	public int WalkRadius
	{
		get
		{
			return AutoTransitConfig.WalkRadius;
		}
		set
		{
			AutoTransitConfig.WalkRadius = value;
		}
	}

	[SettingsUISection("Main", "Tuning")]
	[SettingsUISlider(min = 5f, max = 150f, step = 5f)]
	[SettingsUIDisplayName("AutoTransitPlanner.MaxLines", "Maximum number of lines")]
	[SettingsUIDescription("AutoTransitPlanner.MaxLines.Desc", "The most bus lines the planner will build.")]
	public int MaxLines
	{
		get
		{
			return AutoTransitConfig.MaxLines;
		}
		set
		{
			AutoTransitConfig.MaxLines = value;
		}
	}

	[SettingsUISection("Main", "Tuning")]
	[SettingsUISlider(min = 3000f, max = 16000f, step = 500f)]
	[SettingsUIDisplayName("AutoTransitPlanner.MaxLineLength", "Longest line (m)")]
	[SettingsUIDescription("AutoTransitPlanner.MaxLineLength.Desc", "Upper length limit of a line loop.")]
	public int MaxLineLength
	{
		get
		{
			return AutoTransitConfig.MaxLineLength;
		}
		set
		{
			AutoTransitConfig.MaxLineLength = value;
		}
	}

	public Setting(IMod mod)
		: base(mod)
	{
		SetDefaults();
	}

	public DropdownItem<string>[] GetStopStyleItems()
	{
		List<DropdownItem<string>> list = new List<DropdownItem<string>>
		{
			new DropdownItem<string>
			{
				value = "Automatic",
				displayName = LocalizedString.Value("Automatic (by demand and road)")
			}
		};
		foreach (string availableStopStyle in AutoTransitConfig.AvailableStopStyles)
		{
			list.Add(new DropdownItem<string>
			{
				value = availableStopStyle,
				displayName = LocalizedString.Value(availableStopStyle)
			});
		}
		return list.ToArray();
	}

	public override void SetDefaults()
	{
		AutoTransitConfig.Enabled = false;
		AutoTransitConfig.RemoveOld = false;
		AutoTransitConfig.SizeFleet = true;
		AutoTransitConfig.FitToDepots = true;
		AutoTransitConfig.StopStyle = "Automatic";
		AutoTransitConfig.WalkRadius = 350;
		AutoTransitConfig.MaxLines = 60;
		AutoTransitConfig.MaxLineLength = 9000;
		AutoTransitConfig.Shopping = true;
		AutoTransitConfig.Leisure = true;
	}
}

