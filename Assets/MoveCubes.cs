using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

using MulticastGame.Networking;
using MulticastGame.GameLogic;
using MulticastGame.HUD;

/// <summary>
/// Main MonoBehaviour — entry point for the multiplayer cube game.
///
/// Responsibilities:
///   • Spawns and labels 3 cubes in the scene.
///   • Manages click-to-select + WASD/arrow-key movement for the local player.
///   • Bridges the Networking layer (NetworkComm) ↔ the Logic layer (GameState).
///   • Applies interpolated remote position updates to Unity GameObjects.
///   • Feeds status data to the GUI layer (UIManager).
///
/// Namespace: (root — intentionally thin coordinator between namespaced layers)
/// </summary>
public class MoveCubes : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    private const float MOVE_SPEED = 4.0f;   // units per second
    private const float LERP_SPEED = 12f;    // remote-cube interpolation speed
    private const float SEND_INTERVAL = 0.05f;  // seconds between network broadcasts (~20 Hz)
    private const float COLLISION_DIST = 1.2f;   // units — visual "collision" warning distance

    private static readonly string[] CUBE_IDS = { "Cube1", "Cube2", "Cube3" };

    private static readonly Vector3[] INITIAL_POSITIONS =
    {
        new Vector3(-3f, 0f, 0f),
        new Vector3( 0f, 0f, 0f),
        new Vector3( 3f, 0f, 0f)
    };

    // -----------------------------------------------------------------------
    // Private fields
    // -----------------------------------------------------------------------

    // Layers
    private NetworkComm _network;
    private GameState _gameState;
    private UIManager _ui;

    // Scene objects
    private readonly Dictionary<string, GameObject> _cubeObjects = new();
    private readonly Dictionary<string, MeshRenderer> _cubeRenderers = new();

    // Materials for visual feedback
    private Material _defaultMaterial;
    private Material _selectedMaterial;
    private Material _lockedByOtherMaterial;

    // Local player identity (generated once, persists per run)
    private string _localPlayerId;

    // Input state
    private string _selectedCubeId = null;

    // Network send throttle
    private float _sendTimer = 0f;

    // Thread-safe queue for incoming network messages (NetworkComm fires on bg thread)
    private readonly Queue<(string senderId, string payload)> _msgQueue = new();
    private readonly object _msgLock = new object();

    // Track which cubes need position sync applied this frame
    private readonly Dictionary<string, Vector3> _targetPositions = new();
    private readonly object _targetLock = new object();

    // -----------------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------------

    void Start()
    {
        // Generate a short unique ID for this player (last 4 chars of a GUID)
        _localPlayerId = "P-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
        Debug.Log($"[MoveCubes] Local player ID: {_localPlayerId}");

        // --- Game Logic layer ---
        _gameState = new GameState();
        _gameState.CubePositionChanged += OnCubePositionChanged;
        _gameState.CubeLockChanged += OnCubeLockChanged;

        // --- Create materials ---
        _defaultMaterial = new Material(Shader.Find("Standard"));
        _defaultMaterial.color = new Color(0.4f, 0.7f, 1.0f);

        _selectedMaterial = new Material(Shader.Find("Standard"));
        _selectedMaterial.color = new Color(0.2f, 1.0f, 0.5f);

        _lockedByOtherMaterial = new Material(Shader.Find("Standard"));
        _lockedByOtherMaterial.color = new Color(1.0f, 0.45f, 0.2f);

        // --- Spawn cubes ---
        for (int i = 0; i < CUBE_IDS.Length; i++)
        {
            string id = CUBE_IDS[i];
            Vector3 pos = INITIAL_POSITIONS[i];

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = id;
            cube.transform.position = pos;

            // Label above cube
            CreateLabel(cube, id);

            MeshRenderer rend = cube.GetComponent<MeshRenderer>();
            rend.material = new Material(_defaultMaterial); // each cube gets its own material instance
            _cubeObjects[id] = cube;
            _cubeRenderers[id] = rend;

            _gameState.RegisterCube(id, pos);
        }

        // --- GUI layer ---
        GameObject uiGO = new GameObject("UIManager");
        _ui = uiGO.AddComponent<UIManager>();
        _ui.LocalPlayerId = _localPlayerId;

        foreach (string id in CUBE_IDS)
            _ui.UpdateLockInfo(id, null);

        // --- Networking layer ---
        _network = new NetworkComm();
        _network.MsgReceived += OnNetworkMessage; // fires on background thread

        Thread recvThread = new Thread(_network.ReceiveMessages)
        {
            IsBackground = true,
            Name = "MulticastReceive"
        };
        recvThread.Start();

        _ui.IsConnected = true;
    }

    void Update()
    {
        // --- Drain the thread-safe message queue ---
        DrainMessageQueue();

        // --- Apply interpolated remote positions ---
        ApplyRemotePositions();

        // --- Handle cube selection via mouse click ---
        HandleSelection();

        // --- Handle keyboard movement ---
        HandleMovement();

        // --- Check proximity warnings ---
        CheckCollisions();

        // --- Update GUI ---
        _ui.SelectedCubeId = _selectedCubeId ?? "none";
    }

    void OnDisable()
    {
        // Release any lock this player holds when quitting
        if (_gameState != null && _localPlayerId != null)
        {
            foreach (string id in CUBE_IDS)
            {
                string payload = GameState.SerializeUnlock(id);
                _network?.SendMessage(_localPlayerId, payload);
            }
            _gameState.UnlockAll(_localPlayerId);
        }
        Debug.Log("[MoveCubes] OnDisable — locks released.");
    }

    // -----------------------------------------------------------------------
    // Input — Selection
    // -----------------------------------------------------------------------

    private void HandleSelection()
    {
        // Click to select a cube
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                string hitName = hit.collider.gameObject.name;
                if (Array.Exists(CUBE_IDS, id => id == hitName))
                {
                    // Check not locked by another player
                    CubeState state = _gameState.GetCube(hitName);
                    if (state?.LockedBy != null && state.LockedBy != _localPlayerId)
                    {
                        Debug.Log($"[MoveCubes] {hitName} is locked by {state.LockedBy}");
                        return;
                    }

                    // Deselect previous
                    if (_selectedCubeId != null && _selectedCubeId != hitName)
                    {
                        _gameState.Unlock(_selectedCubeId, _localPlayerId);
                        _network.SendMessage(_localPlayerId,
                            GameState.SerializeUnlock(_selectedCubeId));
                        UpdateCubeColor(_selectedCubeId);
                    }

                    _selectedCubeId = hitName;
                    _gameState.TryLock(_selectedCubeId, _localPlayerId);
                    _network.SendMessage(_localPlayerId,
                        GameState.SerializeLock(_selectedCubeId));
                    UpdateCubeColor(_selectedCubeId);
                    Debug.Log($"[MoveCubes] Selected {_selectedCubeId}");
                }
                else
                {
                    Deselect();
                }
            }
            else
            {
                Deselect();
            }
        }

        // Escape to deselect
        if (Input.GetKeyDown(KeyCode.Escape))
            Deselect();
    }

    private void Deselect()
    {
        if (_selectedCubeId == null) return;
        _gameState.Unlock(_selectedCubeId, _localPlayerId);
        _network.SendMessage(_localPlayerId,
            GameState.SerializeUnlock(_selectedCubeId));
        UpdateCubeColor(_selectedCubeId);
        _selectedCubeId = null;
    }

    // -----------------------------------------------------------------------
    // Input — Movement
    // -----------------------------------------------------------------------

    private void HandleMovement()
    {
        if (_selectedCubeId == null) return;

        // Read input axes (WASD + arrow keys both map to these)
        float h = Input.GetAxisRaw("Horizontal"); // left/right
        float v = Input.GetAxisRaw("Vertical");   // up/down

        if (h == 0f && v == 0f) return;

        Vector3 delta = new Vector3(h, v, 0f) * (MOVE_SPEED * Time.deltaTime);

        CubeState state = _gameState.GetCube(_selectedCubeId);
        if (state == null) return;

        Vector3 newPos = state.Position + delta;
        _gameState.TryApplyMove(_selectedCubeId, _localPlayerId, newPos);

        // Apply immediately to local object
        if (_cubeObjects.TryGetValue(_selectedCubeId, out GameObject go))
            go.transform.position = newPos;

        // Throttle network sends
        _sendTimer -= Time.deltaTime;
        if (_sendTimer <= 0f)
        {
            _sendTimer = SEND_INTERVAL;
            string payload = GameState.SerializeMove(_selectedCubeId, newPos);
            _network.SendMessage(_localPlayerId, payload);
        }
    }

    // -----------------------------------------------------------------------
    // Collision proximity warning
    // -----------------------------------------------------------------------

    private void CheckCollisions()
    {
        for (int i = 0; i < CUBE_IDS.Length; i++)
        {
            for (int j = i + 1; j < CUBE_IDS.Length; j++)
            {
                if (!_cubeObjects.TryGetValue(CUBE_IDS[i], out GameObject a)) continue;
                if (!_cubeObjects.TryGetValue(CUBE_IDS[j], out GameObject b)) continue;

                if (Vector3.Distance(a.transform.position, b.transform.position) < COLLISION_DIST)
                    Debug.Log($"[MoveCubes] ⚠ Proximity: {CUBE_IDS[i]} ↔ {CUBE_IDS[j]}");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Networking — receive (background thread → main thread queue)
    // -----------------------------------------------------------------------

    private void OnNetworkMessage(string senderId, string payload)
    {
        // This fires on the background receive thread — queue for main thread
        lock (_msgLock)
        {
            _msgQueue.Enqueue((senderId, payload));
        }
    }

    private void DrainMessageQueue()
    {
        List<(string, string)> toProcess = null;
        lock (_msgLock)
        {
            if (_msgQueue.Count == 0) return;
            toProcess = new List<(string, string)>(_msgQueue);
            _msgQueue.Clear();
        }

        foreach (var (senderId, payload) in toProcess)
            ProcessMessage(senderId, payload);
    }

    private void ProcessMessage(string senderId, string payload)
    {
        // Ignore our own messages (we already applied locally)
        if (senderId == _localPlayerId) return;

        string cmd = GameState.ParseCommand(payload, out string cubeId, out Vector3 position);
        if (cmd == null) return;

        switch (cmd)
        {
            case "MOVE":
                // Store target for smooth interpolation
                lock (_targetLock)
                    _targetPositions[cubeId] = position;

                _gameState.TryApplyMove(cubeId, senderId, position);
                break;

            case "LOCK":
                _gameState.TryLock(cubeId, senderId);
                break;

            case "UNLOCK":
                _gameState.Unlock(cubeId, senderId);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Remote position interpolation (main thread)
    // -----------------------------------------------------------------------

    private void ApplyRemotePositions()
    {
        Dictionary<string, Vector3> snapshot;
        lock (_targetLock)
        {
            if (_targetPositions.Count == 0) return;
            snapshot = new Dictionary<string, Vector3>(_targetPositions);
        }

        foreach (var kvp in snapshot)
        {
            if (!_cubeObjects.TryGetValue(kvp.Key, out GameObject go)) continue;

            // Don't override the cube the local player is currently moving
            if (kvp.Key == _selectedCubeId) continue;

            go.transform.position = Vector3.Lerp(
                go.transform.position,
                kvp.Value,
                Time.deltaTime * LERP_SPEED);
        }
    }

    // -----------------------------------------------------------------------
    // GameState callbacks (main thread via DrainMessageQueue)
    // -----------------------------------------------------------------------

    private void OnCubePositionChanged(string cubeId, Vector3 newPos)
    {
        // Position updates are handled directly in ProcessMessage + ApplyRemotePositions
    }

    private void OnCubeLockChanged(string cubeId, string ownerOrNull)
    {
        _ui?.UpdateLockInfo(cubeId, ownerOrNull);
        UpdateCubeColor(cubeId);
    }

    // -----------------------------------------------------------------------
    // Visual helpers
    // -----------------------------------------------------------------------

    private void UpdateCubeColor(string cubeId)
    {
        if (!_cubeRenderers.TryGetValue(cubeId, out MeshRenderer rend)) return;

        CubeState state = _gameState.GetCube(cubeId);
        if (state == null) return;

        if (cubeId == _selectedCubeId)
            rend.material.color = _selectedMaterial.color;
        else if (state.LockedBy != null && state.LockedBy != _localPlayerId)
            rend.material.color = _lockedByOtherMaterial.color;
        else
            rend.material.color = _defaultMaterial.color;
    }

    // -----------------------------------------------------------------------
    // Scene helpers
    // -----------------------------------------------------------------------

    private void CreateLabel(GameObject parent, string text)
    {
        // Create a world-space TextMesh above each cube
        GameObject labelGO = new GameObject($"Label_{text}");
        labelGO.transform.SetParent(parent.transform);
        labelGO.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        labelGO.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);

        TextMesh tm = labelGO.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 28;
        tm.color = Color.white;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
    }
}