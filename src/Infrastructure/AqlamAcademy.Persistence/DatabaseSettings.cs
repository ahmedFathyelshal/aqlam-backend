namespace AqlamAcademy.Infrastructure.Persistence;

public class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";
    

    public string ConnectionString { get; set; } = string.Empty;
}
