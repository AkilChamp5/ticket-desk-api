using Microsoft.EntityFrameworkCore;

namespace TicketDesk.Api;

public sealed class TicketDatabase(DbContextOptions<TicketDatabase> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var tickets = model.Entity<Ticket>();
        tickets.Property(ticket => ticket.Title).HasMaxLength(120);
        tickets.Property(ticket => ticket.Description).HasMaxLength(2000);
        tickets.Property(ticket => ticket.Status).HasConversion<string>();
        tickets.Property(ticket => ticket.Priority).HasConversion<string>();
        tickets.Property(ticket => ticket.Version).IsConcurrencyToken();
        tickets.HasIndex(ticket => new { ticket.Status, ticket.Id });
        tickets.HasIndex(ticket => new { ticket.Priority, ticket.Id });

        var changes = model.Entity<StatusChange>();
        changes.Property(change => change.From).HasConversion<string>();
        changes.Property(change => change.To).HasConversion<string>();
        changes.HasIndex(change => new { change.TicketId, change.Id });
        tickets.HasMany(ticket => ticket.History)
            .WithOne()
            .HasForeignKey(change => change.TicketId);
    }
}

public enum TicketStatus { Open, InProgress, Resolved }
public enum TicketPriority { Low, Normal, High }

public sealed class Ticket
{
    public int Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required TicketPriority Priority { get; init; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
    public List<StatusChange> History { get; } = [];
}

public sealed class StatusChange
{
    public int Id { get; init; }
    public int TicketId { get; init; }
    public TicketStatus? From { get; init; }
    public TicketStatus To { get; init; }
    public DateTimeOffset ChangedAt { get; init; }
}
