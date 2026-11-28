using System;
using System.Collections.Generic;
using ITKamianets.Engine.Firebase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>
    /// The first thing the player sees: a bright, light room with a modern table and an iPad standing on it. The iPad
    /// says "Вхід в Google"; the button signs in with Google (<see cref="FirebaseBootstrap"/>), then the iPad lists the
    /// player's projects. Choosing or creating one switches the room off and opens the apartment
    /// (<see cref="ApartmentWallBuilder"/>), with the saved walls already built. The room, table, iPad and its screen
    /// are all built here at runtime, so the scene only needs this component, the Firebase object and an EventSystem.
    /// </summary>
    public class TabletLoginRoom : MonoBehaviour
    {
        private const int MaxListed = 4;
        private const float TableTop = 0.76f;

        [SerializeField] private FirebaseBootstrap bootstrap;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Camera viewCamera;
        [Tooltip("Seconds to wait for Firebase before the sign-in page is shown anyway.")]
        [SerializeField, Min(1f)] private float loadingTimeout = 8f;

        private enum Page { Loading, Login, Projects }

        private static readonly Color Ink = new Color(0.12f, 0.10f, 0.22f);
        private static readonly Color Violet = new Color(0.36f, 0.22f, 0.78f);
        private static readonly Color Soft = new Color(0.92f, 0.90f, 1f);

        private Page _page = Page.Loading;
        private GameObject _room;
        private GameObject _model;
        private GameObject _loadingPage, _loginPage, _projectsPage;
        private Button _googleButton, _createButton;
        private TMP_Text _googleLabel, _loginStatus, _projectsStatus, _emptyLabel;
        private TMP_InputField _nameInput;
        private RectTransform _listRoot;
        private ProjectStore _store;
        private Sprite _round;
        private readonly List<Light> _sceneLights = new List<Light>();
        private float _startTime;
        private bool _entered, _loadingProjects, _creating;

        // ---------------------------------------------------------------- start and flow

        private void Start()
        {
            _startTime = Time.time;
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            // the scene's own sun is only for the apartment; the room has its own light
            foreach (var light in FindObjectsByType<Light>())
            {
                _sceneLights.Add(light);
                light.enabled = false;
            }

            _round = MakeRoundedSprite();
            BuildRoom();
            BuildTablet();
            SetPage(Page.Loading);
        }

        private void Update()
        {
            if (_entered)
            {
                return;
            }

            if (bootstrap != null && bootstrap.IsSignedIn)
            {
                if (_page != Page.Projects)
                {
                    ShowProjects();
                }

                return;
            }

            var wantLogin = bootstrap == null || bootstrap.LoginRequired || bootstrap.Busy || Time.time - _startTime > loadingTimeout;
            if (wantLogin && _page != Page.Login)
            {
                SetPage(Page.Login);
            }

            if (_page == Page.Login)
            {
                var busy = bootstrap != null && bootstrap.Busy;
                _googleButton.interactable = !busy && bootstrap != null;
                _googleLabel.text = busy ? "Чекаємо браузер…" : "Вхід в Google";
                _loginStatus.text = bootstrap == null ? "Немає об'єкта Firebase у сцені." : bootstrap.Status;
            }
        }

        private void SetPage(Page page)
        {
            _page = page;
            _loadingPage.SetActive(page == Page.Loading);
            _loginPage.SetActive(page == Page.Login);
            _projectsPage.SetActive(page == Page.Projects);
        }

        private void ShowProjects()
        {
            SetPage(Page.Projects);
            _store = new ProjectStore(EngineFirebase.Current);
            Refresh();
            LoadProjects();
        }

        private async void LoadProjects()
        {
            if (_loadingProjects)
            {
                return;
            }

            _loadingProjects = true;
            _projectsStatus.text = "";
            try
            {
                await _store.LoadAsync();
                Refresh();
            }
            catch (Exception exception)
            {
                _projectsStatus.text = "Не вдалося завантажити проєкти: " + exception.Message;
            }
            finally
            {
                _loadingProjects = false;
            }
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
                var button = MakeButton("Project " + project.name, _listRoot, project.name, 40f, Soft, Ink, new Vector2(0f, -i * 92f), new Vector2(780f, 78f));
                button.onClick.AddListener(() => EnterApartment());
            }
        }

        private async void Create()
        {
            if (_creating || _store == null)
            {
                return;
            }

            _creating = true;
            _createButton.interactable = false;
            _projectsStatus.text = "Створюємо…";
            try
            {
                await _store.CreateAsync(_nameInput.text);
                _nameInput.text = "";
                EnterApartment();
            }
            catch (ArgumentException exception)
            {
                _projectsStatus.text = exception.Message;
            }
            catch (Exception exception)
            {
                var denied = exception.Message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0;
                _projectsStatus.text = denied
                    ? "Немає доступу до бази даних. Можна продовжити без проєкту."
                    : "Не вдалося створити: " + exception.Message;
            }
            finally
            {
                _creating = false;
                _createButton.interactable = true;
            }
        }

        // Switches the room off and shows the apartment: the saved walls are built by ApartmentWallBuilder on start.
        private void EnterApartment()
        {
            if (_entered)
            {
                return;
            }

            _entered = true;
            _room.SetActive(false);
            foreach (var light in _sceneLights)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }

            EmptyWorld.UseDefaultLighting();
            if (viewCamera != null)
            {
                viewCamera.fieldOfView = 60f;
                viewCamera.transform.SetPositionAndRotation(new Vector3(4.5f, 11f, -4f), Quaternion.Euler(55f, 0f, 0f));
            }

            var builder = new GameObject("ApartmentWallBuilder").AddComponent<ApartmentWallBuilder>();
            builder.UseShell = true;   // the editor screen below replaces the old window
            builder.gameObject.AddComponent<EditorShell>().Init(builder, font);
        }

        // ---------------------------------------------------------------- the room

        private static Material Mat(Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            return material;
        }

        private static Material Glow(Color color)
        {
            return new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject Sphere(string name, Transform parent, Vector3 position, float diameter, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // A thin bar between two points (the iPad's back stand).
        private static void Bar(string name, Transform parent, Vector3 from, Vector3 to, float width, float thickness, Material material)
        {
            var bar = Box(name, parent, (from + to) * 0.5f, new Vector3(width, thickness, Vector3.Distance(from, to)), material);
            bar.transform.rotation = Quaternion.LookRotation(to - from, Vector3.up);
        }

        private void BuildRoom()
        {
            _room = new GameObject("Light Room");
            _room.transform.SetParent(transform, false);
            var r = _room.transform;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.68f, 0.65f, 0.60f);

            // The designed room (Blender: Blender/login_room.blend, exported to Resources/LoginRoom.glb) is used when
            // it is there; the simple room below is only the fallback.
            var prefab = Resources.Load<GameObject>("LoginRoom");
            if (prefab != null)
            {
                _model = Instantiate(prefab, r);
                _model.name = "Room Model";
                BuildLights(r);
                SetupCamera();
                return;
            }

            var wall = Mat(new Color(0.96f, 0.95f, 0.93f), 0.05f);
            var wood = Mat(new Color(0.86f, 0.74f, 0.58f), 0.35f);
            var white = Mat(new Color(0.98f, 0.98f, 0.98f), 0.1f);
            var black = Mat(new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.6f);

            // shell: floor, ceiling and four walls of a 6 x 5.2 m room, 3 m high
            Box("Floor", r, new Vector3(0f, -0.025f, 0.6f), new Vector3(6.1f, 0.05f, 5.3f), wood);
            Box("Ceiling", r, new Vector3(0f, 3.025f, 0.6f), new Vector3(6.1f, 0.05f, 5.3f), white);
            Box("Wall Back", r, new Vector3(0f, 1.5f, 3.225f), new Vector3(6.1f, 3f, 0.05f), wall);
            Box("Wall Front", r, new Vector3(0f, 1.5f, -2.025f), new Vector3(6.1f, 3f, 0.05f), wall);
            Box("Wall Left", r, new Vector3(-3.025f, 1.5f, 0.6f), new Vector3(0.05f, 3f, 5.3f), wall);
            Box("Wall Right", r, new Vector3(3.025f, 1.5f, 0.6f), new Vector3(0.05f, 3f, 5.3f), wall);

            // a big window on the left wall: the sky glows, thin black frame and bars
            var sky = Glow(new Color(0.88f, 0.95f, 1f));
            Box("Window Glass", r, new Vector3(-2.99f, 1.65f, 0.9f), new Vector3(0.02f, 1.7f, 2.4f), sky);
            Box("Window Top", r, new Vector3(-2.98f, 2.5f, 0.9f), new Vector3(0.04f, 0.05f, 2.5f), black);
            Box("Window Bottom", r, new Vector3(-2.98f, 0.8f, 0.9f), new Vector3(0.04f, 0.05f, 2.5f), black);
            Box("Window Near", r, new Vector3(-2.98f, 1.65f, -0.3f), new Vector3(0.04f, 1.75f, 0.05f), black);
            Box("Window Far", r, new Vector3(-2.98f, 1.65f, 2.1f), new Vector3(0.04f, 1.75f, 0.05f), black);
            Box("Window Middle", r, new Vector3(-2.98f, 1.65f, 0.9f), new Vector3(0.04f, 1.75f, 0.04f), black);

            // two pictures on the back wall
            Box("Frame A", r, new Vector3(-0.9f, 1.55f, 3.19f), new Vector3(0.82f, 1.02f, 0.03f), black);
            Box("Picture A", r, new Vector3(-0.9f, 1.55f, 3.17f), new Vector3(0.72f, 0.92f, 0.01f), Mat(new Color(0.90f, 0.66f, 0.55f), 0.1f));
            Box("Frame B", r, new Vector3(0.9f, 1.55f, 3.19f), new Vector3(0.82f, 1.02f, 0.03f), black);
            Box("Picture B", r, new Vector3(0.9f, 1.55f, 3.17f), new Vector3(0.72f, 0.92f, 0.01f), Mat(new Color(0.55f, 0.70f, 0.82f), 0.1f));

            // a light rug under the table
            Box("Rug", r, new Vector3(0f, 0.006f, 0.25f), new Vector3(2.8f, 0.012f, 2f), Mat(new Color(0.82f, 0.84f, 0.88f), 0f));

            // the modern table: a thin white top on slim black metal legs
            var top = Mat(new Color(0.97f, 0.97f, 0.97f), 0.7f);
            Box("Table Top", r, new Vector3(0f, TableTop - 0.02f, 0.15f), new Vector3(1.6f, 0.04f, 0.85f), top);
            foreach (var sx in new[] { -1f, 1f })
            {
                foreach (var sz in new[] { -1f, 1f })
                {
                    Box("Table Leg", r, new Vector3(sx * 0.72f, (TableTop - 0.04f) * 0.5f, 0.15f + sz * 0.37f), new Vector3(0.04f, TableTop - 0.04f, 0.04f), black);
                }
            }

            Box("Table Rail", r, new Vector3(0f, TableTop - 0.1f, 0.15f - 0.37f), new Vector3(1.4f, 0.03f, 0.03f), black);
            Box("Table Rail", r, new Vector3(0f, TableTop - 0.1f, 0.15f + 0.37f), new Vector3(1.4f, 0.03f, 0.03f), black);

            // a plant in the corner
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Plant Pot";
            Destroy(pot.GetComponent<Collider>());
            pot.transform.SetParent(r, false);
            pot.transform.localPosition = new Vector3(2.3f, 0.2f, 2.5f);
            pot.transform.localScale = new Vector3(0.38f, 0.2f, 0.38f);
            pot.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.93f, 0.93f, 0.9f), 0.4f);
            var leaf = Mat(new Color(0.35f, 0.62f, 0.38f), 0.3f);
            Sphere("Plant A", r, new Vector3(2.3f, 0.75f, 2.5f), 0.7f, leaf);
            Sphere("Plant B", r, new Vector3(2.15f, 1.05f, 2.55f), 0.5f, leaf);
            Sphere("Plant C", r, new Vector3(2.45f, 1.0f, 2.4f), 0.45f, leaf);

            BuildLights(r);
            SetupCamera();
        }

        // light: the window's daylight and a soft lamp over the table
        private void BuildLights(Transform r)
        {
            var sun = new GameObject("Daylight", typeof(Light));
            sun.transform.SetParent(r, false);
            sun.transform.rotation = Quaternion.Euler(38f, 78f, 0f);
            var sunLight = sun.GetComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(1f, 0.93f, 0.82f);
            sunLight.intensity = 1.15f;
            sunLight.shadows = LightShadows.Soft;

            var lamp = new GameObject("Table Lamp Light", typeof(Light));
            lamp.transform.SetParent(r, false);
            lamp.transform.localPosition = new Vector3(0f, 2.1f, 0.1f);
            var lampLight = lamp.GetComponent<Light>();
            lampLight.type = LightType.Point;
            lampLight.color = new Color(1f, 0.92f, 0.80f);
            lampLight.intensity = 1.2f;
            lampLight.range = 5f;
            lampLight.shadows = LightShadows.None;
        }

        private void SetupCamera()
        {
            if (viewCamera != null)
            {
                viewCamera.fieldOfView = 40f;
                viewCamera.transform.position = new Vector3(0f, 1.2f, -1.0f);
                viewCamera.transform.LookAt(new Vector3(0f, 0.88f, 0.3f));
            }
        }

        // ---------------------------------------------------------------- the iPad

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void BuildTablet()
        {
            if (_model != null)
            {
                // the model carries an empty on the iPad's glass; the screen goes right there, facing the viewer
                var anchor = FindDeep(_model.transform, "ScreenAnchor");
                if (anchor != null)
                {
                    BuildScreen(anchor, 0.284f, 0.199f, 0f);

                    // Which way the glass faces does not depend on how the model was converted: it is from the iPad's
                    // middle to the anchor on its glass. The screen faces that way, and the camera stands on that side.
                    var outward = anchor.position - anchor.parent.position;
                    if (outward.sqrMagnitude < 1e-10f)
                    {
                        outward = -anchor.forward;
                    }

                    outward.Normalize();
                    anchor.Find("Screen").rotation = Quaternion.LookRotation(-outward, Vector3.up);

                    if (viewCamera != null)
                    {
                        var flat = new Vector3(outward.x, 0f, outward.z).normalized;
                        var target = anchor.position;
                        viewCamera.fieldOfView = 36f;
                        viewCamera.transform.position = target + flat * 0.95f + Vector3.up * 0.30f;
                        viewCamera.transform.LookAt(target + Vector3.up * 0.02f);
                    }

                    return;
                }

                Debug.LogWarning("[VRoom] LoginRoom.glb has no ScreenAnchor; the iPad screen is not placed.", this);
            }

            const float tilt = 20f;
            const float width = 0.30f, height = 0.215f, depth = 0.008f;
            var tablet = new GameObject("iPad").transform;
            tablet.SetParent(_room.transform, false);
            tablet.localPosition = new Vector3(0f, TableTop + height * 0.5f * Mathf.Cos(tilt * Mathf.Deg2Rad) + 0.004f, 0.08f);
            tablet.localRotation = Quaternion.Euler(tilt, 0f, 0f);

            var aluminium = Mat(new Color(0.78f, 0.79f, 0.82f), 0.6f, 0.85f);
            Box("Body", tablet, Vector3.zero, new Vector3(width, height, depth), aluminium);
            Box("Glass", tablet, new Vector3(0f, 0f, -depth * 0.5f - 0.0004f), new Vector3(width - 0.012f, height - 0.012f, 0.0008f), Mat(new Color(0.02f, 0.02f, 0.03f), 0.9f));

            // the stand behind: a slim bar from the iPad's back to the table
            var from = tablet.TransformPoint(new Vector3(0f, -0.02f, depth * 0.5f));
            var to = new Vector3(from.x, TableTop + 0.003f, from.z + 0.12f);
            Bar("Stand", _room.transform, from, to, 0.1f, 0.01f, aluminium);

            BuildScreen(tablet, width - 0.016f, height - 0.016f, -depth * 0.5f - 0.0012f);
        }

        private void BuildScreen(Transform tablet, float screenWidth, float screenHeight, float z)
        {
            var go = new GameObject("Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(tablet, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localRotation = Quaternion.identity;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000f, 1000f * screenHeight / screenWidth);
            go.transform.localScale = Vector3.one * (screenWidth / 1000f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = viewCamera;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(go.transform, false);
            Stretch(background.GetComponent<RectTransform>());
            background.GetComponent<Image>().color = new Color(0.97f, 0.96f, 1f);
            background.GetComponent<Image>().raycastTarget = false;

            _loadingPage = Page_("Loading", rect);
            Label("Loading Text", _loadingPage.transform, "VRoom", 120f, FontStyles.Bold, Violet, new Vector2(0f, 30f), new Vector2(800f, 150f));
            Label("Loading Sub", _loadingPage.transform, "Завантаження…", 44f, FontStyles.Normal, Ink, new Vector2(0f, -80f), new Vector2(800f, 70f));

            _loginPage = Page_("Login", rect);
            BuildLogin(_loginPage.transform);

            _projectsPage = Page_("Projects", rect);
            BuildProjects(_projectsPage.transform);
        }

        private static GameObject Page_(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void BuildLogin(Transform page)
        {
            Label("Title", page, "VRoom", 130f, FontStyles.Bold, Violet, new Vector2(0f, 190f), new Vector2(800f, 160f));
            Label("Subtitle", page, "Інтерактивний 3D-конструктор інтер'єру", 40f, FontStyles.Normal, Ink, new Vector2(0f, 90f), new Vector2(900f, 60f));

            // the Google button: white pill with the coloured G and the text
            _googleButton = MakeButton("Google Button", page, "", 48f, Color.white, Ink, new Vector2(0f, -40f), new Vector2(640f, 130f));
            var outline = _googleButton.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.78f, 0.78f, 0.84f);
            outline.effectDistance = new Vector2(3f, -3f);
            _googleLabel = _googleButton.GetComponentInChildren<TMP_Text>();
            _googleLabel.text = "Вхід в Google";
            _googleLabel.rectTransform.anchoredPosition = new Vector2(50f, 0f);
            Label("Google G", _googleButton.transform, "<color=#4285F4>G</color>", 80f, FontStyles.Bold, Color.white, new Vector2(-230f, 0f), new Vector2(110f, 110f));
            _googleButton.onClick.AddListener(() =>
            {
                if (bootstrap != null)
                {
                    bootstrap.SignInWithGoogle();
                }
            });

            _loginStatus = Label("Status", page, "", 30f, FontStyles.Bold, new Color(0.75f, 0.2f, 0.2f), new Vector2(0f, -190f), new Vector2(880f, 110f));
        }

        private void BuildProjects(Transform page)
        {
            Label("Title", page, "Мої проєкти", 66f, FontStyles.Bold, Violet, new Vector2(0f, 290f), new Vector2(800f, 90f));

            var list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(page, false);
            _listRoot = list.GetComponent<RectTransform>();
            _listRoot.anchoredPosition = new Vector2(0f, 190f);
            _listRoot.sizeDelta = new Vector2(780f, 380f);

            _emptyLabel = Label("Empty", page, "Проєктів ще немає. Створи перший нижче.", 38f, FontStyles.Normal, Ink, new Vector2(0f, 110f), new Vector2(860f, 120f));

            var go = new GameObject("Name Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(page, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-150f, -170f);
            rect.sizeDelta = new Vector2(540f, 84f);
            var image = go.GetComponent<Image>();
            image.sprite = _round;
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var areaRect = area.GetComponent<RectTransform>();
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(24f, 6f);
            areaRect.offsetMax = new Vector2(-24f, -6f);

            var placeholder = Label("Placeholder", area.transform, "Назва проєкту", 38f, FontStyles.Italic, new Color(0.12f, 0.1f, 0.22f, 0.45f), Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
            var text = Label("Text", area.transform, "", 40f, FontStyles.Bold, Ink, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
            foreach (var t in new[] { placeholder, text })
            {
                Stretch(t.rectTransform);
                t.overflowMode = TextOverflowModes.Masking;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            }

            _nameInput = go.GetComponent<TMP_InputField>();
            _nameInput.textViewport = areaRect;
            _nameInput.textComponent = text;
            _nameInput.placeholder = placeholder;
            _nameInput.fontAsset = font;
            _nameInput.pointSize = 40f;
            _nameInput.characterLimit = 40;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.caretColor = Ink;
            _nameInput.selectionColor = new Color(0.36f, 0.22f, 0.78f, 0.3f);
            _nameInput.onSubmit.AddListener(_ => Create());

            _createButton = MakeButton("Create Button", page, "Створити", 40f, Violet, Color.white, new Vector2(300f, -170f), new Vector2(300f, 84f));
            _createButton.onClick.AddListener(Create);

            var skip = MakeButton("Skip Button", page, "Продовжити без проєкту", 30f, Soft, Ink, new Vector2(0f, -262f), new Vector2(520f, 60f));
            skip.onClick.AddListener(EnterApartment);

            _projectsStatus = Label("Status", page, "", 28f, FontStyles.Bold, new Color(0.75f, 0.2f, 0.2f), new Vector2(0f, -325f), new Vector2(900f, 50f));
        }

        // ---------------------------------------------------------------- small UI helpers

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
            image.sprite = _round;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Color.black, 0.07f);
            colors.pressedColor = Color.Lerp(fill, Color.black, 0.22f);
            colors.disabledColor = new Color(0.8f, 0.8f, 0.84f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            Label("Label", go.transform, text, size, FontStyles.Bold, textColor, Vector2.zero, dim - new Vector2(24f, 8f));
            return button;
        }

        private static Sprite MakeRoundedSprite()
        {
            const int size = 64, radius = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    var dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                    var alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
