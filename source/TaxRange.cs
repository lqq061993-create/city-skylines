using System;
using Game.Prefabs;
using Unity.Mathematics;

namespace IndustryMaster
{
    public static class TaxRange
    {
        public const int Minimum = -500;
        public const int Maximum = 100;

        public static int Clamp(int rate) => Math.Max(Minimum, Math.Min(Maximum, rate));

        public static TaxParameterData Expand(TaxParameterData data)
        {
            var range = new int2(Minimum, Maximum);
            data.m_TotalTaxLimits = range;
            data.m_ResidentialTaxLimits = range;
            data.m_CommercialTaxLimits = range;
            data.m_IndustrialTaxLimits = range;
            data.m_OfficeTaxLimits = range;
            data.m_JobLevelTaxLimits = range;
            data.m_ResourceTaxLimits = range;
            return data;
        }
    }
}
