using System.ComponentModel.DataAnnotations;

namespace TicketDesk.Api;

public sealed class CreateTicketRequest
{
    [Required, StringLength(120, MinimumLength = 3)]
    public string Title { get; init; } = "";

    [Required, StringLength(2000)]
    public string Description { get; init; } = "";

    public TicketPriority Priority { get; init; } = TicketPriority.Normal;
}

public sealed class ChangeStatusRequest
{
    [Required]
    public TicketStatus? Status { get; init; }
}

public sealed record TicketView(
    int Id, string Title, string Description, TicketPriority Priority,
    TicketStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static TicketView From(Ticket ticket) => new(
        ticket.Id, ticket.Title, ticket.Description, ticket.Priority,
        ticket.Status, ticket.CreatedAt, ticket.UpdatedAt);
}

public sealed record TicketPage(IReadOnlyList<TicketView> Items, int? NextBeforeId);
public sealed record StatusChangeView(TicketStatus? From, TicketStatus To, DateTimeOffset ChangedAt);
