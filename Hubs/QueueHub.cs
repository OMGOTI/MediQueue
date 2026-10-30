using Microsoft.AspNetCore.SignalR;

namespace MediQueue.Hubs;

/// <summary>
/// Pages connect here. The server sends "queueChanged" whenever a token is issued, called, skipped...
/// and every open page then reloads its own live section. The hub carries no personal data.
/// </summary>
public class QueueHub : Hub
{
}
