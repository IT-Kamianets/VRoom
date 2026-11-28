using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>
    /// The editor screen: a dark top bar (logo, tabs, undo, save), a rail and a panel on the left (projects, walls,
    /// objects, view), the 3D view in the middle with a toolbar above it, the properties of the selected wall on the
    /// right and a bar with the project's numbers at the bottom. It is built here from code with uGUI and drives the
    /// <see cref="ApartmentWallBuilder"/> through its public API; the camera is squeezed into the middle area.
    /// </summary>
    public class EditorShell : MonoBehaviour
    {
        // sizes in reference pixels (the canvas scales from 1920 x 1080)
        const float TopH = 88f, BottomH = 104f, RailW = 120f, LeftW = 360f, RightW = 360f, ToolbarH = 76f;

        static readonly Color Bg = Hex("0D0F17");
        static readonly Color Panel = Hex("151927");
        static readonly Color PanelHi = Hex("1D2233");
        static readonly Color Line = Hex("2A3046");
        static readonly Color Accent = Hex("E9573F");
        static readonly Color AccentDim = Hex("3B1E1D");
        static readonly Color TextC = Hex("F2F3F7");
        static readonly Color Muted = Hex("8C93A8");
        static readonly Color Good = Hex("66D19E");
        static readonly Color Bad = Hex("FF7B7B");

        enum Page { Projects, Objects, Room, Materials, Styles, Settings, View, Help }

        ApartmentWallBuilder _b;
        TMP_FontAsset _font;
        Canvas _canvas;
        Sprite _round, _logo;
        readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        RectTransform _leftBody, _rightBody;
        TMP_Text _toast, _bottomName, _bottomSub, _bottomArea, _bottomWalls, _bottomUpdated;
        Button _undoButton;
        readonly List<Button> _tabButtons = new List<Button>();
        readonly List<Button> _railButtons = new List<Button>();
        readonly List<Button> _toolbarButtons = new List<Button>();

        Page _page = Page.Room;
        int _tab = 1;
        int _lastVersion = -1;
        string _lastMessage = "";
        string _rightSignature = "";
        float _toastUntil;
        DateTime _updated = DateTime.Now;
        int _confirmDelete = -1;
        string _newProjectName = "";

        // the wall fields of the Room page are kept between refreshes, so typing is not interrupted
        string _wallWidth = "6", _wallHeight = "2.7", _wallAngle = "90";
        TMP_Text _roomStatus;
        Button _buildButton;

        static readonly string[] TabNames = { "Проєкт", "Редагування", "Вигляд", "Довідка" };
        static readonly Page[] RailPages = { Page.Objects, Page.Room, Page.Materials, Page.Styles, Page.Settings };
        static readonly string[] RailNames = { "Об'єкти", "Кімната", "Матеріали", "Стилі", "Налаштування" };
        static readonly string[] RailIcons = { "cube", "room", "material", "style", "gear" };

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        // ---------------------------------------------------------------- start

        public void Init(ApartmentWallBuilder builder, TMP_FontAsset font)
        {
            _b = builder;
            _font = font;
            _b.UseShell = true;
            _b.PointerOverUi = () => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            _round = MakeRounded(24);
            var logoAsset = Resources.Load<TextAsset>("vroom-logo");
            if (logoAsset != null)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(logoAsset.bytes))
                {
                    _logo = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }

            BuildCanvas();
            BuildTopBar();
            BuildRail();
            var left = MakePanel("Left Panel", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(RailW, BottomH), new Vector2(RailW + LeftW, -TopH));
            _leftBody = Stack(left, "Left Body", 14f, new RectOffset(24, 24, 24, 24));
            Stretch(_leftBody);
            var right = MakePanel("Right Panel", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-RightW, BottomH), new Vector2(0f, -TopH));
            _rightBody = Stack(right, "Right Body", 14f, new RectOffset(24, 24, 24, 24));
            Stretch(_rightBody);
            BuildToolbar();
            BuildBottomBar();
            BuildToast();
            ShowPage(Page.Room, 1);
            RefreshAll();
        }

        void Update()
        {
            if (_b == null) return;
            UpdateViewport();

            if (!_firstPageChosen && Time.frameCount > 4)
            {
                _firstPageChosen = true;
                if (!_b.Ready) ShowPage(Page.Projects, 0);
            }

            if (_b.Version != _lastVersion)
            {
                _lastVersion = _b.Version;
                _updated = DateTime.Now;
                RefreshAll();
            }

            if (_b.Message != _lastMessage)
            {
                _lastMessage = _b.Message;
                if (_lastMessage != "")
                {
                    _toast.text = _lastMessage;
                    _toast.color = _b.MessageIsError ? Bad : Good;
                    _toastUntil = Time.unscaledTime + 6f;
                }

                if (_roomStatus != null) _roomStatus.text = _lastMessage;
            }

            // the right panel is rebuilt at most a few times a second (corners may be dragged at 60 fps)
            if (Time.unscaledTime - _rightBuiltAt > 0.12f)
            {
                _rightBuiltAt = Time.unscaledTime;
                BuildRight();
            }

            _toast.transform.parent.gameObject.SetActive(Time.unscaledTime < _toastUntil);
            if (_undoButton != null) _undoButton.interactable = _b.CanUndo;
        }

        bool _firstPageChosen;
        float _rightBuiltAt;

        void OnDestroy()
        {
            if (_b != null) _b.UseShell = false;
        }

        // The 3D view fills what is left between the bars and the panels.
        void UpdateViewport()
        {
            var cam = Camera.main;
            if (cam == null || _canvas == null) return;
            float k = _canvas.scaleFactor;
            float x0 = (RailW + LeftW) * k / Screen.width;
            float x1 = 1f - RightW * k / Screen.width;
            float y0 = BottomH * k / Screen.height;
            float y1 = 1f - (TopH + ToolbarH) * k / Screen.height;
            var r = new Rect(x0, y0, Mathf.Max(0.05f, x1 - x0), Mathf.Max(0.05f, y1 - y0));
            if (cam.rect != r) cam.rect = r;
        }

        // ---------------------------------------------------------------- building blocks

        void BuildCanvas()
        {
            var go = new GameObject("Editor UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        Transform Root => _canvas.transform;

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
            return r;
        }

        Image Fill(RectTransform r, Color color, bool rounded, bool raycast = true)
        {
            var image = r.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (rounded)
            {
                image.sprite = _round;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        RectTransform MakePanel(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var background = MakeRect(name + " Background", Root, anchorMin, anchorMax, offsetMin, offsetMax);
            Fill(background, Bg, false);
            var panel = MakeRect(name, background, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            Fill(panel, Panel, true);
            return panel;
        }

        RectTransform Stack(Transform parent, string name, float spacing, RectOffset padding)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            return go.GetComponent<RectTransform>();
        }

        RectTransform Row(Transform parent, float height, float spacing = 10f)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return go.GetComponent<RectTransform>();
        }

        TMP_Text Text(Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Left, float height = -1f)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            if (height > 0f)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = height;
                le.minHeight = height;
            }

            return t;
        }

        Button Btn(Transform parent, string text, float height, Color fill, Color textColor, Action onClick, float size = 24f, FontStyles style = FontStyles.Bold)
        {
            var go = new GameObject("Button " + text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = _round;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(fill, Color.black, 0.25f);
            colors.disabledColor = Color.Lerp(fill, Bg, 0.55f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var label = Text(go.transform, text, size, textColor, style, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            return button;
        }

        TMP_InputField Field(Transform parent, string placeholder, string value, float height, Action<string> onChange)
        {
            var go = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = _round;
            image.type = Image.Type.Sliced;
            image.color = PanelHi;
            go.GetComponent<LayoutElement>().preferredHeight = height;

            var area = MakeRect("Text Area", go.transform, Vector2.zero, Vector2.one, new Vector2(16f, 6f), new Vector2(-16f, -6f));
            area.gameObject.AddComponent<RectMask2D>();
            var hint = Text(area, placeholder, 24f, Muted, FontStyles.Italic);
            var text = Text(area, value, 26f, TextC);
            foreach (var t in new[] { hint, text })
            {
                Stretch(t.rectTransform);
                t.overflowMode = TextOverflowModes.Masking;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.alignment = TextAlignmentOptions.Left;
            }

            var field = go.GetComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.fontAsset = _font;
            field.pointSize = 26f;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = TextC;
            field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            field.text = value;
            if (onChange != null) field.onValueChanged.AddListener(v => onChange(v));
            return field;
        }

        void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
        }

        // ---------------------------------------------------------------- top bar, rail, toolbar, bottom bar

        void BuildTopBar()
        {
            var bar = MakeRect("Top Bar", Root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -TopH), Vector2.zero);
            Fill(bar, Bg, false);
            var line = MakeRect("Line", bar, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
            Fill(line, Line, false, false);

            if (_logo != null)
            {
                var logo = MakeRect("Logo", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, -30f), new Vector2(86f, 30f));
                var img = logo.gameObject.AddComponent<Image>();
                img.sprite = _logo;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            var title = Text(bar, "VRoom", 40f, TextC, FontStyles.Bold, TextAlignmentOptions.Left);
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            title.rectTransform.pivot = new Vector2(0f, 0.5f);
            title.rectTransform.anchoredPosition = new Vector2(100f, 0f);
            title.rectTransform.sizeDelta = new Vector2(200f, 60f);

            var tabs = MakeRect("Tabs", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(330f, -30f), new Vector2(330f + 4 * 196f, 30f));
            var layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                _tabButtons.Add(Btn(tabs, TabNames[i], 60f, PanelHi, Muted, () => OnTab(index), 25f, FontStyles.Normal));
            }

            // right side: undo and save, and the account circle
            var right = MakeRect("Right", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-640f, -30f), new Vector2(-24f, 30f));
            var rl = right.gameObject.AddComponent<HorizontalLayoutGroup>();
            rl.spacing = 14f;
            rl.childAlignment = TextAnchor.MiddleRight;
            rl.childControlWidth = false;
            rl.childControlHeight = false;
            rl.childForceExpandWidth = false;
            rl.childForceExpandHeight = false;
            SideButton(right, "Оновити", 150f, PanelHi, TextC, () => _b.OpenProjectAt(_b.CurrentProject));
            _undoButton = SideButton(right, "Назад", 130f, PanelHi, TextC, () => _b.UiUndo());
            SideButton(right, "Зберегти", 180f, Accent, Color.white, () => _b.SaveNow());
            var avatar = MakeRect("Avatar", right, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            avatar.sizeDelta = new Vector2(60f, 60f);
            Fill(avatar, Hex("3A3F55"), true, false);
            var letter = Text(avatar, "Я", 30f, TextC, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(letter.rectTransform);
        }

        Button SideButton(Transform parent, string text, float width, Color fill, Color textColor, Action click)
        {
            var b = Btn(parent, text, 60f, fill, textColor, click, 24f);
            var rt = b.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 60f);
            var le = b.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 60f;
            return b;
        }

        void BuildRail()
        {
            var rail = MakeRect("Rail", Root, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, BottomH), new Vector2(RailW, -TopH));
            Fill(rail, Bg, false);
            var column = Stack(rail, "Items", 10f, new RectOffset(12, 12, 16, 16));
            Stretch(column);
            for (int i = 0; i < RailPages.Length; i++)
            {
                int index = i;
                var b = Btn(column, "", 128f, Bg, TextC, () => ShowPage(RailPages[index], 1), 20f, FontStyles.Normal);
                var icon = MakeRect("Icon", b.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-22f, -64f), new Vector2(22f, -20f));
                var im = icon.gameObject.AddComponent<Image>();
                im.sprite = Icon(RailIcons[i]);
                im.raycastTarget = false;
                var label = b.GetComponentInChildren<TMP_Text>();
                label.text = RailNames[i];
                label.fontSize = 19f;
                label.alignment = TextAlignmentOptions.Bottom;
                label.rectTransform.offsetMin = new Vector2(0f, 8f);
                _railButtons.Add(b);
            }
        }

        void BuildToolbar()
        {
            var strip = MakeRect("Toolbar", Root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(RailW + LeftW, -(TopH + ToolbarH)), new Vector2(-RightW, -TopH));
            Fill(strip, Bg, false);
            var row = MakeRect("Row", strip, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            string[] tools = { "Вибір", "Переміщення", "Обертання", "Масштаб" };
            for (int i = 0; i < tools.Length; i++)
            {
                int index = i;
                var b = ToolbarButton(row, tools[i], 170f, i == 0 ? AccentDim : PanelHi, i == 0 ? Accent : Muted, () =>
                {
                    if (index > 0) { ShowToast("Цей інструмент стане доступним, коли з'являться меблі."); }
                });
                _toolbarButtons.Add(b);
            }

            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(row, false);
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

            ToolbarButton(row, "Вид", 130f, PanelHi, TextC, () => _b.SetViewMode((_b.ViewModeIndex + 1) % 3));
            var two = ToolbarButton(row, "2D", 90f, PanelHi, TextC, () => _b.SetPlan(true));
            var three = ToolbarButton(row, "3D", 90f, Accent, Color.white, () => _b.SetPlan(false));
            _twoD = two;
            _threeD = three;
        }

        Button _twoD, _threeD;

        Button ToolbarButton(Transform parent, string text, float width, Color fill, Color textColor, Action click)
        {
            var b = Btn(parent, text, 56f, fill, textColor, click, 23f);
            var le = b.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 56f;
            return b;
        }

        void BuildBottomBar()
        {
            var bar = MakeRect("Bottom Bar", Root, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, BottomH));
            Fill(bar, Bg, false);
            var line = MakeRect("Line", bar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero);
            Fill(line, Line, false, false);

            var card = MakeRect("Project Card", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, -38f), new Vector2(500f, 38f));
            Fill(card, Panel, true, false);
            if (_logo != null)
            {
                var thumb = MakeRect("Thumb", card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, -28f), new Vector2(68f, 28f));
                var im = thumb.gameObject.AddComponent<Image>();
                im.sprite = _logo;
                im.preserveAspect = true;
                im.raycastTarget = false;
            }

            _bottomName = Text(card, "", 28f, TextC, FontStyles.Bold);
            _bottomName.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _bottomName.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            _bottomName.rectTransform.offsetMin = new Vector2(84f, 2f);
            _bottomName.rectTransform.offsetMax = new Vector2(-12f, 36f);
            _bottomSub = Text(card, "", 20f, Muted);
            _bottomSub.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _bottomSub.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            _bottomSub.rectTransform.offsetMin = new Vector2(84f, -34f);
            _bottomSub.rectTransform.offsetMax = new Vector2(-12f, 0f);

            _bottomArea = StatText(bar, 560f, 220f);
            _bottomWalls = StatText(bar, 790f, 260f);
            _bottomUpdated = StatText(bar, 1080f, 480f);

            var vr = Btn(bar, "Переглянути у VR", 70f, Accent, Color.white, () => ShowToast("Перегляд у VR буде наступним етапом."), 26f);
            var rt = vr.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-28f, 0f);
            rt.sizeDelta = new Vector2(380f, 70f);
        }

        TMP_Text StatText(Transform parent, float x, float width)
        {
            var t = Text(parent, "", 24f, TextC, FontStyles.Normal, TextAlignmentOptions.Left);
            t.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            t.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            t.rectTransform.pivot = new Vector2(0f, 0.5f);
            t.rectTransform.anchoredPosition = new Vector2(x, 0f);
            t.rectTransform.sizeDelta = new Vector2(width, 60f);
            return t;
        }

        void BuildToast()
        {
            var box = MakeRect("Toast", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-460f, BottomH + 24f), new Vector2(460f, BottomH + 100f));
            Fill(box, new Color(0.05f, 0.06f, 0.10f, 0.92f), true, false);
            _toast = Text(box, "", 24f, Good, FontStyles.Normal, TextAlignmentOptions.Center);
            _toast.rectTransform.anchorMin = Vector2.zero;
            _toast.rectTransform.anchorMax = Vector2.one;
            _toast.rectTransform.offsetMin = new Vector2(20f, 8f);
            _toast.rectTransform.offsetMax = new Vector2(-20f, -8f);
            box.gameObject.SetActive(false);
        }

        void ShowToast(string text)
        {
            _toast.text = text;
            _toast.color = TextC;
            _toastUntil = Time.unscaledTime + 4f;
        }

        // ---------------------------------------------------------------- navigation

        void OnTab(int tab)
        {
            switch (tab)
            {
                case 0: ShowPage(Page.Projects, 0); break;
                case 1: ShowPage(_page == Page.Projects || _page == Page.View || _page == Page.Help ? Page.Room : _page, 1); break;
                case 2: ShowPage(Page.View, 2); break;
                default: ShowPage(Page.Help, 3); break;
            }
        }

        void ShowPage(Page page, int tab)
        {
            _page = page;
            _tab = tab;
            _confirmDelete = -1;
            RefreshChrome();
            BuildLeft();
        }

        void RefreshAll()
        {
            RefreshChrome();
            if (_page == Page.Projects || _page == Page.View) BuildLeft();
            else RefreshRoomStatus();
            _rightSignature = "";   // rebuilt by Update
            RefreshBottom();
        }

        void RefreshChrome()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool on = i == _tab;
                Tint(_tabButtons[i], on ? AccentDim : PanelHi, on ? Accent : Muted);
            }

            for (int i = 0; i < _railButtons.Count; i++)
            {
                bool on = _tab == 1 && RailPages[i] == _page;
                Tint(_railButtons[i], on ? AccentDim : Bg, on ? Accent : Muted);
                var icon = _railButtons[i].transform.Find("Icon");
                if (icon != null) icon.GetComponent<Image>().color = on ? Accent : Muted;
            }

            if (_twoD != null && _b != null)
            {
                Tint(_twoD, _b.Plan2D ? Accent : PanelHi, _b.Plan2D ? Color.white : TextC);
                Tint(_threeD, _b.Plan2D ? PanelHi : Accent, _b.Plan2D ? TextC : Color.white);
            }
        }

        static void Tint(Button b, Color fill, Color text)
        {
            var colors = b.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(fill, Color.black, 0.25f);
            colors.disabledColor = Color.Lerp(fill, Bg, 0.55f);
            b.colors = colors;
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = text;
        }

        // ---------------------------------------------------------------- the left panel pages

        void BuildLeft()
        {
            Clear(_leftBody);
            _roomStatus = null;
            _buildButton = null;
            switch (_page)
            {
                case Page.Projects: BuildProjects(); break;
                case Page.Room: BuildRoom(); break;
                case Page.Objects: BuildObjects(); break;
                case Page.View: BuildView(); break;
                case Page.Help: BuildHelp(); break;
                default: BuildSoon(_page == Page.Materials ? "Матеріали" : _page == Page.Styles ? "Стилі" : "Налаштування"); break;
            }
        }

        void Title(string text)
        {
            Text(_leftBody, text, 36f, TextC, FontStyles.Bold, TextAlignmentOptions.Left, 56f);
        }

        void BuildProjects()
        {
            Title("Мої проєкти");
            if (_b.ProjectCount == 0) Text(_leftBody, "Проєктів ще немає. Створіть перший нижче.", 22f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 60f);
            for (int i = 0; i < _b.ProjectCount; i++)
            {
                int index = i;
                var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                card.transform.SetParent(_leftBody, false);
                var im = card.GetComponent<Image>();
                im.sprite = _round;
                im.type = Image.Type.Sliced;
                im.color = i == _b.CurrentProject ? AccentDim : PanelHi;
                card.GetComponent<LayoutElement>().preferredHeight = 150f;
                var name = Text(card.transform, _b.ProjectNameAt(i), 26f, TextC, FontStyles.Bold);
                name.rectTransform.anchorMin = new Vector2(0f, 1f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.offsetMin = new Vector2(18f, -52f);
                name.rectTransform.offsetMax = new Vector2(-18f, -10f);
                var sub = Text(card.transform, _b.WallsIn(i) + " стін" + (_b.ClosedIn(i) ? " · замкнено" : ""), 20f, Muted);
                sub.rectTransform.anchorMin = new Vector2(0f, 1f);
                sub.rectTransform.anchorMax = new Vector2(1f, 1f);
                sub.rectTransform.offsetMin = new Vector2(18f, -86f);
                sub.rectTransform.offsetMax = new Vector2(-18f, -50f);
                var buttons = MakeRect("Buttons", card.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 12f), new Vector2(-14f, 58f));
                var hl = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.spacing = 10f;
                hl.childControlWidth = true;
                hl.childControlHeight = true;
                hl.childForceExpandWidth = true;
                hl.childForceExpandHeight = true;
                Btn(buttons, i == _b.CurrentProject ? "Відкрито" : "Відкрити", 46f, i == _b.CurrentProject ? Panel : Accent, Color.white, () => { _b.OpenProjectAt(index); ShowPage(Page.Room, 1); }, 20f);
                bool sure = _confirmDelete == i;
                Btn(buttons, sure ? "Точно?" : "Видалити", 46f, sure ? Bad : Panel, sure ? Bg : TextC, () =>
                {
                    if (_confirmDelete == index) { _confirmDelete = -1; _b.DeleteProjectAt(index); }
                    else { _confirmDelete = index; BuildLeft(); }
                }, 20f);
            }

            Spacer(_leftBody, 10f);
            Text(_leftBody, "Назва нового проєкту", 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 30f);
            Field(_leftBody, "Наприклад, Моя квартира", _newProjectName, 64f, v => _newProjectName = v);
            Btn(_leftBody, "Створити проєкт", 64f, Accent, Color.white, () =>
            {
                _b.CreateProjectNamed(_newProjectName);
                _newProjectName = "";
                ShowPage(Page.Room, 1);
            }, 26f);
        }

        void BuildRoom()
        {
            Title("Стіни квартири");
            _roomStatus = Text(_leftBody, _b.Ready ? RoomHeadline() : "Спочатку відкрийте проєкт: вкладка «Проєкт».", 22f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 56f);

            Text(_leftBody, "Ширина стіни, м", 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
            Field(_leftBody, "6", _wallWidth, 60f, v => _wallWidth = v);
            Text(_leftBody, "Висота стіни, м", 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
            Field(_leftBody, "2.7", _wallHeight, 60f, v => _wallHeight = v);
            Text(_leftBody, "Правий кут, °", 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
            Field(_leftBody, "90", _wallAngle, 60f, v => _wallAngle = v);

            _buildButton = Btn(_leftBody, "Побудувати стіну", 64f, Accent, Color.white, () => { if (_b.Ready) _b.UiAddWall(_wallWidth, _wallHeight, _wallAngle); }, 26f);
            _buildButton.interactable = _b.Ready && !_b.Closed;

            var r1 = Row(_leftBody, 56f);
            Btn(r1, "Замкнути контур", 56f, PanelHi, TextC, () => { if (_b.Ready) _b.UiCloseContour(); }, 20f);
            Btn(r1, "Видалити останню", 56f, PanelHi, TextC, () => { if (_b.Ready) _b.UiDeleteLast(); }, 20f);
            var r2 = Row(_leftBody, 56f);
            Btn(r2, "Стерти все", 56f, PanelHi, Bad, () => { if (_b.Ready) _b.UiEraseAll(); }, 20f);
            Btn(r2, "Внутрішні за планом", 56f, PanelHi, TextC, () => { if (_b.Ready) _b.UiLoadLayout(); }, 20f);
            Btn(_leftBody, _b.DrawingInner ? "Малювати внутрішню стіну: увімк." : "Малювати внутрішню стіну", 56f, _b.DrawingInner ? AccentDim : PanelHi, _b.DrawingInner ? Accent : TextC, () =>
            {
                if (!_b.Ready) return;
                _b.SetPlan(true);
                _b.UiToggleDrawInner();
                BuildLeft();
            }, 20f);
        }

        string RoomHeadline()
        {
            return _b.Closed ? "Контур замкнено · " + _b.WallCount + " стін" : "Стіна №" + (_b.WallCount + 1) + " · додайте наступну";
        }

        void RefreshRoomStatus()
        {
            if (_roomStatus != null && _b.Ready && _lastMessage == "") _roomStatus.text = RoomHeadline();
            if (_buildButton != null) _buildButton.interactable = _b.Ready && !_b.Closed;
        }

        void BuildObjects()
        {
            Title("Об'єкти");
            Field(_leftBody, "Пошук меблів...", "", 60f, null);
            var chips = Row(_leftBody, 50f, 8f);
            foreach (var c in new[] { "Усі", "Дивани", "Ліжка", "Столи" })
            {
                Btn(chips, c, 50f, c == "Усі" ? Accent : PanelHi, c == "Усі" ? Color.white : TextC, null, 19f, FontStyles.Normal);
            }

            var grid = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            grid.transform.SetParent(_leftBody, false);
            var g = grid.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(98f, 118f);
            g.spacing = new Vector2(10f, 10f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            grid.GetComponent<LayoutElement>().preferredHeight = 3 * 128f;
            foreach (var n in new[] { "Диван", "Ліжко", "Крісло", "Стіл", "Столик", "Тумба", "Шафа", "Стелаж", "Рослина" })
            {
                var card = Btn(grid.transform, n, 118f, PanelHi, TextC, () => ShowToast("Розстановка меблів буде наступним етапом."), 19f, FontStyles.Normal);
                card.GetComponent<LayoutElement>().preferredHeight = 118f;
            }

            Text(_leftBody, "Меблі з каталогу з'являться тут: перетягнете їх у квартиру.", 19f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 60f);
        }

        void BuildView()
        {
            Title("Вигляд");
            string[] names = { "Огляд", "Режим бога", "Ходьба всередині" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                bool on = !_b.Plan2D && _b.ViewModeIndex == i;
                Btn(_leftBody, names[i], 64f, on ? AccentDim : PanelHi, on ? Accent : TextC, () => { _b.SetViewMode(index); }, 24f);
            }

            Btn(_leftBody, "2D план зверху", 64f, _b.Plan2D ? AccentDim : PanelHi, _b.Plan2D ? Accent : TextC, () => _b.SetPlan(!_b.Plan2D), 24f);
            Spacer(_leftBody, 12f);
            Text(_leftBody, "Режими перегляду (скоро)", 22f, Muted, FontStyles.Bold, TextAlignmentOptions.Left, 34f);
            foreach (var n in new[] { "Простий", "Технічна інфраструктура: електрика, сантехніка, ОВК, газ, слабкострумові мережі", "Порожній" })
            {
                var b = Btn(_leftBody, n, n.Length > 20 ? 110f : 56f, Panel, Muted, null, 19f, FontStyles.Normal);
                b.interactable = false;
            }
        }

        void BuildHelp()
        {
            Title("Довідка");
            Text(_leftBody,
                "Стіни: введіть ширину, висоту й кут і натисніть «Побудувати стіну». Кут 270° — внутрішній кут назовні.\n\n" +
                "2D план: тягніть кути, клацніть стіну, щоб змінити її розміри. «Малювати внутрішню стіну» — затисніть і ведіть мишею.\n\n" +
                "Режим бога: тягніть мишею, коліщатко — наближення. Ходьба: W A S D, Shift — біг.\n\n" +
                "Скасувати: кнопка «Назад» угорі або Ctrl+Z, повернути — Ctrl+Y.",
                21f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft, 560f);
        }

        void BuildSoon(string title)
        {
            Title(title);
            Text(_leftBody, "Цей розділ з'явиться на наступному етапі.", 22f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 60f);
        }

        // ---------------------------------------------------------------- the right panel and the bottom bar

        void BuildRight()
        {
            int kind = _b.SelectedKind;
            string signature = kind + ":" + _b.SelectedNumber + ":" + _b.Version;
            if (signature == _rightSignature) return;
            _rightSignature = signature;
            Clear(_rightBody);

            Text(_rightBody, "Властивості", 34f, TextC, FontStyles.Bold, TextAlignmentOptions.Left, 54f);
            if (kind == 0)
            {
                Text(_rightBody, "Нічого не вибрано.", 24f, TextC, FontStyles.Normal, TextAlignmentOptions.Left, 40f);
                Text(_rightBody, "Клацніть стіну на 2D плані, щоб побачити й змінити її розміри.", 21f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 80f);
                Spacer(_rightBody, 8f);
                Text(_rightBody, "Квартира", 24f, TextC, FontStyles.Bold, TextAlignmentOptions.Left, 36f);
                Text(_rightBody,
                    "Зовнішніх стін: " + _b.WallCount + "\nВнутрішніх стін: " + _b.InnerCount + "\nПлоща: " + (_b.Area > 0f ? _b.Area.ToString("0.0", CultureInfo.InvariantCulture) + " м²" : "контур не замкнено"),
                    22f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft, 110f);
                return;
            }

            bool inner = kind == 2;
            Text(_rightBody, (inner ? "Внутрішня стіна №" : "Зовнішня стіна №") + _b.SelectedNumber, 26f, Accent, FontStyles.Bold, TextAlignmentOptions.Left, 40f);
            _b.SelectedFields(out var a, out var h, out var c);
            string[] labels = inner ? new[] { "Довжина, м", "Висота, м", "Товщина, м" } : new[] { "Ширина, м", "Висота, м", "Кут у кінці стіни, °" };
            string va = a, vb = h, vc = c;
            Text(_rightBody, labels[0], 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
            Field(_rightBody, "", va, 60f, v => va = v);
            Text(_rightBody, labels[1], 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
            Field(_rightBody, "", vb, 60f, v => vb = v);
            Text(_rightBody, labels[2], 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
            Field(_rightBody, "", vc, 60f, v => vc = v);
            Spacer(_rightBody, 6f);
            Btn(_rightBody, "Застосувати", 62f, Accent, Color.white, () => _b.ApplySelectedFields(va, vb, vc), 24f);
            Btn(_rightBody, "Видалити стіну", 56f, Panel, Bad, () => _b.DeleteSelectedAny(), 22f);
        }

        void RefreshBottom()
        {
            _bottomName.text = _b.Ready ? _b.ProjectName : "Немає проєкту";
            _bottomSub.text = _b.Ready ? (_b.Closed ? "Контур замкнено" : "Контур ще не замкнено") : "Відкрийте або створіть проєкт";
            _bottomArea.text = _b.Area > 0f ? _b.Area.ToString("0.0", CultureInfo.InvariantCulture) + " м²" : "— м²";
            _bottomWalls.text = _b.WallCount + " стін · " + _b.InnerCount + " внутр.";
            _bottomUpdated.text = "Оновлено: " + _updated.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- generated sprites

        static Sprite MakeRounded(int radius)
        {
            int size = radius * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        // Simple line icons drawn from segments and rings (white; the Image tints them).
        Sprite Icon(string name)
        {
            if (_icons.TryGetValue(name, out var cached)) return cached;
            const int n = 96;
            var segs = new List<Vector2[]>();
            var rings = new List<Vector3>();   // x, y = centre, z = radius
            var c = new Vector2(48f, 48f);
            switch (name)
            {
                case "cube":
                    var hex = new Vector2[6];
                    for (int i = 0; i < 6; i++) { float a = Mathf.Deg2Rad * (30f + 60f * i); hex[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 34f; }
                    for (int i = 0; i < 6; i++) segs.Add(new[] { hex[i], hex[(i + 1) % 6] });
                    segs.Add(new[] { c, hex[1] });
                    segs.Add(new[] { c, hex[3] });
                    segs.Add(new[] { c, hex[5] });
                    break;
                case "room":
                    segs.Add(new[] { new Vector2(20f, 20f), new Vector2(20f, 76f) });
                    segs.Add(new[] { new Vector2(20f, 76f), new Vector2(76f, 76f) });
                    segs.Add(new[] { new Vector2(76f, 76f), new Vector2(76f, 20f) });
                    segs.Add(new[] { new Vector2(76f, 20f), new Vector2(58f, 20f) });
                    segs.Add(new[] { new Vector2(38f, 20f), new Vector2(20f, 20f) });
                    break;
                case "material":
                    rings.Add(new Vector3(48f, 48f, 32f));
                    rings.Add(new Vector3(48f, 48f, 15f));
                    break;
                case "style":
                    segs.Add(new[] { new Vector2(48f, 14f), new Vector2(82f, 48f) });
                    segs.Add(new[] { new Vector2(82f, 48f), new Vector2(48f, 82f) });
                    segs.Add(new[] { new Vector2(48f, 82f), new Vector2(14f, 48f) });
                    segs.Add(new[] { new Vector2(14f, 48f), new Vector2(48f, 14f) });
                    break;
                default:
                    rings.Add(new Vector3(48f, 48f, 18f));
                    for (int i = 0; i < 8; i++)
                    {
                        float a = Mathf.Deg2Rad * 45f * i;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        segs.Add(new[] { c + d * 27f, c + d * 38f });
                    }

                    break;
            }

            var px = new Color32[n * n];
            const float half = 3.2f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = float.MaxValue;
                    foreach (var s in segs)
                    {
                        var ab = s[1] - s[0];
                        float t = Mathf.Clamp01(Vector2.Dot(p - s[0], ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, Vector2.Distance(p, s[0] + ab * t));
                    }

                    foreach (var r in rings) d = Mathf.Min(d, Mathf.Abs(Vector2.Distance(p, new Vector2(r.x, r.y)) - r.z));
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(half - d + 0.5f));
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _icons[name] = sprite;
            return sprite;
        }
    }
}
