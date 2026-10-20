using Clube.Api.Memberships;
using Clube.Application.Memberships;
using Clube.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Clube")
    ?? throw new InvalidOperationException("Missing connection string 'Clube'.");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Turns a missing or null "cpf" into a 400 at the boundary instead of a NullReferenceException.
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddClubeInfrastructure(connectionString);
builder.Services.AddScoped<SubscribeMember>();
builder.Services.AddScoped<GetMembership>();
builder.Services.AddScoped<CreditMonthlyAllowance>();
builder.Services.AddScoped<RedeemReward>();

var app = builder.Build();

// Development throws BadHttpRequestException on malformed bodies; keep its 400 instead of a generic 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

await app.Services.EnsureClubeDatabaseAsync();
app.MapMemberships();

app.Run();
