using System.Text.Json.Serialization;
using Clube.Core.Ports.Driven;
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
        services.AddOpenApi();
        services.AddProblemDetails();
        services.AddHttpContextAccessor();
        services.AddScoped<IForIdentifyingTheCaller, HttpCaller>();
        // Fails the boot instead of serving a webhook that could be forged with an empty or guessable key.
        services.AddOptions<PspOptions>()
            .BindConfiguration(PspOptions.Section)
            .Validate(psp => psp.WebhookSecret.Length >= PspOptions.MinimumSecretLength, $"Psp:WebhookSecret must have at least {PspOptions.MinimumSecretLength} characters.")
            .ValidateOnStart();
        services.AddSingleton<PixSignature>();
        services.AddScoped<PixWebhookReceiver>();
        return services.ConfigureHttpJsonOptions(options =>
        {
            // Turns a missing or null "cpf" into a 400 at the boundary instead of a NullReferenceException.
            options.SerializerOptions.RespectNullableAnnotations = true;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
            // Enums go out by name, so the contract stays readable and survives reordering the enum.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
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
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        app.MapMemberships();
        app.MapPixWebhooks();
        return app;
    }
}
