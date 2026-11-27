using GroundUp.Core.Gear;

namespace GroundUp.Core.Climbing;

public readonly record struct RackEntry(GearSpecBase Gear, int Quantity);

/// <summary>
/// Tracks trad, aid, and runner hardware carried on the climber's harness during a ground-up ascent.
/// </summary>
public sealed class RackInventory
{
    private readonly Dictionary<string, (GearSpecBase Spec, int Count)> _stock = new(StringComparer.Ordinal);
    private readonly List<string> _orderedCodes = [];

    public RackInventory(IEnumerable<RackEntry> initialEntries)
    {
        ArgumentNullException.ThrowIfNull(initialEntries);
        foreach (RackEntry entry in initialEntries)
        {
            if (entry.Quantity <= 0)
            {
                continue;
            }

            if (_stock.TryGetValue(entry.Gear.Code, out var existing))
            {
                _stock[entry.Gear.Code] = (existing.Spec, existing.Count + entry.Quantity);
            }
            else
            {
                _stock[entry.Gear.Code] = (entry.Gear, entry.Quantity);
                _orderedCodes.Add(entry.Gear.Code);
            }
        }
    }

    public static RackInventory CreateDefaultGroundUpRack() => new(
    [
        new RackEntry(StandardRack.Cam01, 1),
        new RackEntry(StandardRack.Cam02, 1),
        new RackEntry(StandardRack.Cam03, 1),
        new RackEntry(StandardRack.Cam04, 1),
        new RackEntry(StandardRack.Cam05, 2),
        new RackEntry(StandardRack.Cam075, 2),
        new RackEntry(StandardRack.Cam1, 2),
        new RackEntry(StandardRack.Cam2, 2),
        new RackEntry(StandardRack.Cam3, 1),
        new RackEntry(StandardRack.Cam4, 1),

        new RackEntry(StandardRack.Knifeblade, 2),
        new RackEntry(StandardRack.LostArrow, 2),
        new RackEntry(StandardRack.AnglePiton, 1),

        new RackEntry(StandardRack.TalonHook, 2),
        new RackEntry(StandardRack.CliffhangerHook, 2),
        new RackEntry(StandardRack.WideRingHook, 1),

        new RackEntry(StandardRack.Dyneema60, 3),
        new RackEntry(StandardRack.Nylon120, 2)
    ]);

    public IReadOnlyList<RackEntry> GetAllEntries() =>
        _orderedCodes
            .Select(code => _stock[code])
            .Select(item => new RackEntry(item.Spec, item.Count))
            .ToArray();

    public IReadOnlyList<RackEntry> GetEntriesByKind(GearKind kind) =>
        _orderedCodes
            .Select(code => _stock[code])
            .Where(item => item.Spec.Kind == kind)
            .Select(item => new RackEntry(item.Spec, item.Count))
            .ToArray();

    public int GetQuantity(string gearCode) =>
        _stock.TryGetValue(gearCode, out var entry) ? entry.Count : 0;

    public bool TryConsume(GearSpecBase gear)
    {
        ArgumentNullException.ThrowIfNull(gear);
        if (!_stock.TryGetValue(gear.Code, out var existing) || existing.Count <= 0)
        {
            return false;
        }

        _stock[gear.Code] = (existing.Spec, existing.Count - 1);
        return true;
    }

    public void ReturnGear(GearSpecBase gear)
    {
        ArgumentNullException.ThrowIfNull(gear);
        if (_stock.TryGetValue(gear.Code, out var existing))
        {
            _stock[gear.Code] = (existing.Spec, existing.Count + 1);
        }
        else
        {
            _stock[gear.Code] = (gear, 1);
            _orderedCodes.Add(gear.Code);
        }
    }
}
