namespace Oylik.Core.Domain;

/// <summary>
/// Tarmoq filtri (3.9): Barchasi · bitta tarmoq · Belgilanmagan (BranchId = null).
/// Sozlamada saqlanadi: null — Barchasi, "none" — Belgilanmagan, aks holda tarmoq Id.
/// </summary>
public readonly record struct BranchFilter(bool IsAll, Guid? BranchId)
{
    public static BranchFilter All { get; } = new(true, null);
    public static BranchFilter Unassigned { get; } = new(false, null);
    public static BranchFilter Of(Guid branchId) => new(false, branchId);

    public bool IsUnassigned => !IsAll && BranchId is null;

    public bool Matches(Guid? branchId) => IsAll || branchId == BranchId;

    public IEnumerable<PayrollLine> Apply(IEnumerable<PayrollLine> lines)
    {
        var id = BranchId;
        return IsAll ? lines : lines.Where(l => l.BranchId == id);
    }

    public IEnumerable<Employee> Apply(IEnumerable<Employee> employees)
    {
        var id = BranchId;
        return IsAll ? employees : employees.Where(e => e.BranchId == id);
    }

    public string? ToSetting() => IsAll ? null : BranchId?.ToString() ?? "none";

    public static BranchFilter Parse(string? setting) => setting switch
    {
        null or "" => All,
        "none" => Unassigned,
        _ when Guid.TryParse(setting, out var id) => Of(id),
        _ => All,
    };
}
