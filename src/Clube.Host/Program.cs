using Clube.Adapters.Http;
using Clube.Adapters.Sqlite;
using Clube.Core.Application;
using Clube.Core.Ports.Driving;

// Configurator: the only place that knows every piece. It plugs adapters into the hexagon's ports.
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Clube")
    ?? throw new InvalidOperationException("Missing connection string 'Clube'.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IForManagingMemberships, MembershipManager>();
builder.Services.AddSqliteAdapter(connectionString);
builder.Services.AddHttpAdapter();

var app = builder.Build();

await app.Services.EnsureSqliteSchemaAsync();
app.UseHttpAdapter();

app.Run();
