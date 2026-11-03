namespace AqlamAcademy.Domain.Attendance;

public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    Late = 3,
    Excused = 4
}

/// <summary>
/// Attendance of one class/group on one day (school, StartTime null) or one meeting (center, StartTime set).
/// Saving the same class/date/time again updates it, so offline phones can safely resend.
/// </summary>
public class AttendanceSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassGroupId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }

    public Guid TakenByUserId { get; set; }
    public DateTime TakenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedAtUtc { get; set; }

    public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
}

public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }

    /// <summary>Statuses that families are told about.</summary>
    public static bool IsAlert(AttendanceStatus status) => status is AttendanceStatus.Absent or AttendanceStatus.Late;
}
