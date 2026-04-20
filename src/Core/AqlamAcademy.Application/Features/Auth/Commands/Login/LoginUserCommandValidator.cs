using FluentValidation;
using AqlamAcademy.Application.Common.Constants;
using AqlamAcademy.Application.Common.Interfaces;

namespace AqlamAcademy.Application.Features.Auth.Commands.Login;

public class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator(ILocalizationService localizer)
    {
        RuleFor(v => v.UserName)
            .NotEmpty().WithMessage(localizer[ValidationMessages.UserNameRequired]);
            
        RuleFor(v => v.Password)
            .NotEmpty().WithMessage(localizer[ValidationMessages.PasswordRequired]);
    }
}
