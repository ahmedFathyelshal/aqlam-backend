namespace AqlamAcademy.Domain.Lookups;

public abstract class LookupEntity
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
}
