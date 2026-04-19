using Microsoft.AspNetCore.Mvc;
using MediatR;
using MapsterMapper;
using AqlamAcademy.Api.Contracts.Auth;
using AqlamAcademy.Application.Features.Auth.Commands.Register;
using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly IMapper _mapper;

    public AuthController(ISender mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    [HttpPost("register")]
    public async Task<ActionResult<Result<RegisterResponse>>> Register(RegisterRequest request)
    {
        var command = _mapper.Map<RegisterUserCommand>(request);
        var result = await _mediator.Send(command);
        
        if (!result.Succeeded)
            return BadRequest(result);

        return Ok(result);
    }
}