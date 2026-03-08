using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Erenshor.XTarget
{
    // ─────────────────────────────────────────────────────────────────────────
    // XTargetUI  —  static bootstrap (called once from Plugin.Awake)
    // Mirrors the AutoShor pattern: persistent loader watches scene events
    // and spawns the UI controller under GameManager each scene.
    // ─────────────────────────────────────────────────────────────────────────
    internal static class XTargetUI
    {
        private const  string LoaderName  = "XTarget_Loader";
        private static bool   _initialized;

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            EnsureLoaderExists();
            SceneManager.sceneLoaded        += (s, m) => { EnsureLoaderExists(); XTargetLoader.Instance?.TrySpawnUI(); };
            SceneManager.activeSceneChanged += (o, n) => { EnsureLoaderExists(); XTargetLoader.Instance?.TrySpawnUI(); };

            XTargetPlugin.Log.LogInfo("[XTarget] UI bootstrap initialized.");
        }

        internal static void EnsureLoaderExists()
        {
            if (XTargetLoader.Instance != null) return;
            var go = GameObject.Find(LoaderName) ?? new GameObject(LoaderName);
            Object.DontDestroyOnLoad(go);
            if (go.GetComponent<XTargetLoader>() == null)
                go.AddComponent<XTargetLoader>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // XTargetLoader  —  persistent MonoBehaviour that survives scene loads.
    // Waits for GameManager to exist, then spawns XTargetUIController once.
    // ─────────────────────────────────────────────────────────────────────────
    internal class XTargetLoader : MonoBehaviour
    {
        internal static XTargetLoader Instance;

        private const string AnchorName     = "GameManager";
        private const string ControllerName = "XTarget_UI";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            name     = "XTarget_Loader";
            XTargetPlugin.Log.LogInfo("[XTarget] Loader awake.");
            TrySpawnUI();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        internal void TrySpawnUI() => StartCoroutine(WaitAndSpawn());

        private IEnumerator WaitAndSpawn()
        {
            // Wait until GameManager exists in the scene (up to 30 s)
            GameObject anchor = null;
            float waited = 0f;
            while (anchor == null && waited < 30f)
            {
                anchor = GameObject.Find(AnchorName);
                if (anchor == null) { yield return null; waited += Time.unscaledDeltaTime; }
            }

            if (anchor == null)
            {
                XTargetPlugin.Log.LogWarning("[XTarget] GameManager not found — UI not spawned.");
                yield break;
            }

            // Only one controller per scene
            if (GameObject.Find(ControllerName) != null) yield break;

            var host = new GameObject(ControllerName);
            host.transform.SetParent(anchor.transform, false);
            host.AddComponent<XTargetUIController>();
            XTargetPlugin.Log.LogInfo("[XTarget] UIController spawned under " + AnchorName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // XTargetUIController  —  builds and drives the uGUI window.
    //
    // Layout (fixed-width 280 px, height grows with slot count):
    //
    //  ┌─────────────────────────────┐  ← title bar (28 px, draggable)
    //  │  ⊞  Extended Targets    [—] │
    //  ├─────────────────────────────┤
    //  │ 1 [████░░] 72%  Goblin  YOU ▶ #1 │  ← slot row (26 px)
    //  │ 2 [██████] 100% Spider  Ael ▶    │
    //  │  …                           │
    //  └─────────────────────────────┘
    //
    // Each slot row is a child of the vertical layout group inside the body.
    // Row sub-objects: SlotNum | HpBarBg > HpBarFill | HpPct | Name | Arrow+Target | HateRank
    // ─────────────────────────────────────────────────────────────────────────
    internal class XTargetUIController : MonoBehaviour
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color32 C_WindowBg    = Hex("#0F1014", 245);
        private static readonly Color32 C_TitleBg     = Hex("#1A1D23", 255);
        private static readonly Color32 C_Border      = Hex("#2D3139", 255);
        private static readonly Color32 C_RowNormal   = Hex("#14171C", 220);
        private static readonly Color32 C_RowAggro    = Hex("#2D0A0A", 220);   // on player
        private static readonly Color32 C_RowHover    = Hex("#1A3A5C", 220);
        private static readonly Color32 C_TextPri     = Hex("#F1F5F9", 255);
        private static readonly Color32 C_TextMuted   = Hex("#64748B", 255);
        private static readonly Color32 C_TextGroup   = Hex("#88CCFF", 255);   // group member target
        private static readonly Color32 C_Danger      = Hex("#EF4444", 255);   // YOU / top hate
        private static readonly Color32 C_HateGold    = Hex("#FFAA00", 255);   // #1 hate
        private static readonly Color32 C_EmptyText   = Hex("#3D4148", 255);
        private static readonly Color32 C_HpGreen     = Hex("#10B981", 255);
        private static readonly Color32 C_HpYellow    = Hex("#F59E0B", 255);
        private static readonly Color32 C_HpRed       = Hex("#EF4444", 255);
        private static readonly Color32 C_HpBarBg     = Hex("#1C1F25", 255);

        private const int   WINDOW_W   = 280;
        private const int   TITLE_H    = 28;
        private const int   ROW_H      = 26;
        private const int   PADDING    = 6;
        private const int   HP_BAR_W   = 80;
        private const int   MAX_NAME   = 13;

        // ── Runtime ───────────────────────────────────────────────────────────
        private Canvas        _canvas;
        private RectTransform _window;
        private bool          _minimized;
        private bool          _visible = true;
        private bool          _locked;

        // Drag state
        private bool    _dragging;
        private Vector2 _dragOffset;

        // Lock button label ref so we can update its icon
        private TextMeshProUGUI _lockBtnLabel;

        // Slot row pool — one entry per visible slot
        private SlotRow[] _rows;

        // No-aggro label shown when list is empty
        private TextMeshProUGUI _emptyLabel;
        private RectTransform   _body;

        // Chrome refs for auto-hide (background, title bar, border outline)
        private Image           _windowBgImage;
        private GameObject      _titleBarGO;
        private Outline         _windowOutline;
        private bool            _chromeVisible = true;

        // ─────────────────────────────────────────────────────────────────────
        // Inner class that owns the uGUI objects for one row
        // ─────────────────────────────────────────────────────────────────────
        private class SlotRow
        {
            public GameObject   Root;
            public Image        RowBg;
            public TextMeshProUGUI SlotNum;
            public Image        HpBarBg;
            public Image        HpBarFill;
            public TextMeshProUGUI HpPct;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI TargetArrow;  // "▶ <name>"
            public TextMeshProUGUI HateRank;
            public Button       Btn;

        }

        // ─────────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            BuildUI();
        }

        private static bool IsGameplayScene()
        {
            string s = SceneManager.GetActiveScene().name;
            return s != "Menu" && s != "LoadScene";
        }

        private void Update()
        {
            // Toggle visibility (gameplay scenes only)
            if (Input.GetKeyDown(XTargetPlugin.ToggleKey.Value) && IsGameplayScene())
            {
                _visible = !_visible;
            }

            // Force-hide in non-gameplay scenes regardless of _visible
            bool shouldShow = _visible && IsGameplayScene();
            if (_canvas != null) _canvas.gameObject.SetActive(shouldShow);

            if (_canvas == null || !shouldShow) return;

            HandleDrag();
            RefreshSlots();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Drag  (title-bar only)
        // ─────────────────────────────────────────────────────────────────────
        private void HandleDrag()
        {
            if (_window == null || _locked || !_chromeVisible) return;

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _window, Input.mousePosition, null, out local);
                // Title bar is the top 28 px; in local space y goes 0 → negative downward
                if (local.x >= 0 && local.x <= _window.sizeDelta.x &&
                    local.y <= 0 && local.y >= -TITLE_H)
                {
                    _dragging   = true;
                    _dragOffset = (Vector2)_window.position - (Vector2)Input.mousePosition;
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (_dragging)
                {
                    // Persist final position to disk
                    XTargetPlugin.WindowX.Value = _window.anchoredPosition.x;
                    XTargetPlugin.WindowY.Value = _window.anchoredPosition.y;
                    XTargetPlugin.Instance.Config.Save();
                }
                _dragging = false;
            }

            if (_dragging)
                _window.position = (Vector2)Input.mousePosition + _dragOffset;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Refresh slot rows from the current aggro list
        // ─────────────────────────────────────────────────────────────────────
        private void RefreshSlots()
        {
            ExtendedTarget.BuildSlotList();
            var slots = ExtendedTarget.Slots;

            bool isEmpty = slots.Count == 0;

            // Auto-hide chrome when nothing has aggro
            bool showChrome = !XTargetPlugin.AutoHide.Value || !isEmpty;
            if (showChrome != _chromeVisible)
                SetChromeVisible(showChrome);

            _emptyLabel.gameObject.SetActive(isEmpty && _chromeVisible);

            int max = Mathf.Min(slots.Count, _rows.Length);

            for (int i = 0; i < _rows.Length; i++)
            {
                if (i < max)
                {
                    _rows[i].Root.SetActive(true);
                    ApplySlot(_rows[i], slots[i], i + 1);
                }
                else
                {
                    _rows[i].Root.SetActive(false);
                }
            }

            // Resize window height to fit active rows
            float bodyH = isEmpty
                ? ROW_H                                    // just enough for the empty label
                : max * ROW_H + PADDING;
            _window.sizeDelta = new Vector2(WINDOW_W, TITLE_H + bodyH + PADDING);
        }

        private void ApplySlot(SlotRow row, XTargetSlot slot, int num)
        {
            // Row background — red tint if this NPC is on the player
            row.RowBg.color = slot.TargetingPlayer ? C_RowAggro : C_RowNormal;

            // Slot number
            row.SlotNum.text = num.ToString();

            // HP bar fill + colour
            row.HpBarFill.rectTransform.anchorMax = new Vector2(slot.HpPct, 1f);
            row.HpBarFill.color = HpColor(slot.HpPct);

            // HP %
            row.HpPct.text = Mathf.RoundToInt(slot.HpPct * 100f) + "%";

            // Name — truncate if needed
            row.Name.text  = slot.Name.Length > MAX_NAME
                ? slot.Name.Substring(0, MAX_NAME - 1) + "…"
                : slot.Name;
            row.Name.color = slot.TargetingPlayer ? C_Danger : C_TextPri;

            // Arrow + target name
            if (string.IsNullOrEmpty(slot.TargetName))
            {
                row.TargetArrow.text  = "<color=#3D4148>▶ ---</color>";
            }
            else if (slot.TargetingPlayer)
            {
                row.TargetArrow.text = "<color=#666666>▶</color> <color=#EF4444><b>YOU</b></color>";
            }
            else
            {
                string tName = slot.TargetName.Length > 8
                    ? slot.TargetName.Substring(0, 7) + "…"
                    : slot.TargetName;
                row.TargetArrow.text = $"<color=#666666>▶</color> <color=#88CCFF>{tName}</color>";
            }

            // Hate rank
            if (slot.PlayerHateRank > 0)
            {
                row.HateRank.gameObject.SetActive(true);
                row.HateRank.text  = "#" + slot.PlayerHateRank;
                row.HateRank.color = slot.PlayerHateRank == 1 ? C_HateGold : C_TextMuted;
            }
            else
            {
                row.HateRank.gameObject.SetActive(false);
            }

            // Wire click to target — capture index to avoid closure bug
            var captured = slot;
            row.Btn.onClick.RemoveAllListeners();
            row.Btn.onClick.AddListener(() => ExtendedTarget.TargetSlot(captured));
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI Build
        // ─────────────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            // ── Canvas ────────────────────────────────────────────────────────
            var canvasGO = new GameObject("XTarget_Canvas");
            canvasGO.transform.SetParent(transform, false);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 998;

            var cs = canvasGO.AddComponent<CanvasScaler>();
            cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            cs.matchWidthOrHeight  = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            // ── Window panel ──────────────────────────────────────────────────
            _window = MakeRT("XTarget_Window", canvasGO.transform);
            _window.anchorMin        = new Vector2(0, 1);
            _window.anchorMax        = new Vector2(0, 1);
            _window.pivot            = new Vector2(0, 1);
            _window.anchoredPosition = new Vector2(XTargetPlugin.WindowX.Value, XTargetPlugin.WindowY.Value);
            _window.sizeDelta        = new Vector2(WINDOW_W, TITLE_H + ROW_H + PADDING * 2);

            _windowBgImage = AddImage(_window.gameObject, C_WindowBg);
            _windowOutline = _window.gameObject.AddComponent<Outline>();
            _windowOutline.effectColor    = C_Border;
            _windowOutline.effectDistance = new Vector2(1, -1);

            // ── Title bar ─────────────────────────────────────────────────────
            var titleBar = MakeRT("TitleBar", _window);
            _titleBarGO  = titleBar.gameObject;
            titleBar.anchorMin = new Vector2(0, 1);
            titleBar.anchorMax = new Vector2(1, 1);
            titleBar.pivot     = new Vector2(0, 1);
            titleBar.sizeDelta = new Vector2(0, TITLE_H);
            titleBar.anchoredPosition = Vector2.zero;
            AddImage(titleBar.gameObject, C_TitleBg);

            var titleTxt = AddTMP("TitleText", titleBar);
            FillRT(titleTxt.rectTransform, 8, -30, 0, 0);
            titleTxt.text      = "<color=#F1F5F9>Extended </color><color=#008DFD>Targets</color>";
            titleTxt.fontSize  = 12;
            titleTxt.fontStyle = FontStyles.Bold;
            titleTxt.alignment = TextAlignmentOptions.MidlineLeft;

            // Lock button (to the left of minimize button)
            var lockBtn = MakeIconBtn("LockBtn", titleBar, "🔓");
            var lockRT  = lockBtn.GetComponent<RectTransform>();
            lockRT.anchorMin = new Vector2(1, 0);
            lockRT.anchorMax = new Vector2(1, 1);
            lockRT.pivot     = new Vector2(1, 0.5f);
            lockRT.sizeDelta = new Vector2(24, 0);
            lockRT.anchoredPosition = new Vector2(-26, 0);
            _lockBtnLabel = lockBtn.GetComponentInChildren<TextMeshProUGUI>();
            lockBtn.onClick.AddListener(ToggleLock);

            // Minimize button (top-right of title bar)
            var minBtn = MakeIconBtn("MinBtn", titleBar, "—");
            var minRT  = minBtn.GetComponent<RectTransform>();
            minRT.anchorMin = new Vector2(1, 0);
            minRT.anchorMax = new Vector2(1, 1);
            minRT.pivot     = new Vector2(1, 0.5f);
            minRT.sizeDelta = new Vector2(24, 0);
            minRT.anchoredPosition = new Vector2(-2, 0);
            minBtn.onClick.AddListener(ToggleMinimize);

            // ── Body (slot rows live here) ─────────────────────────────────────
            var bodyGO = new GameObject("Body");
            _body = bodyGO.AddComponent<RectTransform>();
            _body.SetParent(_window, false);
            _body.anchorMin        = new Vector2(0, 0);
            _body.anchorMax        = new Vector2(1, 1);
            _body.offsetMin        = new Vector2(PADDING, PADDING);
            _body.offsetMax        = new Vector2(-PADDING, -TITLE_H);

            // Vertical layout for rows
            var vl = bodyGO.AddComponent<VerticalLayoutGroup>();
            vl.spacing               = 2;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.padding                = new RectOffset(0, 0, 0, 0);

            // Empty-list label
            _emptyLabel = AddTMP("EmptyLabel", _body);
            _emptyLabel.text      = "No enemies in range";
            _emptyLabel.color     = C_EmptyText;
            _emptyLabel.fontSize  = 11;
            _emptyLabel.alignment = TextAlignmentOptions.Center;
            _emptyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = ROW_H;
            _emptyLabel.gameObject.SetActive(false);

            // ── Pre-build slot rows ────────────────────────────────────────────
            int cap = Mathf.Clamp(XTargetPlugin.MaxSlots.Value, 1, 20);
            _rows = new SlotRow[cap];
            for (int i = 0; i < cap; i++)
            {
                _rows[i] = BuildSlotRow(_body, i);
                _rows[i].Root.SetActive(false);
            }

            XTargetPlugin.Log.LogInfo("[XTarget] uGUI built — " + cap + " slot rows pre-built.");

            // Restore saved lock state
            if (XTargetPlugin.Locked.Value)
                ToggleLock();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Build one slot row inside the vertical layout group
        //
        //  [slotNum] [hpBarBg[fill]] [hpPct%]  [name]  [▶ target]  [#rank]
        // ─────────────────────────────────────────────────────────────────────
        private SlotRow BuildSlotRow(RectTransform parent, int index)
        {
            var row = new SlotRow();

            // Root container
            var rootGO = new GameObject("Slot_" + index);
            row.Root = rootGO;
            var rootRT = rootGO.AddComponent<RectTransform>();
            rootRT.SetParent(parent, false);

            row.RowBg = rootGO.AddComponent<Image>();
            row.RowBg.color = C_RowNormal;

            // Fixed row height
            var le = rootGO.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth   = 1;

            // Invisible click target (full row) — sits on the Image
            row.Btn = rootGO.AddComponent<Button>();
            row.Btn.targetGraphic = row.RowBg;

            // Hover colour swap via ColorBlock
            var cb = row.Btn.colors;
            cb.normalColor      = Color.white;  // multiplied with RowBg.color
            cb.highlightedColor = new Color32(140, 180, 220, 255);
            cb.pressedColor     = new Color32(100, 140, 180, 255);
            cb.selectedColor    = Color.white;
            row.Btn.colors = cb;

            // ── Horizontal layout inside row ──────────────────────────────────
            var hl = rootGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding               = new RectOffset(4, 4, 3, 3);
            hl.spacing               = 4;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;

            // [1] Slot number  (14 px wide)
            row.SlotNum = AddTMP("SlotNum", rootRT);
            row.SlotNum.text      = (index + 1).ToString();
            row.SlotNum.color     = C_TextMuted;
            row.SlotNum.fontSize  = 10;
            row.SlotNum.alignment = TextAlignmentOptions.Center;
            row.SlotNum.gameObject.AddComponent<LayoutElement>().preferredWidth = 14;

            // [2] HP bar  (HP_BAR_W px wide)
            var barContainerGO = new GameObject("HpBarContainer");
            var barContainerRT = barContainerGO.AddComponent<RectTransform>();
            barContainerRT.SetParent(rootRT, false);
            var barLE = barContainerGO.AddComponent<LayoutElement>();
            barLE.preferredWidth  = HP_BAR_W;
            barLE.flexibleWidth   = 0;

            // Background
            row.HpBarBg = barContainerGO.AddComponent<Image>();
            row.HpBarBg.color = C_HpBarBg;

            // Fill (child, anchored left→right via anchorMax.x)
            var fillGO = new GameObject("HpBarFill");
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.SetParent(barContainerRT, false);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;   // updated each frame to slot.HpPct
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            row.HpBarFill       = fillGO.AddComponent<Image>();
            row.HpBarFill.color = C_HpGreen;

            // HP % label (over the bar)
            row.HpPct = AddTMP("HpPct", barContainerRT);
            FillRT(row.HpPct.rectTransform, 0, 0, 0, 0);
            row.HpPct.text      = "100%";
            row.HpPct.color     = C_TextPri;
            row.HpPct.fontSize  = 10;
            row.HpPct.fontStyle = FontStyles.Bold;
            row.HpPct.alignment = TextAlignmentOptions.Center;
            row.HpPct.raycastTarget = false;

            // [3] Name  (flexible)
            row.Name = AddTMP("Name", rootRT);
            row.Name.text      = "";
            row.Name.fontSize  = 11;
            row.Name.alignment = TextAlignmentOptions.MidlineLeft;
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            row.Name.enableWordWrapping = false;
            row.Name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // [4] Arrow + target name  (72 px wide)
            row.TargetArrow = AddTMP("TargetArrow", rootRT);
            row.TargetArrow.text      = "";
            row.TargetArrow.fontSize  = 10;
            row.TargetArrow.alignment = TextAlignmentOptions.MidlineLeft;
            row.TargetArrow.richText  = true;
            row.TargetArrow.overflowMode     = TextOverflowModes.Ellipsis;
            row.TargetArrow.enableWordWrapping = false;
            row.TargetArrow.gameObject.AddComponent<LayoutElement>().preferredWidth = 72;

            // [5] Hate rank  (20 px wide)
            row.HateRank = AddTMP("HateRank", rootRT);
            row.HateRank.text      = "";
            row.HateRank.fontSize  = 10;
            row.HateRank.alignment = TextAlignmentOptions.MidlineRight;
            row.HateRank.gameObject.AddComponent<LayoutElement>().preferredWidth = 20;

            return row;
        }

        private void SetChromeVisible(bool visible)
        {
            _chromeVisible = visible;

            if (_windowBgImage != null)  _windowBgImage.enabled  = visible;
            if (_windowOutline != null)  _windowOutline.enabled  = visible;
            if (_titleBarGO    != null)  _titleBarGO.SetActive(visible);

            // When chrome is hidden also collapse the window height to just the rows,
            // with no extra padding so it sits flush. Restore full height when shown.
            if (!_minimized)
            {
                int max = Mathf.Min(ExtendedTarget.Slots.Count, _rows.Length);
                _window.sizeDelta = visible
                    ? new Vector2(WINDOW_W, TITLE_H + max * ROW_H + PADDING * 2)
                    : new Vector2(WINDOW_W, max * ROW_H);
            }
        }

        private void ToggleLock()
        {
            _locked = !_locked;
            if (_lockBtnLabel != null)
            {
                _lockBtnLabel.text  = _locked ? "🔒" : "🔓";
                _lockBtnLabel.color = _locked ? C_HateGold : Hex("#64748B", 255);
            }
            XTargetPlugin.Locked.Value = _locked;
            XTargetPlugin.Instance.Config.Save();
        }

        private void ToggleMinimize()
        {
            _minimized = !_minimized;
            _body.gameObject.SetActive(!_minimized);
            _window.sizeDelta = _minimized
                ? new Vector2(WINDOW_W, TITLE_H)
                : new Vector2(WINDOW_W, TITLE_H + ROW_H + PADDING * 2);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Builder helpers
        // ─────────────────────────────────────────────────────────────────────
        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("XTarget_EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Object.DontDestroyOnLoad(es);
        }

        private static RectTransform MakeRT(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image AddImage(GameObject go, Color32 colour)
        {
            var img = go.AddComponent<Image>();
            img.color = colour;
            return img;
        }

        private static void AddOutline(GameObject go, Color32 colour)
        {
            var ol = go.AddComponent<Outline>();
            ol.effectColor    = colour;
            ol.effectDistance = new Vector2(1, -1);
        }

        private static TextMeshProUGUI AddTMP(string name, RectTransform parent)
        {
            var go  = new GameObject(name);
            var rt  = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.color    = Hex("#F1F5F9", 255);
            tmp.fontSize = 11;
            tmp.richText = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        // Stretch rect to fill parent with insets
        private static void FillRT(RectTransform rt, float left, float right, float bottom, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left,   bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static Button MakeIconBtn(string name, RectTransform parent, string label)
        {
            var go  = new GameObject(name);
            var rt  = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);   // transparent bg
            var btn = go.AddComponent<Button>();

            var lblGO = new GameObject("Label");
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.SetParent(rt, false);
            FillRT(lblRT, 0, 0, 0, 0);
            var tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.color     = Hex("#64748B", 255);
            tmp.fontSize  = 12;
            tmp.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────
        private static Color HpColor(float pct)
        {
            if (pct > 0.5f)
                return Color32.Lerp(C_HpYellow, C_HpGreen, (pct - 0.5f) * 2f);
            else
                return Color32.Lerp(C_HpRed, C_HpYellow, pct * 2f);
        }

        private static Color32 Hex(string hex, byte alpha = 255)
        {
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length != 6) return new Color32(255, 255, 255, alpha);
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color32(r, g, b, alpha);
        }
    }
}