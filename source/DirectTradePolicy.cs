using System;

namespace IndustryMaster
{
    internal readonly struct DirectTradeInvoice
    {
        internal readonly int Amount;
        internal readonly int Payment;

        internal DirectTradeInvoice(int amount, int payment)
        {
            Amount = amount;
            Payment = payment;
        }
    }

    internal static class DirectTradePolicy
    {
        internal static DirectTradeInvoice PlanImport(int requested, int stock, long storageRoom, int cash, double unitPrice)
        {
            long available = Math.Min(storageRoom, (long)int.MaxValue - stock);
            return Plan(requested, available, (long)cash - int.MinValue, unitPrice);
        }

        internal static DirectTradeInvoice PlanExport(int requested, int stock, long reserve, int cash, double unitPrice)
        {
            return Plan(requested, stock - Math.Max(0, reserve), (long)int.MaxValue - cash, unitPrice);
        }

        private static DirectTradeInvoice Plan(int requested, long available, long moneyRoom, double unitPrice)
        {
            if (requested <= 0 || available <= 0 || unitPrice < 0 || double.IsNaN(unitPrice) || double.IsInfinity(unitPrice))
                return default;

            int amount = (int)Math.Min(requested, available);
            long limit = Math.Min(moneyRoom, Policy.MaximumTradeInvoice);
            double payment = Math.Round(amount * unitPrice);
            if (payment > limit)
            {
                amount = (int)Math.Min(amount, Math.Floor(limit / unitPrice));
                payment = Math.Round(amount * unitPrice);
            }
            return new DirectTradeInvoice(amount, (int)payment);
        }

        internal static bool ShouldRefill(bool directImport, bool directExport, bool isInput, bool isOutput)
        {
            return !(directImport && isInput) && !(directExport && isOutput);
        }

        internal static int AddSaturated(int value, int change)
        {
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value + change));
        }
    }
}
