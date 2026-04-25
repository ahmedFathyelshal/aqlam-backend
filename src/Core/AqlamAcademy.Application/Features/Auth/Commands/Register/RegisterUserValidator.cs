using FluentValidation;
using AqlamAcademy.Application.Common.Constants;
using AqlamAcademy.Application.Common.Interfaces;

namespace AqlamAcademy.Application.Features.Auth.Commands.Register;

public class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    private readonly IIdentityService _identityService;

    public RegisterUserValidator(
        IIdentityService identityService,
        ILocalizationService localizer)
    {
        _identityService = identityService;

        RuleFor(v => v.UserName)
            .NotEmpty().WithMessage(localizer[ValidationMessages.UserNameRequired])
            .MustAsync(BeUniqueUserName).WithMessage(localizer[ValidationMessages.UserNameAlreadyInUse]);

        RuleFor(v => v.Email)
            .EmailAddress().WithMessage(localizer[ValidationMessages.InvalidEmail])
            .When(v => !string.IsNullOrEmpty(v.Email));

        RuleFor(v => v.Password)
            .NotEmpty().WithMessage(localizer[ValidationMessages.PasswordRequired])
            .MinimumLength(8).WithMessage(localizer[ValidationMessages.PasswordTooShort]);

        RuleFor(v => v.ConfirmPassword)
            .Equal(v => v.Password).WithMessage(localizer[ValidationMessages.PasswordsDoNotMatch]);

        RuleFor(v => v.UserType)
            .IsInEnum().WithMessage(localizer[ValidationMessages.InvalidUserType]);
    }

    public async Task<bool> BeUniqueUserName(string userName, CancellationToken cancellationToken)
    {
        return await _identityService.IsUserNameUniqueAsync(userName);
    }
}


