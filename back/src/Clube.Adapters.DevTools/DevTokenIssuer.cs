using Clube.Adapters.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Clube.Adapters.DevTools;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

// Plays the identity provider for local runs. It signs with exactly what the JwtBearer handler validates against (issuer,
// audience and key from Authentication:Schemes:Bearer, where dotnet user-jwts keeps them), so a token it issues is
// accepted by construction and nothing here can drift from the API's own settings.
internal sealed class DevTokenIssuer(IOptionsMonitor<JwtBearerOptions> bearer)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    // Null while the API has no symmetric key to sign with, so the endpoint can tell the developer what to run.
    public IssuedToken? Issue(IReadOnlyDictionary<string, object> claims)
    {
        TokenValidationParameters validation = bearer.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        string? issuer = validation.ValidIssuer ?? validation.ValidIssuers?.FirstOrDefault();
        string? audience = validation.ValidAudience ?? validation.ValidAudiences?.FirstOrDefault();
        SymmetricSecurityKey? key = validation.IssuerSigningKeys?.OfType<SymmetricSecurityKey>().FirstOrDefault();
        if (issuer is null || audience is null || key is null) return null;

        // The JWT handler checks lifetimes against the real clock, so this must not follow the shifted dev clock.
        DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
        DateTime expiresAt = now.Add(Lifetime);
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object>(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
        return new IssuedToken(token, new DateTimeOffset(expiresAt, TimeSpan.Zero));
    }

    public static Dictionary<string, object> MemberClaims(Guid membershipId) => new()
    {
        [ClubeClaims.Subject] = membershipId.ToString(),
        [ClubeClaims.MembershipId] = membershipId.ToString(),
    };

    public static Dictionary<string, object> OperatorClaims(string name) => new()
    {
        [ClubeClaims.Subject] = name,
        [ClubeClaims.Role] = ClubeClaims.OperatorRole,
    };
}
