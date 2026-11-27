using AutoTransitPlanner.Transit;
using Colossal.Logging;
using Game;
using Game.Modding;

namespace AutoTransitPlanner;

public class Mod : IMod
{
	public static ILog Log { get; } = LogManager.GetLogger("AutoTransitPlanner").SetShowsErrorsInUI(showsErrorsInUI: false);


	public static Setting Settings { get; private set; }

	public void OnLoad(UpdateSystem updateSystem)
	{
		Log.Info("0.3.0-local loaded. New bus lines and existing line repairs require successful game pathfinding for every leg.");
		Settings = new Setting(this);
		Settings.RegisterInOptionsUI();
		OptionsText.Register(Settings);
		updateSystem.UpdateAt<AutoTransitToolSystem>(SystemUpdatePhase.ToolUpdate);
		updateSystem.UpdateAt<AutoTransitController>(SystemUpdatePhase.GameSimulation);
		updateSystem.UpdateAt<FleetSizingSystem>(SystemUpdatePhase.GameSimulation);
	}

	public void OnDispose()
	{
		Log.Info("OnDispose");
		Settings?.UnregisterInOptionsUI();
		Settings = null;
	}
}
