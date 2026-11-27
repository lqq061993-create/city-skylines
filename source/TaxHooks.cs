using System;
using System.Reflection;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;

namespace IndustryMaster
{
    internal static class TaxHooks
    {
        private static readonly FieldInfo ParameterCache = AccessTools.Field(typeof(TaxSystem), "m_TaxParameterData");

        internal static void Install(Harmony harmony)
        {
            if (ParameterCache == null || ParameterCache.FieldType != typeof(TaxParameterData))
                throw new MissingFieldException("TaxSystem.m_TaxParameterData changed");

            var getter = AccessTools.DeclaredMethod(typeof(TaxSystem), nameof(TaxSystem.GetTaxParameterData));
            if (getter == null || getter.ReturnType != typeof(TaxParameterData))
                throw new MissingMethodException("Native tax limit getter changed");

            var setters = SetterMethods();
            var limitsPatch = new HarmonyMethod(typeof(TaxHooks), nameof(LimitsPostfix)) { priority = Priority.Last };
            var totalPatch = new HarmonyMethod(typeof(TaxHooks), nameof(TotalPrefix)) { priority = Priority.Last };
            var groupPatch = new HarmonyMethod(typeof(TaxHooks), nameof(GroupPrefix)) { priority = Priority.Last };
            harmony.Patch(getter, postfix: limitsPatch);
            harmony.Patch(setters[0], prefix: totalPatch);
            for (int i = 1; i < setters.Length; i++)
                harmony.Patch(setters[i], prefix: groupPatch);
        }

        internal static MethodInfo[] SetterMethods()
        {
            var methods = new[]
            {
                AccessTools.PropertySetter(typeof(TaxSystem), nameof(TaxSystem.TaxRate)),
                AccessTools.DeclaredMethod(typeof(TaxSystem), nameof(TaxSystem.SetTaxRate), new[] { typeof(TaxAreaType), typeof(int) }),
                AccessTools.DeclaredMethod(typeof(TaxSystem), nameof(TaxSystem.SetResidentialTaxRate), new[] { typeof(int), typeof(int) }),
                AccessTools.DeclaredMethod(typeof(TaxSystem), nameof(TaxSystem.SetCommercialTaxRate), new[] { typeof(Resource), typeof(int) }),
                AccessTools.DeclaredMethod(typeof(TaxSystem), nameof(TaxSystem.SetIndustrialTaxRate), new[] { typeof(Resource), typeof(int) }),
                AccessTools.DeclaredMethod(typeof(TaxSystem), nameof(TaxSystem.SetOfficeTaxRate), new[] { typeof(Resource), typeof(int) })
            };
            foreach (var method in methods)
                if (method == null || method.ReturnType != typeof(void))
                    throw new MissingMethodException("Native tax setter contract changed");
            return methods;
        }

        private static void LimitsPostfix(ref TaxParameterData __result)
        {
            if (__result.m_TotalTaxLimits.x != __result.m_TotalTaxLimits.y)
                __result = TaxRange.Expand(__result);
        }

        private static void TotalPrefix(ref TaxParameterData ___m_TaxParameterData, ref int __0)
        {
            ___m_TaxParameterData = TaxRange.Expand(___m_TaxParameterData);
            __0 = TaxRange.Clamp(__0);
        }

        private static void GroupPrefix(ref TaxParameterData ___m_TaxParameterData, ref int __1)
        {
            ___m_TaxParameterData = TaxRange.Expand(___m_TaxParameterData);
            __1 = TaxRange.Clamp(__1);
        }

        internal static void ResetCache(World world)
        {
            if (world == null || !world.IsCreated || ParameterCache == null)
                return;
            var tax = world.GetExistingSystemManaged<TaxSystem>();
            if (tax != null)
                ParameterCache.SetValue(tax, default(TaxParameterData));
        }
    }
}
