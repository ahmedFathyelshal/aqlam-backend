namespace AqlamAcademy.Domain.Enums;

public enum CourseVisibility
{
    Private = 1,  // organization members only
    Unlisted = 2, // anyone with the link
    Public = 3    // listed in the marketplace (subject to the organization's publishing policy)
}
