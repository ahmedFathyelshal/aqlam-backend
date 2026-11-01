using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Platform.Organizations;

/// <summary>Creates a Center or School organization with its subdomain and owner. Personal organizations are created at teacher sign-up.</summary>
public record CreateOrganizationCommand(
    string Name,
    string Slug,
    OrganizationType Type,
    string CountryCode,
    string OwnerUserName,
    IsolationTier IsolationTier = IsolationTier.Shared,
    string? DedicatedConnectionString = null) : IRequest<Result<OrganizationDto>>;

public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    /// <summary>Slugs that would collide with platform routes or subdomains.</summary>
    public static readonly HashSet<string> ReservedSlugs =
    [
        "admin", "api", "app", "auth", "login", "www", "mail", "support", "help", "billing", "static", "cdn",
        "assets", "platform", "teacher", "student", "parent", "school", "status", "docs", "blog", "aqlam", "edumena"
    ];

    public static readonly HashSet<string> SupportedCountries = ["SA", "EG"];

    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(3).MaximumLength(150);

        RuleFor(x => x.Slug)
            .NotEmpty()
            .Matches("^[a-z0-9](?:[a-z0-9-]{1,48}[a-z0-9])$")
            .WithMessage("Slug must be 3-50 lowercase letters, digits or hyphens, and cannot start or end with a hyphen.")
            .Must(slug => !ReservedSlugs.Contains(slug))
            .WithMessage("This slug is reserved.");

        RuleFor(x => x.Type)
            .Must(type => type is OrganizationType.Center or OrganizationType.School)
            .WithMessage("Only Center or School organizations can be created here.");

        RuleFor(x => x.CountryCode)
            .Must(code => SupportedCountries.Contains(code))
            .WithMessage("Country must be SA or EG.");

        RuleFor(x => x.OwnerUserName).NotEmpty();

        RuleFor(x => x.IsolationTier).IsInEnum();
        RuleFor(x => x.DedicatedConnectionString)
            .NotEmpty().WithMessage("A connection string is required for a dedicated database.")
            .When(x => x.IsolationTier == IsolationTier.Dedicated);
        RuleFor(x => x.DedicatedConnectionString)
            .Empty().WithMessage("A connection string is only allowed for a dedicated database.")
            .When(x => x.IsolationTier == IsolationTier.Shared);
    }
}

public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, Result<OrganizationDto>>
{
    private readonly IOrganizationAdminService _organizations;

    public CreateOrganizationCommandHandler(IOrganizationAdminService organizations)
    {
        _organizations = organizations;
    }

    public Task<Result<OrganizationDto>> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken) =>
        _organizations.CreateAsync(request.Name.Trim(), request.Slug, request.Type, request.CountryCode, request.OwnerUserName.Trim(),
            request.IsolationTier, request.DedicatedConnectionString, cancellationToken);
}
