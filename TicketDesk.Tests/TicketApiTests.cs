using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TicketDesk.Tests;

public sealed class TicketApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"ticket-desk-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task StatusChangesAreRecordedOnceAndSurviveRestart()
    {
        int id;
        await using (var app = new TestApp(databasePath))
        using (var client = app.CreateClient())
        {
            id = await CreateTicket(client, "Cannot export invoice");

            using var skippedStep = await PutStatus(client, id, "resolved");
            Assert.Equal(HttpStatusCode.Conflict, skippedStep.StatusCode);

            foreach (var status in new[] { "inProgress", "inProgress", "resolved", "open" })
            {
                using var result = await PutStatus(client, id, status);
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            }
        }

        await using var restarted = new TestApp(databasePath);
        using var reader = restarted.CreateClient();
        var ticket = await reader.GetFromJsonAsync<JsonElement>($"/api/tickets/{id}");
        Assert.Equal("open", ticket.GetProperty("status").GetString());
        var history = await reader.GetFromJsonAsync<JsonElement>($"/api/tickets/{id}/history");
        Assert.Equal(
            new[] { "open", "inProgress", "resolved", "open" },
            history.EnumerateArray().Select(change => change.GetProperty("to").GetString()).ToArray());
    }

    [Fact]
    public async Task FilteredPagesHaveAStableCursorAndNoDuplicateTickets()
    {
        await using var app = new TestApp(databasePath);
        using var client = app.CreateClient();
        var firstId = await CreateTicket(client, "First high priority", "high");
        var secondId = await CreateTicket(client, "Second high priority", "high");
        await CreateTicket(client, "Low priority issue", "low");
        var resolvedId = await CreateTicket(client, "Resolved high priority issue", "high");
        using var inProgress = await PutStatus(client, resolvedId, "inProgress");
        inProgress.EnsureSuccessStatusCode();
        using var resolved = await PutStatus(client, resolvedId, "resolved");
        resolved.EnsureSuccessStatusCode();

        const string filter = "/api/tickets?priority=high&status=open&limit=1";
        var firstPage = await client.GetFromJsonAsync<JsonElement>(filter);
        Assert.Equal(secondId, firstPage.GetProperty("items")[0].GetProperty("id").GetInt32());
        var cursor = firstPage.GetProperty("nextBeforeId").GetInt32();
        Assert.Equal(secondId, cursor);

        var secondPage = await client.GetFromJsonAsync<JsonElement>(filter + $"&beforeId={cursor}");
        Assert.Equal(firstId, secondPage.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("nextBeforeId").ValueKind);
    }

    [Fact]
    public async Task InvalidInputsReturn400AndUnknownTicketsReturn404()
    {
        await using var app = new TestApp(databasePath);
        using var client = app.CreateClient();
        using var badTicket = await client.PostAsJsonAsync("/api/tickets", new { title = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, badTicket.StatusCode);
        var problem = await badTicket.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out _));

        var id = await CreateTicket(client, "Browser cannot sign in");
        using var badStatus = await PutStatus(client, id, "unsupported");
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        using var numericStatus = await client.PutAsJsonAsync($"/api/tickets/{id}/status", new { status = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, numericStatus.StatusCode);
        using var badLimit = await client.GetAsync("/api/tickets?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
        using var notFound = await client.GetAsync("/api/tickets/999");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task OpenApiListsTheTicketAndHistoryRoutes()
    {
        await using var app = new TestApp(databasePath);
        using var client = app.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/tickets").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/tickets/{id}/history").TryGetProperty("get", out _));
    }

    private static async Task<int> CreateTicket(HttpClient client, string title, string priority = "normal")
    {
        using var result = await client.PostAsJsonAsync("/api/tickets", new
        {
            title,
            description = "Example issue for an API test.",
            priority
        });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var item = await result.Content.ReadFromJsonAsync<JsonElement>();
        var id = item.GetProperty("id").GetInt32();
        Assert.Equal($"/api/tickets/{id}", result.Headers.Location?.OriginalString);
        return id;
    }

    private static Task<HttpResponseMessage> PutStatus(HttpClient client, int id, string status) =>
        client.PutAsJsonAsync($"/api/tickets/{id}/status", new { status });

    public void Dispose() => File.Delete(databasePath);

    private sealed class TestApp(string path) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TicketDesk"] = $"Data Source={path};Pooling=False"
                }));
    }
}
