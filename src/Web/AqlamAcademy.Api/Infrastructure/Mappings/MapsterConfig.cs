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
        
        // You can add more complex mappings here as the project grows
    }
}

