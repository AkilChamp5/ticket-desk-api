param([string]$BaseUrl = 'http://127.0.0.1:5074')

$ErrorActionPreference = 'Stop'
$ticketUrl = "$($BaseUrl.TrimEnd('/'))/api/tickets"

$ticket = Invoke-RestMethod -Method Post -Uri $ticketUrl -ContentType 'application/json' -Body (
    @{
        title = 'Customer cannot export an invoice'
        description = 'The export button returns an empty file for the November invoice.'
        priority = 'high'
    } | ConvertTo-Json -Compress
)
Write-Output "Created ticket #$($ticket.id) [$($ticket.status), $($ticket.priority) priority]"

$ticketItemUrl = "$ticketUrl/$($ticket.id)"
foreach ($nextStatus in @('inProgress', 'resolved')) {
    $ticket = Invoke-RestMethod -Method Put -Uri "$ticketItemUrl/status" -ContentType 'application/json' -Body (
        @{ status = $nextStatus } | ConvertTo-Json -Compress
    )
    Write-Output "Ticket #$($ticket.id) moved to $($ticket.status)"
}

$history = Invoke-RestMethod -Uri "$ticketItemUrl/history"
$statusPath = ($history | ForEach-Object { $_.to }) -join ' -> '
Write-Output "Saved status history: $statusPath"

$page = Invoke-RestMethod -Uri "${ticketUrl}?status=resolved&priority=high&limit=5"
Write-Output "Recent resolved high-priority tickets returned: $($page.items.Count)"
