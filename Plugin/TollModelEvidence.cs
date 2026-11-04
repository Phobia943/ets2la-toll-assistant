using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ETS2LA.Game.Data;
using ETS2LA.Game.SiiFiles;
using ETS2LA.Game.Utils;
using TruckLib;
using TruckLib.Models;
using TruckLib.Models.Ppd;

namespace MautAssistent;

public sealed class TollModelEvidence(Func<IFileSystem?> readFiles)
{
    IFileSystem? files;
    Dictionary<string,string>? models;
    readonly Dictionary<(string Model, string Variant), IReadOnlyDictionary<int,TollPayment>> cache = new();
    static readonly IReadOnlyDictionary<int,TollPayment> Unknown = new Dictionary<int,TollPayment>();

    public IReadOnlyDictionary<int,TollPayment> Read(ParsedPrefab prefab)
    {
        var current = readFiles();
        if (current == null || prefab.Descriptor == null || prefab.Prefab.AdditionalParts.Count > 0) return Unknown;
        if (!ReferenceEquals(current, files)) { files = current; models = null; cache.Clear(); }
        var key = (prefab.Prefab.Model.ToString(), prefab.Prefab.Variant.ToString());
        if (cache.TryGetValue(key, out var known)) return known;
        try
        {
            models ??= ReadModels(current);
            if (!models.TryGetValue(key.Item1, out var pmd)) return cache[key] = Unknown;
            var model = TruckLib.Models.Model.Open(pmd, current);
            var variants = model.Variants.Where(v => v.Name.ToString() == key.Item2).ToArray();
            if (variants.Length != 1 || variants[0].Attributes.Count != model.Parts.Count ||
                variants[0].Attributes.Any(a => a.Tag.ToString() != "visible" || a.Value > 1)) return cache[key] = Unknown;
            var locators = model.Parts.Where((_, i) => variants[0].Attributes[i].Value == 1)
                .SelectMany(p => p.Locators);
            var pmg = current.ReadAllBytes(Path.ChangeExtension(pmd, ".pmg"));
            var markers = locators.Select(l => (l.Position, Payment: DecodeHookup(pmg, l.HookupOffset)))
                .Where(l => l.Payment != TollPayment.Unknown);
            return cache[key] = Match(prefab.Descriptor, markers);
        }
        catch (Exception ex)
        {
            ETS2LA.Logging.Logger.Warn("Maut-Zahlart konnte nicht aus dem Modell gelesen werden: " + ex.Message);
            return cache[key] = Unknown;
        }
    }

    static Dictionary<string,string> ReadModels(IFileSystem files)
    {
        var models = new Dictionary<string,string>();
        foreach (var path in files.GetFiles("/def/world/").Where(f => f.StartsWith("/def/world/prefab.") && f.EndsWith(".sii")))
        {
            var definitions = SiiFileHandler.Current.GetSiiFile(path);
            if (definitions == null) continue;
            foreach (var unit in definitions.Units)
            {
                if (!unit.Attributes.TryGetValue("model_desc", out var model)) continue;
                string? pmd = ((object?)model)?.ToString()?.Trim('"');
                if (pmd?.StartsWith("/prefab2/tollgate/") != true || !pmd.EndsWith(".pmd")) continue;
                models.TryAdd(unit.Name.Split('.').Last(), pmd);
            }
        }
        return models;
    }

    // PMG v21: locator offsets point into the null-terminated hookup table.
    public static TollPayment DecodeHookup(ReadOnlySpan<byte> pmg, int offset)
    {
        if (pmg.Length < 96 || pmg[0] != 21 || offset < 0) return TollPayment.Unknown;
        int table = BinaryPrimitives.ReadInt32LittleEndian(pmg[88..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(pmg[92..]);
        if (table < 96 || length <= offset || table > pmg.Length || length > pmg.Length - table)
            return TollPayment.Unknown;
        if (offset != 0 && pmg[table + offset - 1] != 0) return TollPayment.Unknown;
        var tail = pmg.Slice(table + offset, length - offset);
        int end = tail.IndexOf((byte)0);
        if (end < 1 || end > 64) return TollPayment.Unknown;
        return Encoding.ASCII.GetString(tail[..end]) switch
        {
            "tollgate.autopass.custom" or "tollgate.autopass.oversize" => TollPayment.Automatic,
            "tollgate.custom" => TollPayment.Manual,
            _ => TollPayment.Unknown
        };
    }

    public static IReadOnlyDictionary<int,TollPayment> Match(PrefabDescriptor descriptor,
        IEnumerable<(Vector3 Position, TollPayment Payment)> markers)
    {
        var curves = descriptor.NavCurves;
        var uniqueGates = descriptor.Semaphores.GroupBy(s => (int)s.SemaphoreId)
            .Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        int? GateAhead(int index)
        {
            var visited = new HashSet<int>();
            while (index >= 0 && index < curves.Count && visited.Add(index))
            {
                var curve = curves[index];
                if (curve.SemaphoreId >= 0) return uniqueGates.Contains(curve.SemaphoreId) ? curve.SemaphoreId : null;
                if (curve.NextLines.Count != 1) return null;
                index = curve.NextLines[0];
            }
            return null;
        }
        var paths = curves.Select((curve, i) => (curve, Gate: GateAhead(i))).ToArray();
        var evidence = new Dictionary<int, HashSet<TollPayment>>();
        foreach (var marker in markers)
        {
            if (marker.Payment == TollPayment.Unknown) continue;
            var near = paths.Select(path =>
            {
                int count = Math.Clamp((int)Math.Ceiling(path.curve.Length * 2), 4, 600);
                float distance = Enumerable.Range(0, count + 1).Min(i => Vector3.Distance(
                    PrefabUtils.InterpolateNavCurveOriented(path.curve, (float)i/count).Position, marker.Position));
                return (path.Gate, distance);
            }).Where(x => x.distance < 2.4f).OrderBy(x => x.distance).ToArray();
            if (near.Length == 0 || near[0].distance > 1.2f || near[0].Gate is not int gate ||
                near.Any(x => x.Gate != gate && x.distance - near[0].distance < .85f)) continue;
            if (!evidence.TryGetValue(gate, out var kinds)) evidence[gate] = kinds = new();
            kinds.Add(marker.Payment);
        }
        return evidence.Where(e => e.Value.Count == 1).ToDictionary(e => e.Key, e => e.Value.Single());
    }
}
