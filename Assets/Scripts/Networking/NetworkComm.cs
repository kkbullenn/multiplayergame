using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace MulticastGame.Networking
{
    /// Handles UDP multicast communication between game instances.
    public class NetworkComm
    {
        // multicast group and port configuration
        private const string MULTICAST_GROUP = "230.0.0.1";
        private const int MULTICAST_PORT = 11000;
        private const int BUFFER_SIZE = 2048;

        // dupe detection: track last seen sequence number for each sender
        private readonly Dictionary<string, int> _lastSeqBySender = new Dictionary<string, int>();
        private readonly object _seqLock = new object();

        // sequence number for outgoing messages (incremented with each send)
        private int _outSeq = 0;

        public delegate void MsgHandler(string senderId, string payload);
        public event MsgHandler MsgReceived;

        // -----------------------------------------------------------------------
        // Send
        // -----------------------------------------------------------------------

        /// <summary>
        /// Broadcasts a message to the multicast group.
        /// Wire format:  SEQ=<n>|FROM=<senderId>|<payload>
        /// </summary>
        public void SendMessage(string senderId, string payload)
        {
            int seq;
            lock (_seqLock) { seq = ++_outSeq; }

            string wire = $"SEQ={seq}|FROM={senderId}|{payload}";
            byte[] data = Encoding.ASCII.GetBytes(wire);

            try
            {
                using Socket sock = new Socket(AddressFamily.InterNetwork,
                                               SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint ep = new IPEndPoint(IPAddress.Parse(MULTICAST_GROUP), MULTICAST_PORT);
                sock.SendTo(data, ep);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkComm] SendMessage error: {e.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Receive  (run on a background thread)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Blocking receive loop. Intended to run on a dedicated background thread.
        /// Fires MsgReceived for every valid, non-duplicate packet.
        /// </summary>
        public void ReceiveMessages()
        {
            Socket sock = null;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                EndPoint localEP = new IPEndPoint(IPAddress.Any, MULTICAST_PORT);
                sock.Bind(localEP);

                MulticastOption mcastOpt = new MulticastOption(
                    IPAddress.Parse(MULTICAST_GROUP), IPAddress.Any);
                sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mcastOpt);

                byte[] buf = new byte[BUFFER_SIZE];
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                while (true)
                {
                    int received = sock.ReceiveFrom(buf, ref remoteEP);
                    string raw = Encoding.ASCII.GetString(buf, 0, received);
                    ProcessIncoming(raw);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkComm] ReceiveMessages error: {e.Message}");
            }
            finally
            {
                sock?.Close();
            }
        }

        // -----------------------------------------------------------------------
        // Internal packet processing
        // -----------------------------------------------------------------------

        private void ProcessIncoming(string raw)
        {
            // Expected format:  SEQ=<n>|FROM=<id>|<payload>
            try
            {
                // Split into at most 3 parts so payload can contain '|'
                string[] parts = raw.Split('|', 3);
                if (parts.Length < 3) return;

                if (!parts[0].StartsWith("SEQ=")) return;
                if (!int.TryParse(parts[0].Substring(4), out int seq)) return;

                if (!parts[1].StartsWith("FROM=")) return;
                string senderId = parts[1].Substring(5).Trim('\0');

                string payload = parts[2].Trim('\0');

                // --- Duplicate / out-of-order detection ---
                lock (_seqLock)
                {
                    if (_lastSeqBySender.TryGetValue(senderId, out int lastSeen))
                    {
                        if (seq <= lastSeen) return; // duplicate or old packet — discard
                    }
                    _lastSeqBySender[senderId] = seq;
                }

                MsgReceived?.Invoke(senderId, payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkComm] ProcessIncoming error: {e.Message}");
            }
        }
    }
}