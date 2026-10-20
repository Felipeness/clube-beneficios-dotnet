using System.Text.Json.Serialization;
using Clube.Core.Ports.Driven;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace Clube.Adapters.Http;

public static class HttpAdapter
{
    public static IServiceCollection AddHttpAdapter(this IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddBearerSecurity());
        services.AddProblemDetails();
        services.AddHttpContextAccessor();
        services.AddScoped<IForIdentifyingTheCaller, HttpCaller>();
        // Fails the boot instead of serving a webhook that could be forged with an empty or guessable key.
        services.AddOptions<PspOptions>()
            .BindConfiguration(PspOptions.Section)
            .Validate(psp => psp.WebhookSecret.Length >= PspOptions.MinimumSecretLength, $"Psp:WebhookSecret must have at least {PspOptions.MinimumSecretLength} characters.")
            .ValidateOnStart();
        services.AddBearerAuthentication();
        services.AddSingleton<PixSignature>();
        services.AddScoped<PixWebhookReceiver>();
        services.AddLedgerHealth();
        return services.ConfigureHttpJsonOptions(options =>
        {
            // Turns a missing or null "cpf" into a 400 at the boundary instead of a NullReferenceException.
            options.SerializerOptions.RespectNullableAnnotations = true;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
            // Enums go out by name, so the contract stays readable and survives reordering the enum.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    }

    // No configuration in code: AddJwtBearer binds Authentication:Schemes:Bearer (what dotnet user-jwts writes), so production
    // points at a real identity provider (Authority, ValidIssuer, ValidAudiences) through configuration alone.
    private static void AddBearerAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.RoleClaimType = ClubeClaims.Role;
            options.TokenValidationParameters.NameClaimType = ClubeClaims.Subject;
        });
        // Same stance as the webhook secret: outside Development a host without a trusted issuer does not boot.
        // Development may run without one (every protected route answers 401) until dotnet user-jwts is set up.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate<IHostEnvironment>((options, environment) => environment.IsDevelopment() || BearerSettings.AreComplete(options), BearerSettings.Missing)
            .ValidateOnStart();
        services.AddSingleton<IAuthorizationHandler, MembershipAccessHandler>();
        services.AddAuthorizationBuilder().AddMembershipPolicies();
    }

    public static WebApplication UseHttpAdapter(this WebApplication app)
    {
        // Development throws BadHttpRequestException on malformed bodies; keep its 400 instead of a generic 500.
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError,
        });
        app.UseStatusCodePages();
        // No fallback policy: each route states what it needs, so the webhook (authenticated by its HMAC) and the
        // sign-up stay reachable without a token.
        app.UseAuthentication();
        app.UseAuthorization();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        app.MapMemberships();
        app.MapPixWebhooks();
        app.MapReconciliation();
        app.MapHealth();
        return app;
    }
}
