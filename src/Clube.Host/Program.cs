using Clube.Adapters.Http;
using Clube.Adapters.Jobs;
using Clube.Adapters.Partners;
using Clube.Adapters.Sqlite;
using Clube.Core.Application;
using Clube.Core.Ports.Driving;

// Configurator: the only place that knows every piece. It plugs adapters into the hexagon's ports.
var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("Clube")
    ?? throw new InvalidOperationException("Missing connection string 'Clube'.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IForManagingMemberships, MembershipManager>();
builder.Services.AddScoped<IForRelayingTheOutbox, OutboxDispatcher>();
builder.Services.AddScoped<IForExpiringCredits, CreditExpiryRunner>();
builder.Services.AddSqliteAdapter(connectionString);
builder.Services.AddHttpAdapter();
builder.Services.AddPartnersAdapter();
builder.Services.AddJobsAdapter();

var app = builder.Build();

await app.Services.EnsureSqliteSchemaAsync();
app.UseHttpAdapter();

app.Run();
