using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Colossal.PSI.Environment;
using Game.Economy;
using Game.Prefabs;
using Unity.Entities;

namespace IndustryMaster
{
    public enum TradeResource
    {
        Grain, ConvenienceFood, Food, Vegetables, Meals, Wood, Timber, Paper, Furniture, Vehicles,
        Lodging, Oil, Petrochemicals, Ore, Plastics, Metals, Electronics, Software, Coal, Stone,
        Livestock, Cotton, Steel, Minerals, Concrete, Machinery, Chemicals, Pharmaceuticals,
        Beverages, Textiles, Telecom, Financial, Media, Entertainment, Recreation, Fish
    }
    public sealed class PriceQuote
    {
        public bool ImportEnabled, ExportEnabled;
        public float Import, Export;
    }
    internal sealed class TradePrices
    {
        private readonly World _world;
        private Dictionary<Resource, PriceQuote> _quotes;
        private Dictionary<Resource, float> _basePrices, _scales;
        private bool _hasImportQuote, _hasExportQuote;
        internal long ImportQuotes, ExportQuotes;
        internal string BookPath => Path.Combine(EnvPath.kUserDataPath, "ModsData", "IndustryMaster", "trade-prices.tsv");
        internal TradePrices(World world)
        {
            _world = world;
            _quotes = new Dictionary<Resource, PriceQuote>();
            _basePrices = new Dictionary<Resource, float>(); _scales = new Dictionary<Resource, float>();
            Load();
            UpdateDirections();
        }
        internal static Resource ResourceOf(TradeResource value) => (Resource)Enum.Parse(typeof(Resource), value.ToString());
        internal void Refresh()
        {
            var em = _world.EntityManager;
            var resources = _world.GetExistingSystemManaged<ResourceSystem>();
            if (resources == null) return;
            var prefabs = resources.GetPrefabs();
            var bases = new Dictionary<Resource, float>(); var scales = new Dictionary<Resource, float>();
            foreach (TradeResource value in Enum.GetValues(typeof(TradeResource)))
            {
                Resource resource = ResourceOf(value); Entity entity = prefabs[resource];
                if (entity == Entity.Null || !em.HasComponent<ResourceData>(entity)) continue;
                var data = em.GetComponentData<ResourceData>(entity);
                bases[resource] = data.m_Price.x;
                scales[resource] = data.m_Weight > 0 ? 1000 : 1;
            }
            _basePrices = bases; _scales = scales;
        }
        internal bool IsOutside(Entity entity) => entity != Entity.Null && _world.EntityManager.HasComponent<Game.Objects.OutsideConnection>(entity);
        internal float ScaleOf(Resource resource) { float value; return _scales.TryGetValue(resource, out value) ? value : 1000; }
        internal float BaseUnit(Resource resource) { float value; return _basePrices.TryGetValue(resource, out value) ? value : 0; }
        internal PriceQuote Get(TradeResource selected)
        {
            Resource resource = ResourceOf(selected); PriceQuote value;
            if (_quotes.TryGetValue(resource, out value)) return new PriceQuote { ImportEnabled = value.ImportEnabled, ExportEnabled = value.ExportEnabled, Import = value.Import, Export = value.Export };
            float native = BaseUnit(resource) * ScaleOf(resource);
            return new PriceQuote { Import = native, Export = native };
        }
        internal void Set(TradeResource selected, PriceQuote value)
        {
            var next = new Dictionary<Resource, PriceQuote>(_quotes);
            Resource resource = ResourceOf(selected);
            if (!value.ImportEnabled && !value.ExportEnabled) next.Remove(resource);
            else next[resource] = new PriceQuote { ImportEnabled = value.ImportEnabled, ExportEnabled = value.ExportEnabled,
                Import = Clean(value.Import), Export = Clean(value.Export) };
            Save(next);
            _quotes = next;
            UpdateDirections();
        }
        private static float Clean(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1000000, value));

        private void UpdateDirections()
        {
            _hasImportQuote = _hasExportQuote = false;
            foreach (var quote in _quotes.Values)
            {
                _hasImportQuote |= quote.ImportEnabled;
                _hasExportQuote |= quote.ExportEnabled;
            }
        }

        internal bool HasAdjustments(bool import) => Mod.TradePricesEnabled
            && ((import ? _hasImportQuote : _hasExportQuote) || Mod.Current.Settings.PriceMultiplier(import) != 1f);

        internal bool TryUnitPrice(Resource resource, bool import, double nativePrice, out double price)
        {
            price = nativePrice;
            if (!Mod.TradePricesEnabled) return false;
            float multiplier = Mod.Current.Settings.PriceMultiplier(import);
            bool fixedPrice = _quotes.TryGetValue(resource, out var quote) && (import ? quote.ImportEnabled : quote.ExportEnabled);
            if (multiplier == 1f && !fixedPrice) return false;
            double basePrice = fixedPrice ? (double)(import ? quote.Import : quote.Export) / ScaleOf(resource) : nativePrice;
            price = Policy.MultiplyPrice(basePrice, multiplier);
            return true;
        }

        private void Save(Dictionary<Resource, PriceQuote> quotes)
        {
            string path = BookPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var result = new StringBuilder("Resource\tImportEnabled\tImportPrice\tExportEnabled\tExportPrice\n");
            foreach (var pair in quotes)
                result.Append(pair.Key).Append('\t').Append(pair.Value.ImportEnabled).Append('\t').Append(pair.Value.Import.ToString("R", CultureInfo.InvariantCulture))
                    .Append('\t').Append(pair.Value.ExportEnabled).Append('\t').Append(pair.Value.Export.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, result.ToString(), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private void Load()
        {
            if (!File.Exists(BookPath)) return;
            foreach (string line in File.ReadAllLines(BookPath))
            {
                var p = line.Split('\t'); Resource resource; bool import, export; float a, b;
                if (p.Length != 5 || !Enum.TryParse(p[0], out resource) || !bool.TryParse(p[1], out import) || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                    || !bool.TryParse(p[3], out export) || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) continue;
                if (import || export) _quotes[resource] = new PriceQuote { ImportEnabled = import, ExportEnabled = export, Import = Clean(a), Export = Clean(b) };
            }
        }
        internal string Describe(TradeResource selected)
        {
            var resource = ResourceOf(selected); var quote = Get(selected);
            string unit = ScaleOf(resource) == 1000 ? Mod.Text("元/吨", "per tonne") : Mod.Text("元/单位", "per unit");
            return Mod.Text("原版基价 ", "Native base ") + (BaseUnit(resource) * ScaleOf(resource)).ToString("0.##") + " " + unit
                + Mod.Text("。已应用进口价 ", ". Applied import quote ") + (quote.ImportEnabled ? quote.Import.ToString("0.##") : Mod.Text("原版", "native"))
                + Mod.Text("，出口 ", ", export ") + (quote.ExportEnabled ? quote.Export.ToString("0.##") : Mod.Text("原版", "native"));
        }
    }
}
