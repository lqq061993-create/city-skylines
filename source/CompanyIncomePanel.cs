using System;
using Colossal.UI.Binding;
using Game.UI.InGame;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;

namespace IndustryMaster
{
    /// <summary>
    /// Feeds the virtual company income into the building info panel: the income row gets a
    /// receipt annotation and the profit row is corrected, because the game derives profit from
    /// produced goods only and never sees the money this mod pays directly into the company.
    /// </summary>
    internal static class CompanyIncomePanel
    {
        private static int _profitIndex = -1;

        internal static void Install(Harmony harmony)
        {
            var writer = AccessTools.DeclaredMethod(typeof(CompanySection), nameof(CompanySection.OnWriteProperties),
                new[] { typeof(IJsonWriter) });
            if (writer == null || AccessTools.Field(typeof(CompanySection), "companyEntity")?.FieldType != typeof(Entity))
                throw new MissingMemberException("Company panel binding changed");

            harmony.Patch(writer, postfix: new HarmonyMethod(typeof(CompanyIncomePanel), nameof(WriteReceipt)));

            // QueryCompanyData fills m_Results from CompanyStatisticData, whose m_Profit is
            // CompanyEconomyStatisticSystem's estimate (produced goods minus costs) and therefore
            // ignores income that was paid straight into the company account.
            var query = AccessTools.DeclaredMethod(typeof(CompanySection), "QueryCompanyData", new[] { typeof(Entity) });
            int profitIndex = ProfitIndex();
            if (query == null || AccessTools.Field(typeof(CompanySection), "m_Results")?.FieldType != typeof(NativeArray<int>)
                || profitIndex < 0)
                throw new MissingMemberException("Company panel profit binding changed");

            _profitIndex = profitIndex;
            harmony.Patch(query, postfix: new HarmonyMethod(typeof(CompanyIncomePanel), nameof(AddVirtualIncomeToProfit)));
        }

        private static int ProfitIndex()
        {
            Type resultType = AccessTools.Inner(typeof(CompanySection), "ResultType");
            if (resultType == null || !resultType.IsEnum) return -1;
            try { return Convert.ToInt32(Enum.Parse(resultType, "Profit")); }
            catch (ArgumentException) { return -1; }
        }

        private static void WriteReceipt(CompanySection __instance, IJsonWriter __0, Entity ___companyEntity)
        {
            if (___companyEntity == Entity.Null) return;
            int amount = VirtualIncome(__instance.World, ___companyEntity);
            if (amount <= 0) return;

            __0.PropertyName("industryMasterVirtualIncome");
            __0.Write(amount);
            __0.PropertyName("industryMasterVirtualIncomeLabel");
            __0.Write(Mod.Text("虚空收入", "Virtual income"));
        }

        private static void AddVirtualIncomeToProfit(CompanySection __instance, Entity entity, NativeArray<int> ___m_Results)
        {
            if (_profitIndex < 0 || !___m_Results.IsCreated || _profitIndex >= ___m_Results.Length) return;
            int amount = VirtualIncome(__instance.World, entity);
            if (amount == 0) return;

            // NativeArray is a view over unmanaged memory, so this writes the array the game reads.
            ___m_Results[_profitIndex] = DirectTradePolicy.AddSaturated(___m_Results[_profitIndex], amount);
        }

        private static int VirtualIncome(World world, Entity company)
        {
            if (company == Entity.Null) return 0;
            return world.GetExistingSystemManaged<VirtualCompanyIncomeSystem>()?.LatestPayment(company) ?? 0;
        }
    }
}
