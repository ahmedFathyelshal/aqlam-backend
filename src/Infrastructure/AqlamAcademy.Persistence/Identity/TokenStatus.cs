namespace AqlamAcademy.Infrastructure.Persistence.Identity;

public enum TokenStatus
{
    Active = 1,
    Consumed = 2,
    Revoked = 3,
    Invalidated = 4
}
