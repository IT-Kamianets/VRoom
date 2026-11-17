using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VRoom
{
    /// <summary>
    /// Test bench for placing furniture by a text command through a local Ollama model.
    /// It builds a 10x10 m room with a window on the west wall, sends the command to Ollama, reads the JSON answer
    /// (item, position, x, z) and puts the matching furniture model into the room. Type a command in the box in the
    /// Game view and press Enter or the button.
    /// </summary>
    public class FurnitureAI : MonoBehaviour
    {
        [SerializeField] string ollamaUrl = "http://localhost:11434";
        [SerializeField] string model = "qwen3.5:4b";
        [SerializeField] float roomHalf = 5f;
        [SerializeField] float wallGap = 0.05f;

        // The model does not calculate metres for "next to X": it names the relation and the target, the code finds the spot.
        // Both the prompt and the schema are built from the Catalog, so a new model only needs one line there.
        static string SystemPrompt =>
            "You control a VR room 10x10 meters. Seen from above: +x is RIGHT, -x is LEFT (the window is on the left wall, x=-5), " +
            "+z is UP (far wall), -z is DOWN (near the viewer). Coordinates are from -5 to 5. Reply ONLY with JSON following the schema. " +
            "Items (id = what the user may call it): sofa_beige = sofa/диван, double_bed_beige = bed/ліжко, modern_double_bed = modern bed, " +
            "armchair_beige = armchair/крісло, dining_table_oak = table/стіл, coffee_table_oval = coffee table/журнальний столик, " +
            "old_table = old table, dining_chair_linen = chair/стілець, wardrobe_oak = wardrobe/шафа, fridge_side_by_side = fridge/холодильник, " +
            "fridge_built_in = built-in fridge, nightstand_oak = nightstand/тумбочка, bedside_table = small bedside table, " +
            "media_console_oak = TV stand/тумба під телевізор, kitchen_stove = stove/плита, kitchen_sink_cabinet = sink/мийка, " +
            "kitchen_base_cabinet = kitchen cabinet, kitchen_wall_cabinet = wall cabinet/навісна шафа, bathroom_vanity_oak = bathroom vanity, " +
            "shower_cabin = shower/душ, toilet_wall_hung = toilet/унітаз. " +
            "action: add = put a NEW item; move = move an EXISTING item; rotate = turn an EXISTING item by rotate_degrees " +
            "(positive = clockwise seen from above); remove = delete an EXISTING item. " +
            "For rotate and remove use position center, relation none, target none, x 0, z 0. " +
            "If the command puts or moves the item next to, to the right of, to the left of, behind or in front of ANOTHER item, " +
            "set relation (next_to, right_of, left_of, behind, in_front_of) and target (that other item, from the room state) and set x and z to 0; " +
            "the program computes the exact spot. Otherwise set relation none, target none and give x and z " +
            "('closer to the center' means x 0, z 0; 'a bit to the right' means the item's current x plus 1). " +
            "If the command says 'under / by / near / against a wall', position MUST be a wall and NOT center: " +
            "west_wall = the left wall with the window, east_wall = the right wall, north_wall = the far wall (up), " +
            "south_wall = the near wall (down); if no wall is named use wall. Use center only when the command says the middle of the room. " +
            "If the command says 'in a corner', position MUST be corner (not a wall) and x,z MUST be that corner: left far = (-4.5, 4.5), " +
            "right far = (4.5, 4.5), left near = (-4.5, -4.5), right near = (4.5, -4.5); if no corner is named use (-4.5, 4.5). " +
            "Use the room state. Never use add to change an item that already exists. No explanations.\n" +
            "Example. Room: sofa_beige at (-4.0, 0.0) rotated 0 deg. Command: put a fridge to the right of the sofa. " +
            "Answer: {\"action\":\"add\",\"item\":\"fridge_side_by_side\",\"position\":\"center\",\"relation\":\"right_of\",\"target\":\"sofa_beige\",\"x\":0,\"z\":0,\"rotate_degrees\":0}\n" +
            "Example. Room: dining_table_oak at (1.0, 1.0) rotated 0 deg. Command: move the table a bit to the left. " +
            "Answer: {\"action\":\"move\",\"item\":\"dining_table_oak\",\"position\":\"center\",\"relation\":\"none\",\"target\":\"none\",\"x\":0.0,\"z\":1.0,\"rotate_degrees\":0}\n" +
            "Example. Room: empty. Command: put a fridge in the right near corner. " +
            "Answer: {\"action\":\"add\",\"item\":\"fridge_side_by_side\",\"position\":\"corner\",\"relation\":\"none\",\"target\":\"none\",\"x\":4.5,\"z\":-4.5,\"rotate_degrees\":0}\n" +
            "Example. Room: empty. Command: put a wardrobe against the right wall. " +
            "Answer: {\"action\":\"add\",\"item\":\"wardrobe_oak\",\"position\":\"east_wall\",\"relation\":\"none\",\"target\":\"none\",\"x\":0,\"z\":0,\"rotate_degrees\":0}";

        // Ollama forces the answer to follow this JSON schema, so item, action and relation are always valid words.
        static string Schema
        {
            get
            {
                var items = "\"" + string.Join("\",\"", Catalog.Keys) + "\"";
                return @"{""type"":""object"",""properties"":{" +
                    @"""action"":{""type"":""string"",""enum"":[""add"",""move"",""rotate"",""remove""]}," +
                    @"""item"":{""type"":""string"",""enum"":[" + items + "]}," +
                    @"""position"":{""type"":""string"",""enum"":[""near_window"",""center"",""corner"",""wall"",""west_wall"",""east_wall"",""north_wall"",""south_wall""]}," +
                    @"""relation"":{""type"":""string"",""enum"":[""none"",""next_to"",""right_of"",""left_of"",""behind"",""in_front_of""]}," +
                    @"""target"":{""type"":""string"",""enum"":[""none""," + items + "]}," +
                    @"""x"":{""type"":""number""},""z"":{""type"":""number""},""rotate_degrees"":{""type"":""number""}}," +
                    @"""required"":[""action"",""item"",""position"",""relation"",""target"",""x"",""z"",""rotate_degrees""]}";
            }
        }

        [Serializable] public class ChatMsg { public string role; public string content; }
        [Serializable] public class Opts { public int num_ctx = 2048; public float temperature = 0.2f; }
        [Serializable]
        public class ChatReq
        {
            public string model;
            public ChatMsg[] messages;
            public bool stream;
            public bool think;
            public string format = "json";
            public string keep_alive = "30m"; // keep the model in memory so the next command is fast
            public Opts options = new Opts();
        }

        [Serializable]
        public class SavedState
        {
            public List<string> history = new List<string>();
            public string last = "";
            public bool open = true;
        }
        [Serializable] public class ChatResp { public ChatMsg message; }
        [Serializable]
        public class Reply
        {
            public string action;
            public string item;
            public string position;
            public string relation;
            public string target;
            public float x;
            public float z;
            public float rotate_degrees;
        }

        class Piece
        {
            public string item;
            public GameObject go;
        }

        struct Entry
        {
            public string file;
            public float lift; // metres above the floor, for things that hang on a wall
            public Entry(string file, float lift = 0f) { this.file = file; this.lift = lift; }
        }

        // The models from the Assetra collection "VRoom" are already in real metres, so they are not scaled.
        static readonly Dictionary<string, Entry> Catalog = new Dictionary<string, Entry>
        {
            { "sofa_beige", new Entry("sofa_beige") },
            { "double_bed_beige", new Entry("double_bed_beige") },
            { "modern_double_bed", new Entry("modern_double_bed") },
            { "armchair_beige", new Entry("armchair_beige") },
            { "dining_table_oak", new Entry("dining_table_oak") },
            { "coffee_table_oval", new Entry("coffee_table_oval") },
            { "old_table", new Entry("old_table") },
            { "dining_chair_linen", new Entry("dining_chair_linen") },
            { "wardrobe_oak", new Entry("wardrobe_oak") },
            { "fridge_side_by_side", new Entry("fridge_side_by_side") },
            { "fridge_built_in", new Entry("fridge_built_in") },
            { "nightstand_oak", new Entry("nightstand_oak") },
            { "bedside_table", new Entry("bedside_table") },
            { "media_console_oak", new Entry("media_console_oak") },
            { "kitchen_stove", new Entry("kitchen_stove") },
            { "kitchen_sink_cabinet", new Entry("kitchen_sink_cabinet") },
            { "kitchen_base_cabinet", new Entry("kitchen_base_cabinet") },
            { "kitchen_wall_cabinet", new Entry("kitchen_wall_cabinet", 1.4f) },
            { "bathroom_vanity_oak", new Entry("bathroom_vanity_oak") },
            { "shower_cabin", new Entry("shower_cabin") },
            { "toilet_wall_hung", new Entry("toilet_wall_hung") },
        };

        Transform furnitureRoot;
        readonly List<Piece> pieces = new List<Piece>();
        const string PrefsKey = "VRoom.FurnitureAI.State";
        const int MaxHistory = 20;

        string input = "";
        string status = "Готово";
        string lastJson = "";
        bool busy;

        bool open = true;
        readonly List<string> history = new List<string>();
        int recall;
        Vector2 historyScroll;
        bool focusInput;
        GUIStyle leftButton;

        void Start()
        {
            BuildRoom();
            PlaceCamera();
            LoadState();
            StartCoroutine(WarmUp());
        }

        // ---------------------------------------------------------------- saved state

        void LoadState()
        {
            try
            {
                var json = PlayerPrefs.GetString(PrefsKey, "");
                if (string.IsNullOrEmpty(json)) return;
                var s = JsonUtility.FromJson<SavedState>(json);
                history.Clear();
                history.AddRange(s.history);
                open = s.open;
                recall = history.Count;
            }
            catch (Exception ex) { Debug.LogWarning("FurnitureAI: cannot load saved state: " + ex.Message); }
        }

        void SaveState()
        {
            try
            {
                var s = new SavedState { open = open };
                s.history.AddRange(history);
                PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(s));
                PlayerPrefs.Save();
            }
            catch (Exception ex) { Debug.LogWarning("FurnitureAI: cannot save state: " + ex.Message); }
        }

        void Remember(string text)
        {
            history.Remove(text);
            history.Add(text);
            while (history.Count > MaxHistory) history.RemoveAt(0);
            recall = history.Count;
            input = ""; // the box is cleared once the command is sent
            SaveState();
        }

        void Toggle()
        {
            open = !open;
            focusInput = open;
            SaveState();
        }

        /// <summary>Loads the model into memory in the background, so the first real command is not slow.</summary>
        IEnumerator WarmUp()
        {
            var req = new ChatReq
            {
                model = model,
                stream = false,
                think = false,
                messages = new[] { new ChatMsg { role = "user", content = "ok" } },
                options = new Opts { num_ctx = 2048, temperature = 0.2f },
            };
            using (var www = new UnityWebRequest(ollamaUrl + "/api/chat", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(req)));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = 180;
                yield return www.SendWebRequest();
            }
        }

        // ---------------------------------------------------------------- room

        void BuildRoom()
        {
            var root = new GameObject("Room").transform;
            root.SetParent(transform, false);
            furnitureRoot = new GameObject("Furniture").transform;
            furnitureRoot.SetParent(transform, false);

            float size = roomHalf * 2f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            Paint(floor, new Color(0.62f, 0.5f, 0.38f));

            const float h = 2.6f, t = 0.1f;
            var wallColor = new Color(0.9f, 0.88f, 0.84f);
            Wall(root, "Wall West", new Vector3(-roomHalf - t / 2, h / 2, 0), new Vector3(t, h, size + t * 2), wallColor);
            Wall(root, "Wall East", new Vector3(roomHalf + t / 2, h / 2, 0), new Vector3(t, h, size + t * 2), wallColor);
            Wall(root, "Wall North", new Vector3(0, h / 2, roomHalf + t / 2), new Vector3(size, h, t), wallColor);
            Wall(root, "Wall South", new Vector3(0, h / 2, -roomHalf - t / 2), new Vector3(size, h, t), wallColor);

            // A bright window on the west wall, centred at z = 0.
            var window = GameObject.CreatePrimitive(PrimitiveType.Cube);
            window.name = "Window";
            window.transform.SetParent(root, false);
            window.transform.localPosition = new Vector3(-roomHalf + 0.02f, 1.5f, 0f);
            window.transform.localScale = new Vector3(0.05f, 1.2f, 1.8f);
            Paint(window, new Color(0.55f, 0.8f, 1f));
            Destroy(window.GetComponent<Collider>());
        }

        static void Wall(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = name;
            w.transform.SetParent(parent, false);
            w.transform.localPosition = pos;
            w.transform.localScale = scale;
            Paint(w, color);
        }

        static void Paint(GameObject go, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            go.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = color };
        }

        void PlaceCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(0f, 11f, -3.5f);
            cam.transform.LookAt(new Vector3(0f, 0f, 0.5f));
        }

        // ---------------------------------------------------------------- UI

        void OnGUI()
        {
            var e = Event.current;

            // F2 opens and closes the panel from anywhere.
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F2)
            {
                Toggle();
                e.Use();
                return;
            }

            if (leftButton == null)
                leftButton = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };

            if (!open)
            {
                if (GUI.Button(new Rect(20, 20, 150, 32), "AI чат (F2)")) Toggle();
                return;
            }

            string toRun = null; // run after the layout is finished, so the history can change safely

            GUILayout.BeginArea(new Rect(20, 20, Mathf.Min(Screen.width - 40, 720), 380), GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("VRoom AI: команда для меблів (" + model + ")");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Закрити (F2)", GUILayout.Width(110))) Toggle();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Історія (" + history.Count + ")");
            GUILayout.FlexibleSpace();
            if (history.Count > 0 && GUILayout.Button("Очистити історію", GUILayout.Width(140)))
            {
                history.Clear();
                recall = 0;
                SaveState();
            }
            GUILayout.EndHorizontal();

            historyScroll = GUILayout.BeginScrollView(historyScroll, GUILayout.Height(110));
            for (int i = history.Count - 1; i >= 0; i--)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(history[i], leftButton)) { input = history[i]; recall = i; focusInput = true; }
                GUI.enabled = !busy;
                if (GUILayout.Button("↻", GUILayout.Width(32))) toRun = history[i];
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            // Up and Down arrows walk through the history while the input has focus.
            bool inputFocused = GUI.GetNameOfFocusedControl() == "cmd";
            if (inputFocused && e.type == EventType.KeyDown && history.Count > 0)
            {
                if (e.keyCode == KeyCode.UpArrow)
                {
                    recall = Mathf.Max(0, recall - 1);
                    input = history[recall];
                    e.Use();
                }
                else if (e.keyCode == KeyCode.DownArrow)
                {
                    recall = Mathf.Min(history.Count, recall + 1);
                    input = recall < history.Count ? history[recall] : "";
                    e.Use();
                }
            }

            bool enter = inputFocused && e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            GUI.SetNextControlName("cmd");
            input = GUILayout.TextField(input);
            if (focusInput && e.type == EventType.Repaint) { GUI.FocusControl("cmd"); focusInput = false; }

            GUILayout.BeginHorizontal();
            GUI.enabled = !busy;
            if (GUILayout.Button("Поставити") || enter) { toRun = input; if (enter) e.Use(); }
            GUI.enabled = true;
            if (GUILayout.Button("Очистити кімнату")) Clear();
            GUILayout.EndHorizontal();

            GUILayout.Label(status);
            if (!string.IsNullOrEmpty(lastJson)) GUILayout.Label("JSON: " + lastJson);
            GUILayout.EndArea();

            if (toRun != null) Submit(toRun);
        }

        void Submit(string text)
        {
            if (busy || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            Remember(text);
            StartCoroutine(Run(text));
        }

        void OnDisable() => SaveState();

        void Clear()
        {
            if (furnitureRoot == null) return;
            foreach (Transform c in furnitureRoot) Destroy(c.gameObject);
            pieces.Clear();
            status = "Кімнату очищено";
        }

        // ---------------------------------------------------------------- Ollama

        /// <summary>The chat request with the JSON schema as the answer format (built by hand: JsonUtility cannot nest a schema).</summary>
        string BuildChatBody(string user)
        {
            return "{\"model\":\"" + Esc(model) + "\",\"stream\":false,\"think\":false,\"keep_alive\":\"30m\"," +
                   "\"format\":" + Schema + ",\"options\":{\"num_ctx\":2048,\"temperature\":0.2}," +
                   "\"messages\":[{\"role\":\"system\",\"content\":\"" + Esc(SystemPrompt) + "\"}," +
                   "{\"role\":\"user\",\"content\":\"" + Esc(user) + "\"}]}";
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        IEnumerator Run(string text)
        {
            busy = true;
            status = "Модель думає...";
            lastJson = "";

            var body = Encoding.UTF8.GetBytes(BuildChatBody(BuildState() + "\nCommand: " + text));

            using (var www = new UnityWebRequest(ollamaUrl + "/api/chat", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(body);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = 180;
                float started = Time.realtimeSinceStartup;
                yield return www.SendWebRequest();
                float took = Time.realtimeSinceStartup - started;

                if (www.result != UnityWebRequest.Result.Success)
                {
                    status = "Помилка Ollama: " + www.error + " (чи запущена Ollama на " + ollamaUrl + "?)";
                    busy = false;
                    yield break;
                }

                try
                {
                    var resp = JsonUtility.FromJson<ChatResp>(www.downloadHandler.text);
                    lastJson = resp.message.content.Trim().Replace("\n", " ");
                    var reply = JsonUtility.FromJson<Reply>(resp.message.content);
                    status = Apply(reply) + "  [" + took.ToString("0.0") + " с]";
                }
                catch (Exception ex)
                {
                    status = "Не вдалося розібрати відповідь: " + ex.Message;
                }
            }
            busy = false;
        }

        // ---------------------------------------------------------------- placing

        /// <summary>Tells the model what is already in the room, so "turn the wardrobe" can find the wardrobe.</summary>
        string BuildState()
        {
            if (pieces.Count == 0) return "Room: empty.";
            var sb = new StringBuilder("Room: ");
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                var c = GetBounds(p.go).center;
                int yaw = Mathf.RoundToInt(Mathf.Repeat(p.go.transform.eulerAngles.y, 360f)) % 360;
                if (i > 0) sb.Append("; ");
                sb.Append(p.item).Append(" at (")
                  .Append(c.x.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ")
                  .Append(c.z.ToString("0.0", CultureInfo.InvariantCulture)).Append(") rotated ")
                  .Append(yaw).Append(" deg");
            }
            return sb.Append('.').ToString();
        }

        string Apply(Reply r)
        {
            if (r == null || string.IsNullOrEmpty(r.item) || !Catalog.TryGetValue(r.item, out var entry))
                return "Модель повернула невідомий предмет: " + (r == null ? "null" : r.item);

            // The model named the other item but forgot the relation: "next to" is the natural reading.
            if ((string.IsNullOrEmpty(r.relation) || r.relation == "none") && !string.IsNullOrEmpty(r.target) && r.target != "none")
                r.relation = "next_to";

            switch ((r.action ?? "add").ToLowerInvariant())
            {
                case "add":
                    return Add(r, entry);

                case "move":
                {
                    var p = FindPiece(r.item);
                    return p == null ? NoPiece(r.item) : Move(p, r);
                }

                case "rotate":
                {
                    var p = FindPiece(r.item);
                    return p == null ? NoPiece(r.item) : Rotate(p, r.rotate_degrees);
                }

                case "remove":
                {
                    var p = FindPiece(r.item);
                    if (p == null) return NoPiece(r.item);
                    pieces.Remove(p);
                    Destroy(p.go);
                    return "Прибрав: " + r.item;
                }

                default:
                    return "Невідома дія: " + r.action;
            }
        }

        static string NoPiece(string item) => "У кімнаті немає предмета: " + item;

        /// <summary>The newest piece of that kind.</summary>
        Piece FindPiece(string item)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
                if (pieces[i].item == item) return pieces[i];
            return null;
        }

        static bool HasRelation(Reply r) =>
            !string.IsNullOrEmpty(r.relation) && r.relation != "none" && !string.IsNullOrEmpty(r.target) && r.target != "none";

        /// <summary>The newest piece of that kind, other than <paramref name="self"/>.</summary>
        Piece FindPieceOtherThan(string item, Piece self)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
                if (pieces[i].item == item && pieces[i] != self) return pieces[i];
            return null;
        }

        static string Describe(Reply r) => HasRelation(r) ? r.relation + " " + r.target : r.position;

        string Add(Reply r, Entry entry)
        {
            if (HasRelation(r) && FindPieceOtherThan(r.target, null) == null) return NoPiece(r.target);

            var prefab = LoadPrefab(entry.file);
            if (prefab == null)
                return "Не знайдено модель Assets/VRoom/Furniture/" + entry.file + ".glb (це працює лише в Editor)";

            var go = Instantiate(prefab, furnitureRoot);
            go.name = r.item;

            var piece = new Piece { item = r.item, go = go };
            var at = PlaceAt(piece, r);
            pieces.Add(piece);
            return "Поставив: " + r.item + " (" + Describe(r) + ") у " + Fmt(at);
        }

        string Move(Piece p, Reply r)
        {
            if (HasRelation(r) && FindPieceOtherThan(r.target, p) == null) return NoPiece(r.target);

            // "another corner": if the model chose the spot the piece already stands in, take the opposite corner.
            var old = GetBounds(p.go).center;
            if (r.position == "corner" && new Vector2(r.x - old.x, r.z - old.z).magnitude < 1.5f)
            {
                r.x = old.x > 0f ? -roomHalf : roomHalf;
                r.z = old.z > 0f ? -roomHalf : roomHalf;
            }
            var at = PlaceAt(p, r);
            return "Перемістив: " + p.item + " (" + Describe(r) + ") у " + Fmt(at);
        }

        string Rotate(Piece p, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f) degrees = 90f;
            p.go.transform.RotateAround(GetBounds(p.go).center, Vector3.up, degrees);

            // Keep it inside the room after turning.
            var b = GetBounds(p.go);
            float lim = roomHalf - wallGap;
            var shift = Vector3.zero;
            if (b.min.x < -lim) shift.x = -lim - b.min.x; else if (b.max.x > lim) shift.x = lim - b.max.x;
            if (b.min.z < -lim) shift.z = -lim - b.min.z; else if (b.max.z > lim) shift.z = lim - b.max.z;
            p.go.transform.position += shift;
            return "Повернув: " + p.item + " на " + degrees.ToString("0") + "°";
        }

        /// <summary>Puts the piece at the wanted spot on the floor, inside the room and away from other furniture.</summary>
        Vector3 PlaceAt(Piece p, Reply r)
        {
            // "next to / right of / left of / behind / in front of another piece": the exact spot comes from real sizes.
            if (HasRelation(r))
            {
                var anchor = FindPieceOtherThan(r.target, p);
                if (anchor != null) return PlaceRelative(p, anchor, r.relation);
            }

            var go = p.go;
            float x = Mathf.Clamp(r.x, -roomHalf, roomHalf);
            float z = Mathf.Clamp(r.z, -roomHalf, roomHalf);
            SnapToPosition(p, r.position, ref x, ref z);

            // Turn the front towards the room centre (snapped to 90 degrees), unless it stands in the middle.
            if (r.position != "center")
            {
                float yaw = Mathf.Round(Mathf.Atan2(-x, -z) * Mathf.Rad2Deg / 90f) * 90f;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            var b = GetBounds(go);
            float lim = roomHalf - wallGap;
            var target = new Vector3(
                Mathf.Clamp(x, -lim + b.extents.x, lim - b.extents.x),
                0f,
                Mathf.Clamp(z, -lim + b.extents.z, lim - b.extents.z));

            target = FindFreeSpot(target, b.extents, lim, p);

            return Put(go, b, target);
        }

        /// <summary>
        /// Puts the piece beside another one, with the same turn and a small gap. If that side is outside the room
        /// or taken, the other sides are tried (right, left, behind, in front).
        /// </summary>
        Vector3 PlaceRelative(Piece p, Piece anchor, string relation)
        {
            var go = p.go;
            go.transform.rotation = Quaternion.Euler(0f, Mathf.Round(anchor.go.transform.eulerAngles.y / 90f) * 90f, 0f);

            var a = GetBounds(anchor.go);
            var b = GetBounds(go);
            float lim = roomHalf - wallGap;
            const float gap = 0.1f;

            var order = relation == "next_to"
                ? new[] { "right_of", "left_of", "behind", "in_front_of" }
                : new[] { relation, "right_of", "left_of", "behind", "in_front_of" };

            Vector3? first = null;
            foreach (var side in order)
            {
                var c = SideCenter(a, b.extents, side, gap);
                // Along the wall a deeper piece is simply pushed in from the wall, not rejected.
                if (side == "right_of" || side == "left_of")
                    c.z = Mathf.Clamp(c.z, -lim + b.extents.z, lim - b.extents.z);
                else
                    c.x = Mathf.Clamp(c.x, -lim + b.extents.x, lim - b.extents.x);
                if (first == null) first = c;
                bool inside = Mathf.Abs(c.x) + b.extents.x <= lim + 0.001f && Mathf.Abs(c.z) + b.extents.z <= lim + 0.001f;
                if (inside && !Overlaps(c, b.extents, p)) return Put(go, b, c);
            }

            // Nothing fits cleanly: keep the first wanted side, inside the room and pushed away from other furniture.
            var f = first.Value;
            f.x = Mathf.Clamp(f.x, -lim + b.extents.x, lim - b.extents.x);
            f.z = Mathf.Clamp(f.z, -lim + b.extents.z, lim - b.extents.z);
            return Put(go, b, FindFreeSpot(f, b.extents, lim, p));
        }

        static readonly Vector2[] CornerOrder =
            { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };

        /// <summary>
        /// Makes "against a wall" and "in a corner" exact, whatever numbers the model gave: a wall puts the piece
        /// right at that wall, a corner at the nearest corner (or the first free one when the model gave none).
        /// </summary>
        void SnapToPosition(Piece p, string position, ref float x, ref float z)
        {
            switch (position)
            {
                case "near_window":
                case "west_wall": x = -roomHalf; return;
                case "east_wall": x = roomHalf; return;
                case "north_wall": z = roomHalf; return;
                case "south_wall": z = -roomHalf; return;

                case "wall":
                {
                    // The nearest wall to the given spot; with no spot at all, the far (north) wall.
                    float dW = x + roomHalf, dE = roomHalf - x, dS = z + roomHalf, dN = roomHalf - z;
                    bool noSpot = Mathf.Abs(x) < 0.5f && Mathf.Abs(z) < 0.5f;
                    float min = noSpot ? dN : Mathf.Min(Mathf.Min(dW, dE), Mathf.Min(dS, dN));
                    if (Mathf.Approximately(min, dN)) z = roomHalf;
                    else if (Mathf.Approximately(min, dW)) x = -roomHalf;
                    else if (Mathf.Approximately(min, dE)) x = roomHalf;
                    else z = -roomHalf;
                    return;
                }

                case "corner":
                {
                    if (Mathf.Abs(x) >= 1f || Mathf.Abs(z) >= 1f)
                    {
                        x = x >= 0f ? roomHalf : -roomHalf;
                        z = z >= 0f ? roomHalf : -roomHalf;
                        return;
                    }
                    var ext = GetBounds(p.go).extents;
                    foreach (var c in CornerOrder)
                    {
                        var spot = new Vector3(c.x * (roomHalf - ext.x), 0f, c.y * (roomHalf - ext.z));
                        if (!Overlaps(spot, ext, p)) { x = c.x * roomHalf; z = c.y * roomHalf; return; }
                    }
                    x = -roomHalf; z = roomHalf;
                    return;
                }
            }
        }

        static Vector3 SideCenter(Bounds a, Vector3 ext, string side, float gap)
        {
            switch (side)
            {
                case "right_of": return new Vector3(a.max.x + ext.x + gap, 0f, a.center.z);
                case "left_of": return new Vector3(a.min.x - ext.x - gap, 0f, a.center.z);
                case "behind": return new Vector3(a.center.x, 0f, a.max.z + ext.z + gap);
                default: return new Vector3(a.center.x, 0f, a.min.z - ext.z - gap); // in_front_of
            }
        }

        static Vector3 Put(GameObject go, Bounds b, Vector3 c)
        {
            // The bottom stands on the floor, or at the wall-mount height for things like a kitchen wall cabinet.
            float lift = Catalog.TryGetValue(go.name, out var entry) ? entry.lift : 0f;
            go.transform.position += new Vector3(c.x - b.center.x, lift - b.min.y, c.z - b.center.z);
            return c;
        }

        static string Fmt(Vector3 v) => "(" + v.x.ToString("0.0") + "; " + v.z.ToString("0.0") + ")";

        Vector3 FindFreeSpot(Vector3 wanted, Vector3 ext, float lim, Piece self)
        {
            if (!Overlaps(wanted, ext, self)) return wanted;
            for (int ring = 1; ring <= 16; ring++)
            {
                float d = ring * 0.3f;
                for (int a = 0; a < 360; a += 45)
                {
                    var c = wanted + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * d;
                    c.x = Mathf.Clamp(c.x, -lim + ext.x, lim - ext.x);
                    c.z = Mathf.Clamp(c.z, -lim + ext.z, lim - ext.z);
                    if (!Overlaps(c, ext, self)) return c;
                }
            }
            return wanted;
        }

        bool Overlaps(Vector3 center, Vector3 ext, Piece self)
        {
            // Pieces at different heights do not collide (a wall cabinet hangs above a sink).
            float selfBottom = Catalog.TryGetValue(self.item, out var entry) ? entry.lift : 0f;
            float selfTop = selfBottom + GetBounds(self.go).size.y;

            foreach (var other in pieces)
            {
                if (other == self) continue;
                var p = GetBounds(other.go);
                if (p.max.y <= selfBottom + 0.01f || p.min.y >= selfTop - 0.01f) continue;
                if (Mathf.Abs(center.x - p.center.x) < ext.x + p.extents.x &&
                    Mathf.Abs(center.z - p.center.z) < ext.z + p.extents.z)
                    return true;
            }
            return false;
        }

        static Bounds GetBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static GameObject LoadPrefab(string file)
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRoom/Furniture/" + file + ".glb");
            if (asset != null) return asset;
#endif
            return Resources.Load<GameObject>("Furniture/" + file);
        }
    }
}
