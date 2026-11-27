using System;

namespace IndustryMaster
{
    public static class Policy
    {
        public const int StorageKg = 10000000;
        public const int MaximumTradeInvoice = 2000000000;
        public static int TruckKg(int tonnes) => Math.Max(100, Math.Min(1000, tonnes)) * 1000;
        public static int VehicleSlots(int original) => original <= 0 ? original : Math.Max(50, original);
        public static int Scale(int original, float multiplier)
        {
            if (original <= 0) return original;
            double scale = float.IsNaN(multiplier) ? 1 : Math.Max(0.1, Math.Min(100, multiplier));
            return (int)Math.Max(1, Math.Min(100000000, Math.Round(original * scale, MidpointRounding.AwayFromZero)));
        }

        public static float PriceMultiplier(float value) => float.IsNaN(value) ? 1f : Math.Max(0f, Math.Min(100f, value));

        public static double MultiplyPrice(double basePrice, float multiplier)
        {
            float factor = PriceMultiplier(multiplier);
            if (factor == 0 || double.IsNaN(basePrice) || basePrice <= 0)
                return 0;
            return Math.Min(MaximumTradeInvoice, basePrice * factor);
        }

        public static float QuotePerUnit(double price, int amount) =>
            (float)Math.Min(price, (double)MaximumTradeInvoice / Math.Max(1, amount));

        public static float QuoteTotal(double price, int amount) =>
            (float)Math.Min(MaximumTradeInvoice, price * Math.Max(0, amount));

        public static int ExportAmount(int stock, int inputReserve, int capacity) =>
            (int)Math.Max(0, Math.Min(Math.Max(0L, (long)stock - inputReserve), Math.Max(0, capacity)));
    }
}
