# Ticket Desk API

Ticket Desk is a C# REST API for a small support team. It opens support tickets, filters the queue by status and priority, and stores a history of every status change.

Built with ASP.NET Core on .NET 10, Entity Framework Core, and SQLite. SQLite runs in a local file, so the demo does not require a separate database server.

## Run it locally

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). From the project folder, run:

```powershell
dotnet run --project TicketDesk.Api -- --urls http://127.0.0.1:5074
```

On its first run the API applies the included database migration and creates `ticket-desk.db`. Keep the server running. From a second PowerShell window in the project folder, run:

```powershell
.\scripts\demo.ps1
```

The script opens a high-priority ticket, moves it through the workflow, reads its history back from SQLite, and requests a filtered list. A fresh run looks like this:

```text
Created ticket #1 [open, high priority]
Ticket #1 moved to inProgress
Ticket #1 moved to resolved
Saved status history: open -> inProgress -> resolved
Recent resolved high-priority tickets returned: 1
```

In a browser, open [the ticket list](http://127.0.0.1:5074/api/tickets?status=resolved) or [the OpenAPI document](http://127.0.0.1:5074/openapi/v1.json) while the server is running.

## API endpoints

| Method and route | What it does |
| --- | --- |
| `POST /api/tickets` | Creates an open ticket and returns `201 Created` with its URL. |
| `GET /api/tickets` | Lists tickets newest first, with optional `status`, `priority`, `limit`, and `beforeId` filters. |
| `GET /api/tickets/{id}` | Returns a single ticket. |
| `PUT /api/tickets/{id}/status` | Updates the status using a body such as `{"status":"inProgress"}`. |
| `GET /api/tickets/{id}/history` | Returns the status changes in the order they occurred. |

A new ticket takes a `title` and `description`. Priority defaults to `normal` and also accepts `low` and `high`. For list requests, the default limit is 20 and the maximum is 100. Pass the response's `nextBeforeId` as `beforeId` alongside the same filters to load the next page.

The workflow is `open → inProgress → resolved`. Reopening a resolved ticket moves it back to `open`. Repeating the current status succeeds without adding duplicate history. Invalid input returns `400`, unknown ticket IDs return `404`, and invalid or concurrent status changes return `409`.

## How it works

- **REST.** The endpoints accept and return JSON. Request models validate input, and the OpenAPI document describes the available operations.
- **SQL.** The migration creates two tables. `Tickets` stores the current state. `StatusChanges` references the ticket ID with a foreign key. Indexes on `(Status, Id)` and `(Priority, Id)` support the queue filters.
- **Consistency.** EF Core saves the new status and the history row in one transaction. A version column guards against simultaneous edits based on the same ticket state.

If you open the SQLite database in a database viewer, this query counts tickets by status:

```sql
SELECT Status, COUNT(*) AS TicketCount
FROM Tickets
GROUP BY Status
ORDER BY Status;
```

## Run the tests

```powershell
dotnet test TicketDesk.sln
```

The integration tests send HTTP requests through ASP.NET Core and use a temporary SQLite database. They cover status changes after a restart, filtering and pagination, error responses, and OpenAPI documentation.

To inspect the migration as SQL, run:

```powershell
dotnet tool restore
dotnet ef migrations script --project TicketDesk.Api
```
