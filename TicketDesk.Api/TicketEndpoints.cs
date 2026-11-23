using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace TicketDesk.Api;

internal static class TicketEndpoints
{
    internal static void MapTickets(this WebApplication app)
    {
        var tickets = app.MapGroup("/api/tickets").WithTags("Tickets");
        tickets.MapPost("", Create).WithSummary("Open a support ticket");
        tickets.MapGet("", List).WithSummary("Browse tickets with status and priority filters");
        tickets.MapGet("/{id:int}", Get).WithSummary("Read one support ticket");
        tickets.MapPut("/{id:int}/status", ChangeStatus)
            .WithSummary("Move a ticket to its next status")
            .ProducesProblem(StatusCodes.Status409Conflict);
        tickets.MapGet("/{id:int}/history", GetHistory).WithSummary("Read a ticket's status history");
    }

    private static async Task<Results<Created<TicketView>, ValidationProblem>> Create(
        CreateTicketRequest request, TicketDatabase database, CancellationToken cancellation)
    {
        var title = request.Title.Trim();
        if (title.Length < 3)
            return Invalid("title", "The title must contain at least three characters.");

        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Title = title,
            Description = request.Description.Trim(),
            Priority = request.Priority,
            CreatedAt = now,
            UpdatedAt = now
        };
        ticket.History.Add(new StatusChange { To = TicketStatus.Open, ChangedAt = now });
        database.Tickets.Add(ticket);
        await database.SaveChangesAsync(cancellation);

        return TypedResults.Created($"/api/tickets/{ticket.Id}", TicketView.From(ticket));
    }

    private static async Task<Results<Ok<TicketPage>, ValidationProblem>> List(
        TicketDatabase database, CancellationToken cancellation,
        string? status = null, string? priority = null, int? beforeId = null, int limit = 20)
    {
        if (limit is < 1 or > 100)
            return Invalid("limit", "Use a limit between 1 and 100.");
        if (beforeId is <= 0)
            return Invalid("beforeId", "Use the positive nextBeforeId from the previous page.");
        if (!TryParseFilter<TicketStatus>(status, out var ticketStatus))
            return Invalid("status", "Use open, inProgress, or resolved.");
        if (!TryParseFilter<TicketPriority>(priority, out var ticketPriority))
            return Invalid("priority", "Use low, normal, or high.");

        IQueryable<Ticket> query = database.Tickets.AsNoTracking();
        if (status is not null)
            query = query.Where(ticket => ticket.Status == ticketStatus);
        if (priority is not null)
            query = query.Where(ticket => ticket.Priority == ticketPriority);
        if (beforeId is int previousId)
            query = query.Where(ticket => ticket.Id < previousId);

        var results = await query.OrderByDescending(ticket => ticket.Id)
            .Take(limit + 1).ToListAsync(cancellation);
        var items = results.Take(limit).Select(TicketView.From).ToArray();
        int? nextBeforeId = results.Count > limit ? items[^1].Id : null;
        return TypedResults.Ok(new TicketPage(items, nextBeforeId));
    }

    private static async Task<Results<Ok<TicketView>, NotFound>> Get(
        int id, TicketDatabase database, CancellationToken cancellation)
    {
        var ticket = await database.Tickets.AsNoTracking()
            .SingleOrDefaultAsync(ticket => ticket.Id == id, cancellation);
        if (ticket is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(TicketView.From(ticket));
    }

    private static async Task<Results<Ok<TicketView>, NotFound, ProblemHttpResult, ValidationProblem>> ChangeStatus(
        int id, ChangeStatusRequest request, TicketDatabase database, CancellationToken cancellation)
    {
        if (request.Status is not { } next)
            return Invalid("status", "A status is required.");

        var ticket = await database.Tickets.SingleOrDefaultAsync(ticket => ticket.Id == id, cancellation);
        if (ticket is null)
            return TypedResults.NotFound();
        if (ticket.Status == next)
            return TypedResults.Ok(TicketView.From(ticket));
        if (!CanChangeStatus(ticket.Status, next))
            return Conflict("Use open → inProgress → resolved. A resolved ticket can be reopened.");

        var now = DateTimeOffset.UtcNow;
        ticket.History.Add(new StatusChange { From = ticket.Status, To = next, ChangedAt = now });
        ticket.Status = next;
        ticket.UpdatedAt = now;
        ticket.Version++;

        try
        {
            // EF saves the ticket and its new history row in one transaction.
            await database.SaveChangesAsync(cancellation);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Another request changed the ticket. Read the latest status and try again.");
        }

        return TypedResults.Ok(TicketView.From(ticket));
    }

    private static async Task<Results<Ok<StatusChangeView[]>, NotFound>> GetHistory(
        int id, TicketDatabase database, CancellationToken cancellation)
    {
        var changes = await database.StatusChanges.AsNoTracking()
            .Where(change => change.TicketId == id)
            .OrderBy(change => change.Id)
            .Select(change => new StatusChangeView(change.From, change.To, change.ChangedAt))
            .ToArrayAsync(cancellation);
        if (changes.Length == 0)
            return TypedResults.NotFound();
        return TypedResults.Ok(changes);
    }

    private static bool CanChangeStatus(TicketStatus current, TicketStatus next) =>
        (current, next) is (TicketStatus.Open, TicketStatus.InProgress)
            or (TicketStatus.InProgress, TicketStatus.Resolved)
            or (TicketStatus.Resolved, TicketStatus.Open);

    private static bool TryParseFilter<T>(string? value, out T result) where T : struct, Enum
    {
        result = default;
        return value is null || (Enum.TryParse(value, ignoreCase: true, out result)
            && string.Equals(value, result.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Status conflict", detail: detail);
}
