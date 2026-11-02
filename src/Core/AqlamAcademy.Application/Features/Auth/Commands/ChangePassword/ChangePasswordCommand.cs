using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Auth.Commands.ChangePassword;

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result<bool>>;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

public class ChangePasswordCommandHandler(IIdentityService identityService, ICurrentUserService currentUser)
    : IRequestHandler<ChangePasswordCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken) =>
        identityService.ChangePasswordAsync(currentUser.UserId, request.CurrentPassword, request.NewPassword);
}
