using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using Mapster;
using MapsterMapper;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Infrastructure.Persistence;
using AqlamAcademy.Localization;
using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace AqlamAcademy.Api.Infrastructure.DependencyInjection;

public static class PresentationDependencyInjection
{
    private const string CorsPolicy = "AqlamCorsPolicy";
    public const string AuthRateLimitPolicy = "AuthRateLimit";

    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Localization
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddScoped<ILocalizationService, LocalizationService>();

        // 2. Current User Service
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentOrganization, CurrentOrganization>();

        // 2. Mapster
        var mapsterConfig = TypeAdapterConfig.GlobalSettings;
        mapsterConfig.Scan(Assembly.GetExecutingAssembly());
        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        // 3. JWT Authentication
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JWT settings are not configured.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var rsa = RSA.Create();
            rsa.FromXmlString(jwtSettings.Rsa.PublicKeyXml);
            var publicKey = new RsaSecurityKey(rsa);

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = publicKey
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Cookies.TryGetValue("accessToken", out var accessToken))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },

                // A session created on an organization's own domain only works on that domain, and a
                // main-domain session cannot be replayed against an organization domain.
                OnTokenValidated = context =>
                {
                    var tokenTenant = context.Principal?.FindFirst(AuthClaims.OrganizationTenantId)?.Value;
                    var hostTenant = context.HttpContext.GetHostTenantId();

                    if (!string.Equals(tokenTenant, hostTenant, StringComparison.Ordinal))
                        context.Fail("The session was issued for a different domain.");

                    return Task.CompletedTask;
                }
            };
        });

        // 3b. Multi-tenancy: resolve the organization by host (subdomain or custom domain) first,
        // then by the {org} route value on the main domain. Resolution never grants access by itself.
        var baseDomain = configuration["Cell:BaseDomain"] ?? "localhost";
        services.AddMultiTenant<AqlamTenantInfo>()
            .WithDelegateStrategy<HttpContext, AqlamTenantInfo>(httpContext =>
            {
                var host = httpContext.Request.Host.Host.ToLowerInvariant();
                var isMainDomain = host == baseDomain || host == "localhost" || System.Net.IPAddress.TryParse(host, out _);
                return Task.FromResult(isMainDomain ? null! : host);
            })
            .WithRouteStrategy("org", false)
            .WithStore<OrganizationTenantStore>(ServiceLifetime.Scoped);

        // 3c. Permission-based authorization inside the resolved organization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, NotFoundForNonMembersResultHandler>();

        // 4. CORS
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicy, builder =>
            {
                builder.WithOrigins(allowedOrigins)
                       .AllowAnyHeader()
                       .AllowAnyMethod()
                       .AllowCredentials();
            });
        });

        // 5. Rate limiting: slow down credential stuffing on login/register (per client IP)
        var authPermitsPerMinute = configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    public static IApplicationBuilder UsePresentation(this IApplicationBuilder app)
    {
        // 4. Request Localization Middleware
        var supportedCultures = new[]
        {
            new CultureInfo("en-US"),
            new CultureInfo("ar-EG")
        };

        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("en-US"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures
        });

        app.UseRouting();

        // 5. CORS Middleware
        app.UseCors(CorsPolicy);

        // Resolve the tenant before authentication/authorization so policies can use it
        app.UseMultiTenant();

        // 6. Authentication & Authorization Middleware
        app.UseAuthentication();
        app.UseAuthorization();

        // 7. Rate limiting (after routing so endpoint policies apply)
        app.UseRateLimiter();

        return app;
    }
}


