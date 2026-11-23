using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TicketDesk.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<TicketDatabase>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    options.UseSqlite(configuration.GetConnectionString("TicketDesk") ?? "Data Source=ticket-desk.db");
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
// Keep invalid request bodies as HTTP 400 responses in development, too.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Give a fresh local demo its database schema on the first run.
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<TicketDatabase>();
    await database.Database.MigrateAsync();
}

app.MapOpenApi();
app.MapGet("/", () => TypedResults.Ok(new
{
    service = "Ticket Desk API",
    tickets = "/api/tickets",
    openApi = "/openapi/v1.json"
})).ExcludeFromDescription();
app.MapTickets();
app.Run();

public partial class Program { }
