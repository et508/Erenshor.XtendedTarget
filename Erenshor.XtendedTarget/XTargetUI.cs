using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Erenshor.XTarget
{

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

            if (GameObject.Find(ControllerName) != null) yield break;

            var host = new GameObject(ControllerName);
            host.transform.SetParent(anchor.transform, false);
            host.AddComponent<XTargetUIController>();
            XTargetPlugin.Log.LogInfo("[XTarget] UIController spawned under " + AnchorName);
        }
    }

    internal class XTargetUIController : MonoBehaviour
    {

        private static readonly Color32 C_WindowBg    = Hex("#0F1014", 245);
        private static readonly Color32 C_TitleBg     = Hex("#1A1D23", 255);
        private static readonly Color32 C_Border      = Hex("#2D3139", 255);
        private static readonly Color32 C_RowNormal   = Hex("#14171C", 220);
        private static readonly Color32 C_RowAggro    = Hex("#2D0A0A", 220);
        private static readonly Color32 C_TextPri     = Hex("#F1F5F9", 255);
        private static readonly Color32 C_TextMuted   = Hex("#64748B", 255);
        private static readonly Color32 C_Danger      = Hex("#EF4444", 255);
        private static readonly Color32 C_HateGold    = Hex("#FFAA00", 255);
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

        private Canvas        _canvas;
        private RectTransform _window;
        private bool          _visible = true;
        private bool          _locked;

        private bool    _dragging;
        private Vector2 _dragOffset;

        private TextMeshProUGUI _lockBtnLabel;

        private SlotRow[] _rows;

        private TextMeshProUGUI _emptyLabel;
        private RectTransform   _body;

        private Image           _windowBgImage;
        private GameObject      _titleBarGO;
        private Outline         _windowOutline;
        private bool            _chromeVisible = true;

        private class SlotRow
        {
            public GameObject   Root;
            public Image        RowBg;
            public TextMeshProUGUI SlotNum;
            public Image        HpBarBg;
            public Image        HpBarFill;
            public TextMeshProUGUI HpPct;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI TargetArrow;
            public TextMeshProUGUI HateRank;
            public Button       Btn;

        }

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

            if (Input.GetKeyDown(XTargetPlugin.ToggleKey.Value) && IsGameplayScene())
            {
                _visible = !_visible;
            }

            bool shouldShow = _visible && IsGameplayScene();
            if (_canvas != null) _canvas.gameObject.SetActive(shouldShow);

            if (_canvas == null || !shouldShow) return;

            HandleDrag();
            RefreshSlots();
        }

        private void HandleDrag()
        {
            if (_window == null || _locked || XTargetPlugin.AutoHide.Value) return;

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _window, Input.mousePosition, null, out local);

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

                    XTargetPlugin.WindowX.Value = _window.anchoredPosition.x;
                    XTargetPlugin.WindowY.Value = _window.anchoredPosition.y;
                    XTargetPlugin.Instance.Config.Save();
                }
                _dragging = false;
            }

            if (_dragging)
                _window.position = (Vector2)Input.mousePosition + _dragOffset;
        }

        private void RefreshSlots()
        {
            ExtendedTarget.BuildSlotList();
            var slots = ExtendedTarget.Slots;

            bool isEmpty  = slots.Count == 0;
            bool autoHide = XTargetPlugin.AutoHide.Value;

            bool showChrome = !autoHide;
            if (showChrome != _chromeVisible)
                SetChromeVisible(showChrome);

            _emptyLabel.gameObject.SetActive(showChrome && isEmpty);

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

            if (autoHide)
            {

                _body.offsetMin = new Vector2(0, 0);
                _body.offsetMax = new Vector2(0, 0);
                _window.sizeDelta = new Vector2(WINDOW_W, max * ROW_H);
            }
            else
            {

                _body.offsetMin = new Vector2(PADDING, PADDING);
                _body.offsetMax = new Vector2(-PADDING, -TITLE_H);
                float bodyH = isEmpty ? ROW_H : max * ROW_H + PADDING;
                _window.sizeDelta = new Vector2(WINDOW_W, TITLE_H + bodyH + PADDING);
            }
        }

        private void ApplySlot(SlotRow row, XTargetSlot slot, int num)
        {

            row.RowBg.color = slot.TargetingPlayer ? C_RowAggro : C_RowNormal;

            row.SlotNum.text = num.ToString();

            row.HpBarFill.rectTransform.anchorMax = new Vector2(slot.HpPct, 1f);
            row.HpBarFill.color = HpColor(slot.HpPct);

            row.HpPct.text = Mathf.RoundToInt(slot.HpPct * 100f) + "%";

            row.Name.text  = slot.Name.Length > MAX_NAME
                ? slot.Name.Substring(0, MAX_NAME - 1) + "…"
                : slot.Name;
            row.Name.color = slot.TargetingPlayer ? C_Danger : C_TextPri;

            if (string.IsNullOrEmpty(slot.TargetName))
            {
                row.TargetArrow.text  = "<color=#3D4148>> ---</color>";
            }
            else if (slot.TargetingPlayer)
            {
                row.TargetArrow.text = "<color=#666666>></color> <color=#EF4444><b>YOU</b></color>";
            }
            else
            {
                string tName = slot.TargetName.Length > 8
                    ? slot.TargetName.Substring(0, 7) + "…"
                    : slot.TargetName;
                row.TargetArrow.text = $"<color=#666666>></color> <color=#88CCFF>{tName}</color>";
            }

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

            var captured = slot;
            row.Btn.onClick.RemoveAllListeners();
            row.Btn.onClick.AddListener(() => ExtendedTarget.TargetSlot(captured));
        }

        private void BuildUI()
        {

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

            var hideBtn = MakeIconBtn("HideBtn", titleBar, "Hide");
            var hideRT  = hideBtn.GetComponent<RectTransform>();
            hideRT.anchorMin = new Vector2(1, 0);
            hideRT.anchorMax = new Vector2(1, 1);
            hideRT.pivot     = new Vector2(1, 0.5f);
            hideRT.sizeDelta = new Vector2(40, 0);
            hideRT.anchoredPosition = new Vector2(-52, 0);
            hideBtn.onClick.AddListener(ToggleAutoHide);

            var lockBtn = MakeIconBtn("LockBtn", titleBar, "Unlock");
            var lockRT  = lockBtn.GetComponent<RectTransform>();
            lockRT.anchorMin = new Vector2(1, 0);
            lockRT.anchorMax = new Vector2(1, 1);
            lockRT.pivot     = new Vector2(1, 0.5f);
            lockRT.sizeDelta = new Vector2(48, 0);
            lockRT.anchoredPosition = new Vector2(-2, 0);
            _lockBtnLabel = lockBtn.GetComponentInChildren<TextMeshProUGUI>();
            lockBtn.onClick.AddListener(ToggleLock);

            var bodyGO = new GameObject("Body");
            _body = bodyGO.AddComponent<RectTransform>();
            _body.SetParent(_window, false);
            _body.anchorMin        = new Vector2(0, 0);
            _body.anchorMax        = new Vector2(1, 1);
            _body.offsetMin        = new Vector2(PADDING, PADDING);
            _body.offsetMax        = new Vector2(-PADDING, -TITLE_H);

            var vl = bodyGO.AddComponent<VerticalLayoutGroup>();
            vl.spacing               = 2;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.padding                = new RectOffset(0, 0, 0, 0);

            _emptyLabel = AddTMP("EmptyLabel", _body);
            _emptyLabel.text      = "No enemies in range";
            _emptyLabel.color     = C_EmptyText;
            _emptyLabel.fontSize  = 11;
            _emptyLabel.alignment = TextAlignmentOptions.Center;
            _emptyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = ROW_H;
            _emptyLabel.gameObject.SetActive(false);

            int cap = Mathf.Clamp(XTargetPlugin.MaxSlots.Value, 1, 20);
            _rows = new SlotRow[cap];
            for (int i = 0; i < cap; i++)
            {
                _rows[i] = BuildSlotRow(_body, i);
                _rows[i].Root.SetActive(false);
            }

            XTargetPlugin.Log.LogInfo("[XTarget] uGUI built — " + cap + " slot rows pre-built.");

            if (XTargetPlugin.Locked.Value)
                ToggleLock();
        }

        private SlotRow BuildSlotRow(RectTransform parent, int index)
        {
            var row = new SlotRow();

            var rootGO = new GameObject("Slot_" + index);
            row.Root = rootGO;
            var rootRT = rootGO.AddComponent<RectTransform>();
            rootRT.SetParent(parent, false);

            row.RowBg = rootGO.AddComponent<Image>();
            row.RowBg.color = C_RowNormal;

            var le = rootGO.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth   = 1;

            row.Btn = rootGO.AddComponent<Button>();
            row.Btn.targetGraphic = row.RowBg;

            var cb = row.Btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color32(140, 180, 220, 255);
            cb.pressedColor     = new Color32(100, 140, 180, 255);
            cb.selectedColor    = Color.white;
            row.Btn.colors = cb;

            var hl = rootGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding               = new RectOffset(4, 4, 3, 3);
            hl.spacing               = 4;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;

            row.SlotNum = AddTMP("SlotNum", rootRT);
            row.SlotNum.text      = (index + 1).ToString();
            row.SlotNum.color     = C_TextMuted;
            row.SlotNum.fontSize  = 10;
            row.SlotNum.alignment = TextAlignmentOptions.Center;
            row.SlotNum.gameObject.AddComponent<LayoutElement>().preferredWidth = 14;

            var barContainerGO = new GameObject("HpBarContainer");
            var barContainerRT = barContainerGO.AddComponent<RectTransform>();
            barContainerRT.SetParent(rootRT, false);
            var barLE = barContainerGO.AddComponent<LayoutElement>();
            barLE.preferredWidth  = HP_BAR_W;
            barLE.flexibleWidth   = 0;

            row.HpBarBg = barContainerGO.AddComponent<Image>();
            row.HpBarBg.color = C_HpBarBg;

            var fillGO = new GameObject("HpBarFill");
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.SetParent(barContainerRT, false);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            row.HpBarFill       = fillGO.AddComponent<Image>();
            row.HpBarFill.color = C_HpGreen;

            row.HpPct = AddTMP("HpPct", barContainerRT);
            FillRT(row.HpPct.rectTransform, 0, 0, 0, 0);
            row.HpPct.text      = "100%";
            row.HpPct.color     = C_TextPri;
            row.HpPct.fontSize  = 10;
            row.HpPct.fontStyle = FontStyles.Bold;
            row.HpPct.alignment = TextAlignmentOptions.Center;
            row.HpPct.raycastTarget = false;

            row.Name = AddTMP("Name", rootRT);
            row.Name.text      = "";
            row.Name.fontSize  = 11;
            row.Name.alignment = TextAlignmentOptions.MidlineLeft;
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            row.Name.enableWordWrapping = false;
            row.Name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            row.TargetArrow = AddTMP("TargetArrow", rootRT);
            row.TargetArrow.text      = "";
            row.TargetArrow.fontSize  = 10;
            row.TargetArrow.alignment = TextAlignmentOptions.MidlineLeft;
            row.TargetArrow.richText  = true;
            row.TargetArrow.overflowMode     = TextOverflowModes.Ellipsis;
            row.TargetArrow.enableWordWrapping = false;
            row.TargetArrow.gameObject.AddComponent<LayoutElement>().preferredWidth = 72;

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
            if (_windowBgImage != null)  _windowBgImage.enabled = visible;
            if (_windowOutline != null)  _windowOutline.enabled = visible;
            if (_titleBarGO    != null)  _titleBarGO.SetActive(visible);
        }

        private void ToggleLock()
        {
            _locked = !_locked;
            if (_lockBtnLabel != null)
            {
                _lockBtnLabel.text  = _locked ? "Lock" : "Unlock";
                _lockBtnLabel.color = _locked ? C_HateGold : Hex("#64748B", 255);
            }
            XTargetPlugin.Locked.Value = _locked;
            XTargetPlugin.Instance.Config.Save();
        }

        private void ToggleAutoHide()
        {
            XTargetPlugin.AutoHide.Value = true;
            XTargetPlugin.Instance.Config.Save();
        }

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
            img.color = new Color(0, 0, 0, 0);
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