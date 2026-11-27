using System;
using System.Collections.Generic;
using AutoTransitPlanner.Transit;
using Colossal.Localization;
using Game.SceneFlow;

namespace AutoTransitPlanner;

internal static class OptionsText
{
	public static void Register(Setting setting)
	{
		try
		{
			Dictionary<string, string> dict = new Dictionary<string, string>
			{
				[setting.GetSettingsLocaleID()] = "Auto Transit Planner",
				[setting.GetOptionTabLocaleID("Main")] = "Main",
				[setting.GetOptionGroupLocaleID("Actions")] = "Actions",
				[setting.GetOptionGroupLocaleID("Automation")] = "Automation",
				[setting.GetOptionGroupLocaleID("Tuning")] = "Tuning"
			};
			LocalizationManager localizationManager = GameManager.instance?.localizationManager;
			if (localizationManager == null)
			{
				Mod.Log.Warn("Options titles: the game has no localization manager yet");
			}
			else
			{
				localizationManager.AddSource(localizationManager.fallbackLocaleId, new MemorySource(dict));
			}
		}
		catch (Exception ex)
		{
			Mod.Log.Warn("Options titles could not be registered: " + ex.Message);
		}
	}
}
