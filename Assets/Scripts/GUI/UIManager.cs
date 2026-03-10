using System.Collections.Generic;
using UnityEngine;

namespace MulticastGame.HUD
{
    /// <summary>
    /// Draws an in-game HUD using Unity's immediate-mode GUI (OnGUI).
    /// Displays: local player ID, currently selected cube, lock status per cube,
    /// and a live connection indicator.
    ///
    /// Attach this component to any persistent GameObject in the scene.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Public state — set by MoveCubes each frame
        // -----------------------------------------------------------------------
        public string LocalPlayerId { get; set; } = "—";
        public string SelectedCubeId { get; set; } = "none";
        public bool IsConnected { get; set; } = false;

        // cubeId -> ownerPlayerId (null = free)
        private readonly Dictionary<string, string> _lockInfo =
            new Dictionary<string, string>();

        private GUIStyle _panelStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _highlightStyle;
        private GUIStyle _dotStyle;
        private bool _stylesInitialised = false;

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        public void UpdateLockInfo(string cubeId, string ownerOrNull)
        {
            _lockInfo[cubeId] = ownerOrNull;
        }

        // -----------------------------------------------------------------------
        // Unity GUI
        // -----------------------------------------------------------------------

        private void InitStyles()
        {
            if (_stylesInitialised) return;

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.1f, 0.82f)) },
                border = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(10, 10, 8, 8)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.85f, 0.9f, 1f) }
            };

            _highlightStyle = new GUIStyle(_labelStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.4f, 1f, 0.6f) }
            };

            _dotStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            _stylesInitialised = true;
        }

        private void OnGUI()
        {
            InitStyles();

            float panelWidth = 230f;
            float panelHeight = 130f + (_lockInfo.Count * 20f);
            float margin = 12f;

            Rect panelRect = new Rect(margin, margin, panelWidth, panelHeight);
            GUI.Box(panelRect, GUIContent.none, _panelStyle);

            float x = panelRect.x + 10f;
            float y = panelRect.y + 8f;
            float lineH = 20f;

            // --- Connection indicator ---
            string dot = IsConnected ? "●" : "●";
            Color dotCol = IsConnected ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);
            string status = IsConnected ? "Connected" : "Waiting…";
            _dotStyle.normal.textColor = dotCol;
            GUI.Label(new Rect(x, y, panelWidth - 20, lineH), $"{dot}  {status}", _dotStyle);
            y += lineH + 4f;

            // Divider
            DrawHorizontalLine(x, y, panelWidth - 20f, new Color(0.3f, 0.3f, 0.5f, 0.6f));
            y += 6f;

            // --- Player ID ---
            GUI.Label(new Rect(x, y, panelWidth - 20, lineH),
                $"Your ID:  ", _labelStyle);
            GUI.Label(new Rect(x + 65f, y, panelWidth - 85f, lineH),
                LocalPlayerId, _highlightStyle);
            y += lineH;

            // --- Selected cube ---
            GUI.Label(new Rect(x, y, panelWidth - 20, lineH),
                $"Selected: ", _labelStyle);
            GUI.Label(new Rect(x + 65f, y, panelWidth - 85f, lineH),
                SelectedCubeId == "none" ? "—" : SelectedCubeId, _highlightStyle);
            y += lineH;

            // Divider
            DrawHorizontalLine(x, y + 2f, panelWidth - 20f, new Color(0.3f, 0.3f, 0.5f, 0.6f));
            y += 10f;

            // --- Cube lock states ---
            GUI.Label(new Rect(x, y, panelWidth - 20, lineH),
                "Cube locks:", _labelStyle);
            y += lineH;

            foreach (var kvp in _lockInfo)
            {
                string owner = string.IsNullOrEmpty(kvp.Value) ? "free" : kvp.Value;
                bool isMe = kvp.Value == LocalPlayerId;
                bool isFree = string.IsNullOrEmpty(kvp.Value);

                Color col = isFree ? new Color(0.6f, 0.6f, 0.6f)
                          : isMe ? new Color(0.4f, 1f, 0.6f)
                                    : new Color(1f, 0.5f, 0.3f);

                GUIStyle s = new GUIStyle(_labelStyle) { normal = { textColor = col } };
                GUI.Label(new Rect(x + 8f, y, panelWidth - 28f, lineH),
                    $"{kvp.Key}  →  {owner}", s);
                y += lineH;
            }

            // --- Controls hint (bottom-right corner) ---
            float hintW = 200f;
            float hintH = 66f;
            Rect hintRect = new Rect(
                Screen.width - hintW - margin,
                Screen.height - hintH - margin,
                hintW, hintH);
            GUI.Box(hintRect, GUIContent.none, _panelStyle);
            GUIStyle hint = new GUIStyle(_labelStyle)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.6f, 0.65f, 0.75f) }
            };
            float hx = hintRect.x + 8f;
            float hy = hintRect.y + 6f;
            GUI.Label(new Rect(hx, hy, hintW - 16, 18), "Click cube  →  select", hint);
            GUI.Label(new Rect(hx, hy + 18, hintW - 16, 18), "WASD / Arrows  →  move", hint);
            GUI.Label(new Rect(hx, hy + 36, hintW - 16, 18), "Escape  →  deselect", hint);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        private static void DrawHorizontalLine(float x, float y, float width, Color col)
        {
            Texture2D lineTex = MakeTex(1, 1, col);
            GUI.DrawTexture(new Rect(x, y, width, 1f), lineTex);
        }
    }
}