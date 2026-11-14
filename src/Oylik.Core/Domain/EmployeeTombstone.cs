namespace Oylik.Core.Domain;

/// <summary>
/// Bosqich 3: o'chirilgan (tarixsiz) xodim izi. Server upsert-only — o'chirilgan xodim serverda faol qolib, boshqa zavodga
/// pull bilan qaytmasligi uchun keyingi sinxronda IsActive=false bilan jo'natiladi, server qabul qilgach o'chiriladi.
/// </summary>
public sealed class EmployeeTombstone
{
    public Guid Id { get; set; }

    /// <summary>Server bo'sh nomni rad etadi — o'chirish paytidagi nom saqlanadi.</summary>
    public string FullName { get; set; } = "";

    public int TabelNo { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
}
