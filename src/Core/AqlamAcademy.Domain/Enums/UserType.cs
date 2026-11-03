namespace AqlamAcademy.Domain.Enums;

public enum UserType
{
    Student = 1,
    Teacher = 2,
    Assistant = 3,
    Parent = 4   // links to children with a code from their school; no platform role or profile
}
