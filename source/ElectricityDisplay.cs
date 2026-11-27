using System;
using System.Reflection;
using Colossal.UI.Binding;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;

namespace IndustryMaster
{
    internal sealed class ElectricityDisplay : IDisposable
    {
        private readonly World _world;
        private readonly EntityQuery _producers;
        private object _productionBinding, _availabilityBinding, _oldWriter, _oldGetter;
        private FieldInfo _writerField, _getterField;
        internal long TotalRaw { get; private set; }
        internal int NegativeProducers { get; private set; }
        internal int NativeWrapped { get; private set; }
        internal string Description => Mod.Text("生产汇总 ", "Production total ") + (TotalRaw / 10000d).ToString("N2") + Mod.Text("兆瓦", " MW")
            + (TotalRaw > int.MaxValue ? Mod.Text("（已修正32位统计溢出）", " (32-bit display overflow corrected)") : "");
        internal ElectricityDisplay(World world)
        {
            _world = world;
            _producers = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ElectricityProducer>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
        }
        internal void Update()
        {
            var em = _world.EntityManager;
            em.CompleteDependencyBeforeRO<ElectricityProducer>();
            long total = 0; int negative = 0;
            using (var producers = _producers.ToComponentDataArray<ElectricityProducer>(Allocator.Temp))
                foreach (var producer in producers) { total += producer.m_Capacity; if (producer.m_Capacity < 0) negative++; }
            TotalRaw = total; NegativeProducers = negative;
            NativeWrapped = _world.GetExistingSystemManaged<ElectricityStatisticsSystem>()?.production ?? 0;
        }
        internal void Attach()
        {
            if (_productionBinding != null) return;
            var ui = _world.GetExistingSystemManaged<ElectricityInfoviewUISystem>();
            if (ui == null) return;
            // Everything is resolved into locals first; nothing is assigned to this instance until
            // every member and type has been validated, so a changed UI layout can be retried later.
            var productionField = typeof(ElectricityInfoviewUISystem).GetField("m_ElectricityProduction", BindingFlags.Instance | BindingFlags.NonPublic);
            var availabilityField = typeof(ElectricityInfoviewUISystem).GetField("m_ElectricityAvailability", BindingFlags.Instance | BindingFlags.NonPublic);
            var writerField = typeof(GetterValueBinding<int>).GetField("m_Writer", BindingFlags.Instance | BindingFlags.NonPublic);
            var getterField = typeof(GetterValueBinding<IndicatorValue>).GetField("m_Getter", BindingFlags.Instance | BindingFlags.NonPublic);
            if (productionField == null || availabilityField == null || writerField == null || getterField == null)
                throw new MissingMemberException("Power UI binding members changed");
            var productionBinding = productionField.GetValue(ui);
            var availabilityBinding = availabilityField.GetValue(ui);
            if (!(productionBinding is GetterValueBinding<int>) || !(availabilityBinding is GetterValueBinding<IndicatorValue>))
                throw new MissingMemberException("Power UI binding types changed");

            object oldWriter = writerField.GetValue(productionBinding);
            object oldGetter = getterField.GetValue(availabilityBinding);
            writerField.SetValue(productionBinding, new LongPowerWriter(this));
            getterField.SetValue(availabilityBinding, (Func<IndicatorValue>)(() => IndicatorValue.Calculate((float)TotalRaw,
                _world.GetExistingSystemManaged<ElectricityStatisticsSystem>().consumption)));

            _writerField = writerField; _getterField = getterField;
            _oldWriter = oldWriter; _oldGetter = oldGetter;
            _availabilityBinding = availabilityBinding;
            _productionBinding = productionBinding;
            Update();
            ((GetterValueBinding<int>)productionBinding).TriggerUpdate();
        }
        public void Dispose()
        {
            if (_productionBinding != null && _oldWriter != null) _writerField.SetValue(_productionBinding, _oldWriter);
            if (_availabilityBinding != null && _oldGetter != null) _getterField.SetValue(_availabilityBinding, _oldGetter);
            if (_world.IsCreated) _producers.Dispose();
        }
        private sealed class LongPowerWriter : IWriter<int>
        {
            private readonly ElectricityDisplay _owner;
            internal LongPowerWriter(ElectricityDisplay owner) { _owner = owner; }
            public void Write(IJsonWriter writer, int ignored) { _owner.Update(); writer.Write(_owner.TotalRaw); }
        }
    }
}
