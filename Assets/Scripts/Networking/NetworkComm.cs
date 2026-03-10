using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// SignalR client NuGet package — add via NuGetForUnity (see README for setup steps)
using Microsoft.AspNetCore.SignalR.Client;

namespace MulticastGame.Networking
{
    /// <summary>
    /// WebSocket-based network layer using SignalR.
    /// Drop-in replacement for the UDP multicast NetworkComm.cs —
    /// exposes the identical public API so MoveCubes.cs requires zero changes.
    ///
    /// Architecture:
    ///   Unity client  →  SignalR Hub (GameServer)  →  all other Unity clients
    ///
    /// To swap back to multicast: delete this file, rename NetworkCommUDP.cs → NetworkComm.cs.
    /// </summary>
    public class NetworkComm
    {
        // -----------------------------------------------------------------------
        // Configuration — set SERVER_IP to the LAN IP of whichever machine
        // is running the GameServer before hitting Play.
        // -----------------------------------------------------------------------
        private const string SERVER_IP = "localhost"; // ← change to host machine's LAN IP
        private const int SERVER_PORT = 7777;
        private const string HUB_PATH = "/gameHub";

        private string HubUrl => $"http://{SERVER_IP}:{SERVER_PORT}{HUB_PATH}";

        // -----------------------------------------------------------------------
        // Public event — identical signature to the UDP version
        // -----------------------------------------------------------------------
        public delegate void MsgHandler(string senderId, string payload);
        public event MsgHandler MsgReceived;

        // Connection state
        private HubConnection _connection;
        private bool _isConnected = false;
        public bool IsConnected => _isConnected;

        // -----------------------------------------------------------------------
        // Connect  (call once from MoveCubes.Start, replaces the bg thread)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Builds the SignalR connection and starts it asynchronously.
        /// Safe to call from Unity's main thread.
        /// </summary>
        public void Connect()
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(HubUrl)
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10)
                })
                .Build();

            // Register incoming message handler
            _connection.On<string, string>("ReceiveGameMessage", (senderId, payload) =>
            {
                MsgReceived?.Invoke(senderId, payload);
            });

            // Handle disconnection events
            _connection.Closed += async (error) =>
            {
                _isConnected = false;
                Debug.LogWarning($"[NetworkCommWS] Connection closed: {error?.Message}");
                await Task.Delay(2000);
            };

            _connection.Reconnecting += (error) =>
            {
                _isConnected = false;
                Debug.LogWarning($"[NetworkCommWS] Reconnecting: {error?.Message}");
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                _isConnected = true;
                Debug.Log($"[NetworkCommWS] Reconnected: {connectionId}");
                return Task.CompletedTask;
            };

            // Start connection on a background thread so Unity doesn't freeze
            Task.Run(async () =>
            {
                try
                {
                    await _connection.StartAsync();
                    _isConnected = true;
                    Debug.Log($"[NetworkCommWS] Connected to {HubUrl}");
                }
                catch (Exception e)
                {
                    _isConnected = false;
                    Debug.LogError($"[NetworkCommWS] Failed to connect to {HubUrl}: {e.Message}");
                    Debug.LogError("[NetworkCommWS] Make sure the GameServer is running first.");
                }
            });
        }

        // -----------------------------------------------------------------------
        // Send  (identical signature to UDP version)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sends a message to all other connected clients via the SignalR hub.
        /// Fire-and-forget — mirrors the UDP send behaviour.
        /// </summary>
        public void SendMessage(string senderId, string payload)
        {
            if (_connection == null || _connection.State != HubConnectionState.Connected)
                return;

            // Fire-and-forget async send — doesn't block Unity's main thread
            _ = _connection.InvokeAsync("SendGameMessage", senderId, payload)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Debug.LogWarning($"[NetworkCommWS] SendMessage failed: {t.Exception?.Message}");
                });
        }

        // -----------------------------------------------------------------------
        // Disconnect  (call from MoveCubes.OnDisable)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Gracefully closes the SignalR connection.
        /// </summary>
        public void Disconnect()
        {
            if (_connection == null) return;
            _ = _connection.StopAsync();
            _isConnected = false;
            Debug.Log("[NetworkCommWS] Disconnected.");
        }

        // -----------------------------------------------------------------------
        // Legacy stub — ReceiveMessages() was the UDP background thread entry point.
        // Kept so MoveCubes.cs compiles without changes if it still references it.
        // -----------------------------------------------------------------------
        [Obsolete("WebSocket version uses Connect() instead. This method is a no-op.")]
        public void ReceiveMessages() { }
    }
}