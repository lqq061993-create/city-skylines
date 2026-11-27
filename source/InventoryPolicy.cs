using System;
namespace IndustryMaster
{
    public enum InventoryMode { Off, Full, TenPercentPerSecond }
    public static class InventoryPolicy
    {
        public static int[] Targets(int capacity, int[] amounts, long otherPhysical, bool full)
        {
            var result = (int[])amounts.Clone();
            FillTargets(capacity, amounts, amounts.Length, otherPhysical, full, result);
            return result;
        }

        internal static void FillTargets(int capacity, int[] amounts, int count, long otherPhysical, bool full, int[] result)
        {
            if (capacity <= 0 || count == 0)
            {
                Array.Copy(amounts, result, count);
                return;
            }
            long used = Math.Max(0, otherPhysical);
            for (int i = 0; i < count; i++) used += Math.Max(0, amounts[i]);
            long room = Math.Max(0, (long)capacity - used);
            int share = capacity / count;
            int remainder = capacity % count;
            for (int i = 0; i < count; i++)
            {
                int current = Math.Max(0, amounts[i]);
                long target = share + (i < remainder ? 1L : 0L);
                long gap = Math.Max(0, target - current);
                long amount = full ? gap : Math.Min(gap, (target + 9) / 10);
                amount = Math.Min(amount, room);
                room -= amount;
                result[i] = (int)Math.Min(int.MaxValue, current + amount);
            }
        }
    }
}
