using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Clube.Adapters.Http;

internal static class BearerSettings
{
    public const string Missing =
        "Authentication:Schemes:Bearer needs ValidIssuer, ValidAudiences and either Authority or SigningKeys outside Development.";

    // Without an issuer or an audience the handler silently skips that check, so both are demanded, not just a key.
    public static bool AreComplete(JwtBearerOptions options)
    {
        var parameters = options.TokenValidationParameters;
        bool hasIssuer = parameters.ValidIssuers?.Any(issuer => !string.IsNullOrWhiteSpace(issuer)) == true;
        bool hasAudience = parameters.ValidAudiences?.Any(audience => !string.IsNullOrWhiteSpace(audience)) == true;
        bool hasKeys = !string.IsNullOrWhiteSpace(options.Authority) || parameters.IssuerSigningKeys?.Any() == true;
        return hasIssuer && hasAudience && hasKeys;
    }
}

// Lets Scalar show a token field and marks which operations need one.
internal static class BearerOpenApi
{
    private const string SchemeId = JwtBearerDefaults.AuthenticationScheme;

    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options) => options
        .AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Member token (claim membership_id) or operator token (role operator).",
            };
            return Task.CompletedTask;
        })
        .AddOperationTransformer((operation, context, _) =>
        {
            if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any()) return Task.CompletedTask;

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [] });
            return Task.CompletedTask;
        });
}
