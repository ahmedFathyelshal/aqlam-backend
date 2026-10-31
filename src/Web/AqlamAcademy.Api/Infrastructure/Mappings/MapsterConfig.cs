using Mapster;
using AqlamAcademy.Api.Contracts.Auth;
using AqlamAcademy.Application.Features.Auth.Commands.Register;

namespace AqlamAcademy.Api.Infrastructure.Mappings;

public class MapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Simple mapping from RegisterRequest (API) to RegisterUserCommand (Application)
        config.NewConfig<RegisterRequest, RegisterUserCommand>();
        
        // Course Mappings
        config.NewConfig<AqlamAcademy.Domain.Courses.Course, AqlamAcademy.Application.Features.Teacher.Courses.DTOs.CourseDraftDto>()
              .Map(dest => dest.Chapters, src => src.Chapters.OrderBy(ch => ch.Order));
              
        config.NewConfig<AqlamAcademy.Domain.Courses.CourseChapter, AqlamAcademy.Application.Features.Teacher.Courses.DTOs.CourseChapterDto>()
              .Map(dest => dest.Lessons, src => src.Lessons.OrderBy(l => l.Order));
    }
}

