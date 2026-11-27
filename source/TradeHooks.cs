using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;
using Unity.Jobs;

namespace IndustryMaster
{
    // These three settlement jobs normally run through precompiled Burst code, which
    // ignores managed Harmony patches. Only when the new features need them, execute
    // their ORIGINAL Execute methods synchronously after their native dependencies.
    internal static class TradeHooks
    {
        internal static void Install(Harmony harmony)
        {
            var buyer = typeof(ResourceBuyerSystem).GetNestedType("BuyJob", BindingFlags.NonPublic);
            var exporter = typeof(ResourceExporterSystem).GetNestedType("HandleExportsJob", BindingFlags.NonPublic);
            var delivery = typeof(DeliveryTruckAISystem).GetNestedType("DeliverJob", BindingFlags.NonPublic);
            if (buyer == null || exporter == null || delivery == null) throw new MissingMemberException("Trade settlement jobs changed");
            harmony.Patch(AccessTools.DeclaredMethod(buyer, "Execute"), transpiler: new HarmonyMethod(typeof(TradeHooks), nameof(ImportPriceCode)));
            harmony.Patch(AccessTools.DeclaredMethod(exporter, "Execute"), transpiler: new HarmonyMethod(typeof(TradeHooks), nameof(ExportPriceCode)));
            harmony.Patch(AccessTools.DeclaredMethod(delivery, "Execute"), transpiler: new HarmonyMethod(typeof(TradeHooks), nameof(DeliveryPriceCode)));
            foreach (Type system in new[] { typeof(ResourceBuyerSystem), typeof(ResourceExporterSystem), typeof(DeliveryTruckAISystem.Actions) })
                harmony.Patch(AccessTools.DeclaredMethod(system, "OnUpdate"), transpiler: new HarmonyMethod(typeof(TradeHooks), nameof(ScheduleCode)));
        }
        private static IEnumerable<CodeInstruction> ScheduleCode(IEnumerable<CodeInstruction> instructions)
        {
            int matches = 0;
            foreach (var code in instructions)
            {
                var method = code.operand as MethodInfo;
                if (method != null && method.IsGenericMethod && method.Name == "Schedule" && method.DeclaringType == typeof(IJobExtensions))
                {
                    Type job = method.GetGenericArguments()[0];
                    if (job.Name == "BuyJob" || job.Name == "HandleExportsJob" || job.Name == "DeliverJob")
                    {
                        code.operand = AccessTools.Method(typeof(TradeHooks), nameof(ScheduleManaged)).MakeGenericMethod(job); matches++;
                    }
                }
                yield return code;
            }
            if (matches != 1) throw new InvalidOperationException("Expected exactly one settlement scheduling call; found " + matches);
        }
        public static JobHandle ScheduleManaged<T>(T job, JobHandle dependsOn) where T : struct, IJob
        {
            string jobName = typeof(T).Name;
            var prices = Mod.Current?.Prices;
            bool full = jobName == "DeliverJob" && Mod.FullLoadingActive;
            bool adjustPrice = jobName switch
            {
                "BuyJob" => prices?.HasAdjustments(true) == true,
                "HandleExportsJob" => prices?.HasAdjustments(false) == true,
                _ => prices?.HasAdjustments(true) == true || prices?.HasAdjustments(false) == true
            };
            if (!adjustPrice && !full) return job.Schedule(dependsOn);
            dependsOn.Complete();
            if (full) Mod.Current.BeforeDeliveryActions(job);
            job.Execute();
            if (full) Mod.Current.AfterDeliveryActions();
            return default(JobHandle);
        }
        private static CodeInstruction LocalAddress(MethodBase method, string name)
        {
            var local = method.GetMethodBody().LocalVariables.Single(v => v.LocalType.Name == name);
            return new CodeInstruction(OpCodes.Ldloca_S, checked((byte)local.LocalIndex));
        }
        private static bool PriceCall(CodeInstruction code)
        {
            var method = code.operand as MethodInfo;
            return method != null && method.DeclaringType == typeof(EconomyUtils) && (method.Name == "GetIndustrialPrice" || method.Name == "GetMarketPrice");
        }
        private static IEnumerable<CodeInstruction> ImportPriceCode(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            Type eventType = __originalMethod.DeclaringType.DeclaringType.GetNestedType("SalesEvent", BindingFlags.NonPublic);
            int matches = 0;
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var code = list[i];
                var method = code.operand as MethodInfo;
                bool payment = method != null && method.DeclaringType == typeof(UnityEngine.Mathf) && method.Name == "RoundToInt"
                    && i + 1 < list.Count && list[i + 1].opcode == OpCodes.Neg;
                if (!payment) { yield return code; continue; }
                matches++;
                var load = list[i - 1];
                int moneyLocal;
                if (load.opcode == OpCodes.Ldloc_0) moneyLocal = 0;
                else if (load.opcode == OpCodes.Ldloc_1) moneyLocal = 1;
                else if (load.opcode == OpCodes.Ldloc_2) moneyLocal = 2;
                else if (load.opcode == OpCodes.Ldloc_3) moneyLocal = 3;
                else if (load.operand is LocalBuilder) moneyLocal = ((LocalBuilder)load.operand).LocalIndex;
                else moneyLocal = Convert.ToInt32(load.operand);
                bool first = true;
                foreach (string field in new[] { "m_Resource", "m_Amount", "m_Flags", "m_Seller" })
                {
                    var address = LocalAddress(__originalMethod, "SalesEvent");
                    if (first) { address.labels.AddRange(code.labels); code.labels.Clear(); first = false; }
                    yield return address; yield return new CodeInstruction(OpCodes.Ldfld, eventType.GetField(field));
                }
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradeHooks), nameof(ImportTotal)));
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Stloc_S, checked((byte)moneyLocal));
                yield return code;
            }
            if (matches != 1) throw new InvalidOperationException("Import payment call contract changed: " + matches);
        }
        private static IEnumerable<CodeInstruction> ExportPriceCode(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            Type eventType = __originalMethod.DeclaringType.DeclaringType.GetNestedType("ExportEvent", BindingFlags.NonPublic);
            int matches = 0, bounds = 0;
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var code = list[i];
                if (code.opcode == OpCodes.Conv_I4 && i > 0 && list[i - 1].opcode == OpCodes.Div)
                { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(TradeHooks), nameof(SafeQuantityBound)); bounds++; }
                yield return code;
                var costField = code.operand as FieldInfo;
                if (code.opcode == OpCodes.Ldfld && costField != null && costField.DeclaringType == typeof(TradeCost) && costField.Name == "m_BuyCost")
                {
                    foreach (string field in new[] { "m_Resource", "m_Buyer" })
                    { yield return LocalAddress(__originalMethod, "ExportEvent"); yield return new CodeInstruction(OpCodes.Ldfld, eventType.GetField(field)); }
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradeHooks), nameof(VirtualExportCost)));
                }
                if (!PriceCall(code)) continue;
                matches++;
                foreach (string field in new[] { "m_Resource", "m_Amount", "m_Buyer" })
                { yield return LocalAddress(__originalMethod, "ExportEvent"); yield return new CodeInstruction(OpCodes.Ldfld, eventType.GetField(field)); }
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradeHooks), nameof(ExportPrice)));
            }
            if (matches != 1 || bounds != 1) throw new InvalidOperationException("Export price/bound contract changed: " + matches + "/" + bounds);
        }
        private static IEnumerable<CodeInstruction> DeliveryPriceCode(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int matches = 0;
            foreach (var code in instructions)
            {
                yield return code;
                if (!PriceCall(code)) continue;
                matches++;
                var local = __originalMethod.GetMethodBody().LocalVariables.Single(v => v.LocalType == typeof(DeliveryTruckAISystem.DeliveredStack));
                yield return new CodeInstruction(OpCodes.Ldloc_S, checked((byte)local.LocalIndex));
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TradeHooks), nameof(DeliveryPrice)));
            }
            if (matches != 1) throw new InvalidOperationException("Truck settlement price contract changed: " + matches);
        }
        public static int SafeQuantityBound(float value) => float.IsNaN(value) ? 1000 : (int)Math.Max(1000, Math.Min((double)int.MaxValue - 1000, value));
        public static float ImportTotal(float original, Resource resource, int amount, int flags, Entity seller)
        {
            var prices = Mod.Current?.Prices;
            if (prices == null || amount <= 0 || ((flags & 2) == 0 && !prices.IsOutside(seller))) return original;
            if (!prices.TryUnitPrice(resource, true, original / (double)amount, out double target)) return original;
            prices.ImportQuotes++;
            // Override the accepted invoice total immediately before native money writes.
            // The same local then feeds seller receipts and company cost statistics.
            return Policy.QuoteTotal(target, amount);
        }
        public static float ExportPrice(float original, Resource resource, int amount, Entity buyer)
        {
            var prices = Mod.Current?.Prices;
            // This job pays virtual exports. Physical exports are paid on delivery.
            if (prices == null || buyer != Entity.Null || !prices.TryUnitPrice(resource, false, original, out double target)) return original;
            prices.ExportQuotes++;
            // Virtual export applies the selected outside connection's legacy buy cost.
            // Keep its goods base overridden; zero-price quotes are safe via the bound fix.
            return Policy.QuotePerUnit(target, amount);
        }
        public static float VirtualExportCost(float cost, Resource resource, Entity buyer)
        {
            return buyer == Entity.Null && Mod.Current?.Prices?.TryUnitPrice(resource, false, 0, out _) == true ? 0 : cost;
        }
        public static float DeliveryPrice(float original, DeliveryTruckAISystem.DeliveredStack item)
        {
            var prices = Mod.Current?.Prices;
            if (prices == null || item.buildingUpkeep || item.storageTransfer || item.amount <= 0) return original;
            bool destinationOutside = prices.IsOutside(item.target), payerOutside = prices.IsOutside(item.costPayer);
            if (destinationOutside == payerOutside) return original;
            bool import = item.moneyRefund ? destinationOutside : payerOutside;
            if (!prices.TryUnitPrice(item.resource, import, original, out double target)) return original;
            if (import) prices.ImportQuotes++; else prices.ExportQuotes++;
            return Policy.QuotePerUnit(target, item.amount);
        }
    }
}
