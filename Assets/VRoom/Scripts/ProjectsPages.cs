using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>
    /// The spread that opens after sign-in: on the left page the player's projects to choose from, on the right page a
    /// field to name and create a new one. Its two world-space canvases are built here, on the pages of the book.
    /// </summary>
    public class ProjectsPages : MonoBehaviour
    {
        private const int MaxListed = 6;

        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float pageCenterX = 0.20f;
        [SerializeField] private float canvasHeight = 0.0405f;

        private static readonly Color Ink = new Color(0.17f, 0.10f, 0.06f);
        private static readonly Color Cream = new Color(0.98f, 0.94f, 0.84f);
        private static readonly Color Leather = new Color(0.45f, 0.07f, 0.10f);

        private RectTransform _leftCanvas;
        private RectTransform _rightCanvas;
        private RectTransform _listRoot;
        private TMP_Text _emptyLabel;
        private TMP_Text _status;
        private TMP_InputField _nameInput;
        private Button _createButton;
        private ProjectStore _store;
        private bool _busy;

        /// <summary>Raised when the player chooses one of their projects (or has just created one).</summary>
        public event Action<ProjectInfo> ProjectChosen;

        private void Awake()
        {
            _leftCanvas = MakeCanvas("Projects Page UI", -pageCenterX);
            _rightCanvas = MakeCanvas("New Project Page UI", pageCenterX);
            BuildLeft();
            BuildRight();
            Hide();
        }

        public void Hide()
        {
            _leftCanvas.gameObject.SetActive(false);
            _rightCanvas.gameObject.SetActive(false);
        }

        public void Show(ProjectStore store)
        {
            _store = store;
            _leftCanvas.gameObject.SetActive(true);
            _rightCanvas.gameObject.SetActive(true);
            Refresh();
        }

        private RectTransform MakeCanvas(string name, float x)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, canvasHeight, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.001f;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = viewCamera != null ? viewCamera : Camera.main;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300f, 340f);
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            return rect;
        }

        private TMP_Text Label(string name, Transform parent, string text, float size, FontStyles style, Color color, Vector2 pos, Vector2 dim, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = dim;
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private Button MakeButton(string name, Transform parent, string text, float size, Color fill, Color textColor, Vector2 pos, Vector2 dim)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = dim;
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(fill, Color.black, 0.3f);
            colors.disabledColor = new Color(0.45f, 0.35f, 0.33f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            Label("Label", go.transform, text, size, FontStyles.Bold, textColor, Vector2.zero, dim - new Vector2(16f, 6f));
            return button;
        }

        private void BuildLeft()
        {
            Label("Title", _leftCanvas, "Мої проєкти", 34f, FontStyles.Bold, Ink, new Vector2(0f, 135f), new Vector2(290f, 56f));
            var root = new GameObject("List", typeof(RectTransform));
            root.transform.SetParent(_leftCanvas, false);
            _listRoot = root.GetComponent<RectTransform>();
            _listRoot.anchoredPosition = Vector2.zero;
            _listRoot.sizeDelta = new Vector2(280f, 260f);
            _emptyLabel = Label("Empty", _leftCanvas, "Тут з'являться твої проєкти.\nСтвори перший на сторінці праворуч.", 20f, FontStyles.Italic, Ink, new Vector2(0f, 20f), new Vector2(270f, 90f));
        }

        private void BuildRight()
        {
            Label("Title", _rightCanvas, "Новий проєкт", 34f, FontStyles.Bold, Ink, new Vector2(0f, 135f), new Vector2(290f, 56f));

            var go = new GameObject("Name Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(_rightCanvas, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0f, 62f);
            rect.sizeDelta = new Vector2(260f, 52f);
            var image = go.GetComponent<Image>();
            image.color = new Color(1f, 0.97f, 0.9f, 0.9f);

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var areaRect = area.GetComponent<RectTransform>();
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(12f, 4f);
            areaRect.offsetMax = new Vector2(-12f, -4f);

            var placeholder = Label("Placeholder", area.transform, "Назва проєкту", 22f, FontStyles.Italic, new Color(0.17f, 0.10f, 0.06f, 0.5f), Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
            var text = Label("Text", area.transform, "", 24f, FontStyles.Normal, Ink, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
            foreach (var t in new[] { placeholder, text })
            {
                var r = t.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
                t.overflowMode = TextOverflowModes.Masking;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            }

            _nameInput = go.GetComponent<TMP_InputField>();
            _nameInput.textViewport = areaRect;
            _nameInput.textComponent = text;
            _nameInput.placeholder = placeholder;
            _nameInput.fontAsset = font;
            _nameInput.pointSize = 24f;
            _nameInput.characterLimit = 40;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.caretColor = Ink;
            _nameInput.selectionColor = new Color(0.45f, 0.07f, 0.10f, 0.35f);
            _nameInput.onSubmit.AddListener(_ => Create());

            _createButton = MakeButton("Create Button", _rightCanvas, "Створити", 26f, Leather, Cream, new Vector2(0f, -8f), new Vector2(200f, 56f));
            _createButton.onClick.AddListener(Create);
            _status = Label("Status", _rightCanvas, "", 19f, FontStyles.Italic, Ink, new Vector2(0f, -88f), new Vector2(270f, 100f));
        }

        private void Refresh()
        {
            foreach (Transform child in _listRoot)
            {
                Destroy(child.gameObject);
            }

            var items = _store?.Items ?? new List<ProjectInfo>();
            _emptyLabel.gameObject.SetActive(items.Count == 0);
            var shown = Mathf.Min(items.Count, MaxListed);
            for (var i = 0; i < shown; i++)
            {
                var project = items[items.Count - 1 - i];   // newest first
                var button = MakeButton("Project " + project.name, _listRoot, project.name, 24f, new Color(0.30f, 0.20f, 0.12f), Cream, new Vector2(0f, 85f - i * 48f), new Vector2(270f, 42f));
                button.onClick.AddListener(() => Choose(project));
            }

            if (items.Count > shown)
            {
                Label("More", _listRoot, $"і ще {items.Count - shown}…", 18f, FontStyles.Italic, Ink, new Vector2(0f, 85f - shown * 48f), new Vector2(270f, 30f));
            }
        }

        private void Choose(ProjectInfo project)
        {
            _status.text = $"Відкрито проєкт «{project.name}».";
            ProjectChosen?.Invoke(project);
        }

        private async void Create()
        {
            if (_busy || _store == null)
            {
                return;
            }

            _busy = true;
            _createButton.interactable = false;
            _status.text = "Створюємо…";
            try
            {
                var project = await _store.CreateAsync(_nameInput.text);
                _nameInput.text = "";
                Refresh();
                Choose(project);
                _status.text = $"Проєкт «{project.name}» створено.";
            }
            catch (ArgumentException exception)
            {
                _status.text = exception.Message;
            }
            catch (Exception exception)
            {
                var denied = exception.Message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0;
                _status.text = denied
                    ? "Немає доступу до бази даних. Потрібно опублікувати правила Firestore (firestore.rules)."
                    : "Не вдалося створити: " + exception.Message;
            }
            finally
            {
                _busy = false;
                _createButton.interactable = true;
            }
        }
    }
}
