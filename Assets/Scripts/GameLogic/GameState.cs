using System;
using System.Collections.Generic;
using UnityEngine;

namespace MulticastGame.GameLogic
{
    /// <summary>
    /// Represents the authoritative state of a single cube in the scene.
    /// </summary>
    public class CubeState
    {
        public string CubeId { get; set; }
        public Vector3 Position { get; set; }
        /// <summary>Which player is currently moving this cube (null = nobody).</summary>
        public string LockedBy { get; set; }

        public CubeState(string cubeId, Vector3 initialPosition)
        {
            CubeId = cubeId;
            Position = initialPosition;
            LockedBy = null;
        }
    }

    /// <summary>
    /// Central game-state store.  All mutation goes through here so the logic
    /// layer stays cleanly separated from networking and Unity MonoBehaviours.
    /// </summary>
    public class GameState
    {
        private readonly Dictionary<string, CubeState> _cubes =
            new Dictionary<string, CubeState>();

        // Fired whenever any cube position changes (cubeId, newPosition)
        public event Action<string, Vector3> CubePositionChanged;

        // Fired when a cube's lock owner changes (cubeId, newOwnerOrNull)
        public event Action<string, string> CubeLockChanged;

        // -----------------------------------------------------------------------
        // Registration
        // -----------------------------------------------------------------------

        public void RegisterCube(string cubeId, Vector3 initialPosition)
        {
            if (!_cubes.ContainsKey(cubeId))
                _cubes[cubeId] = new CubeState(cubeId, initialPosition);
        }

        public IEnumerable<CubeState> AllCubes => _cubes.Values;

        public CubeState GetCube(string cubeId) =>
            _cubes.TryGetValue(cubeId, out var s) ? s : null;

        // -----------------------------------------------------------------------
        // Movement
        // -----------------------------------------------------------------------

        /// <summary>
        /// Attempts to apply a position update.
        /// Rejects the update if the cube is locked by a different player.
        /// Returns true if the update was applied.
        /// </summary>
        public bool TryApplyMove(string cubeId, string requestingPlayer, Vector3 newPosition)
        {
            if (!_cubes.TryGetValue(cubeId, out CubeState state)) return false;

            // Allow if unlocked OR the requesting player already holds the lock
            if (state.LockedBy != null && state.LockedBy != requestingPlayer)
                return false;

            state.Position = newPosition;
            CubePositionChanged?.Invoke(cubeId, newPosition);
            return true;
        }

        // -----------------------------------------------------------------------
        // Locking  (soft lock — tells other clients who is moving a cube)
        // -----------------------------------------------------------------------

        public bool TryLock(string cubeId, string playerId)
        {
            if (!_cubes.TryGetValue(cubeId, out CubeState state)) return false;
            if (state.LockedBy != null && state.LockedBy != playerId) return false;

            state.LockedBy = playerId;
            CubeLockChanged?.Invoke(cubeId, playerId);
            return true;
        }

        public void Unlock(string cubeId, string playerId)
        {
            if (!_cubes.TryGetValue(cubeId, out CubeState state)) return;
            if (state.LockedBy != playerId) return;

            state.LockedBy = null;
            CubeLockChanged?.Invoke(cubeId, null);
        }

        public void UnlockAll(string playerId)
        {
            foreach (var state in _cubes.Values)
                if (state.LockedBy == playerId)
                    Unlock(state.CubeId, playerId);
        }

        // -----------------------------------------------------------------------
        // Message serialisation helpers  (kept in GameLogic so Networking is agnostic)
        // -----------------------------------------------------------------------

        /// <summary>Serialise a MOVE command payload.</summary>
        public static string SerializeMove(string cubeId, Vector3 pos) =>
            $"MOVE|{cubeId}|{pos.x:F4},{pos.y:F4},{pos.z:F4}";

        /// <summary>Serialise a LOCK command payload.</summary>
        public static string SerializeLock(string cubeId) =>
            $"LOCK|{cubeId}";

        /// <summary>Serialise an UNLOCK command payload.</summary>
        public static string SerializeUnlock(string cubeId) =>
            $"UNLOCK|{cubeId}";

        /// <summary>
        /// Parse an incoming payload string.
        /// Returns the command type ("MOVE", "LOCK", "UNLOCK") or null on failure.
        /// </summary>
        public static string ParseCommand(string payload,
            out string cubeId, out Vector3 position)
        {
            cubeId = null;
            position = Vector3.zero;

            string[] parts = payload.Split('|');
            if (parts.Length < 2) return null;

            string cmd = parts[0];
            cubeId = parts[1];

            if (cmd == "MOVE" && parts.Length == 3)
            {
                string[] coords = parts[2].Split(',');
                if (coords.Length == 3 &&
                    float.TryParse(coords[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(coords[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(coords[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    position = new Vector3(x, y, z);
                    return "MOVE";
                }
                return null;
            }

            if (cmd == "LOCK" && parts.Length == 2) return "LOCK";
            if (cmd == "UNLOCK" && parts.Length == 2) return "UNLOCK";

            return null;
        }
    }
}