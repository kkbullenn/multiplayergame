using Microsoft.AspNetCore.SignalR;

namespace MulticastGame.Server
{
    /// <summary>
    /// SignalR Hub — acts as the central relay for all game clients.
    ///
    /// Clients call:
    ///   SendGameMessage(senderId, payload)
    ///
    /// The hub broadcasts to ALL connected clients (including the sender,
    /// who filters their own messages out client-side by comparing senderId).
    ///
    /// This mirrors the multicast pattern:  one sender → all receivers.
    /// </summary>
    public class GameHub : Hub
    {
        /// <summary>
        /// Receives a message from one client and broadcasts it to all others.
        /// </summary>
        public async Task SendGameMessage(string senderId, string payload)
        {
            // Broadcast to every connected client except the sender
            await Clients.Others.SendAsync("ReceiveGameMessage", senderId, payload);
        }

        /// <summary>
        /// Called automatically when a client connects.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"[GameHub] Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Called automatically when a client disconnects.
        /// Broadcasts an unlock-all so other clients release that player's cube locks.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"[GameHub] Client disconnected: {Context.ConnectionId}");
            await Clients.Others.SendAsync("PlayerDisconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}