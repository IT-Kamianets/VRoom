using System.Collections;
using ITKamianets.Engine.Firebase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>
    /// The book on the table in the entrance room. It opens on the sign-in page (the "sign in with Google" button on
    /// the right page); once the player is signed in, whether just now or from a saved account, the page turns and the
    /// next spread shows their projects and a way to create one (<see cref="ProjectsPages"/>).
    /// </summary>
    public class LoginBook : MonoBehaviour
    {
        [SerializeField] private FirebaseBootstrap bootstrap;
        [SerializeField] private Button googleButton;
        [SerializeField] private TMP_Text buttonLabel;
        [SerializeField] private TMP_Text statusLabel;
        [Tooltip("Canvases of the sign-in spread; they are hidden when the page turns.")]
        [SerializeField] private GameObject[] loginPages;
        [SerializeField] private PageFlip pageFlip;
        [SerializeField] private ProjectsPages projectPages;
        [SerializeField, Min(0f)] private float turnDelay = 0.9f;
        [Header("Opening a project")]
        [Tooltip("The camera the player looks through; it is drawn in over the book while the pages riffle. Main camera when empty.")]
        [SerializeField] private Transform viewCamera;
        [SerializeField, Min(1)] private int riffleCount = 24;
        [SerializeField, Min(0.02f)] private float firstTurnSeconds = 0.06f;
        [SerializeField, Min(0.02f)] private float lastTurnSeconds = 0.02f;
        [SerializeField, Min(0.1f)] private float diveSeconds = 0.9f;
        [Tooltip("Everything of the entrance room (book, table, candles); it is switched off when the project's world opens.")]
        [SerializeField] private GameObject entranceRoot;

        private enum Step { Waiting, Login, Turning, Projects, Opening, Blank }

        private Step _step = Step.Waiting;
        private float _signedInAt = -1f;
        private ProjectStore _store;

        private void Awake()
        {
            googleButton.onClick.AddListener(() => bootstrap.SignInWithGoogle());
            projectPages.ProjectChosen += OnProjectChosen;
        }

        private void OnDestroy()
        {
            if (projectPages != null)
            {
                projectPages.ProjectChosen -= OnProjectChosen;
            }
        }

        /// <summary>The project the player is in; set once the pages have riffled to the blank page.</summary>
        public ProjectInfo OpenedProject { get; private set; }

        private void OnProjectChosen(ProjectInfo project)
        {
            if (_step != Step.Projects)
            {
                return;
            }

            _step = Step.Opening;
            OpenedProject = project;
            StartCoroutine(OpenProject());
        }

        // The pages riffle faster and faster while the camera sinks towards the paper; then it dives into the page,
        // the screen goes the colour of paper, and the project's empty world is there when it clears.
        private IEnumerator OpenProject()
        {
            projectPages.Hide();
            var cam = viewCamera != null ? viewCamera : Camera.main.transform;
            var book = pageFlip.transform;
            var startPosition = cam.position;
            var startRotation = cam.rotation;
            // the camera stays where the player stands and looks at the open page; the dive goes in from there
            var page = book.position + Vector3.up * 0.03f;
            var lookAtPage = Quaternion.LookRotation(page - startPosition, Vector3.up);

            var total = 0f;
            var lengths = new float[riffleCount];
            for (var i = 0; i < riffleCount; i++)
            {
                lengths[i] = Mathf.Lerp(firstTurnSeconds, lastTurnSeconds, riffleCount == 1 ? 1f : i / (riffleCount - 1f));
                total += lengths[i];
            }

            var elapsed = 0f;
            for (var i = 0; i < riffleCount; i++)
            {
                var turned = false;
                pageFlip.Play(() => turned = true, lengths[i]);
                while (!turned)
                {
                    elapsed += Time.deltaTime;
                    var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / total));
                    cam.rotation = Quaternion.Slerp(startRotation, lookAtPage, k);
                    yield return null;
                }
            }

            // the dive: from where the player stands straight into the page, accelerating, with the screen turning to paper
            var fade = EmptyWorld.CreateFade(new Color(0.96f, 0.91f, 0.78f));
            for (var t = 0f; t < diveSeconds; t += Time.deltaTime)
            {
                if (!pageFlip.IsPlaying)
                {
                    pageFlip.Play(null, lastTurnSeconds);   // the pages keep turning all the way into the book
                }

                var k = Mathf.Clamp01(t / diveSeconds);
                cam.position = Vector3.Lerp(startPosition, page, k * k);
                SetAlpha(fade, Mathf.SmoothStep(0f, 1f, (k - 0.35f) / 0.65f));
                yield return null;
            }

            SetAlpha(fade, 1f);
            EnterWorld(cam);
            _step = Step.Blank;
            fade.gameObject.AddComponent<FadeOut>();   // this book is switched off with the room, so the fade runs on its own
        }

        private static void SetAlpha(UnityEngine.UI.Image image, float alpha)
        {
            var c = image.color;
            c.a = alpha;
            image.color = c;
        }

        // Switches the entrance room off and puts the player in the basic empty world, where the project is worked on.
        private void EnterWorld(Transform cam)
        {
            var room = entranceRoot != null ? entranceRoot : transform.root.gameObject;
            var world = EmptyWorld.Create(new Vector3(0f, -200f, 0f));
            EmptyWorld.UseDefaultLighting();
            room.SetActive(false);
            cam.SetPositionAndRotation(world.transform.position + new Vector3(0f, 1.7f, -4f), Quaternion.Euler(10f, 0f, 0f));
        }

        private void Update()
        {
            if (_step >= Step.Turning)
            {
                return;
            }

            if (_step == Step.Waiting && bootstrap.LoginRequired)
            {
                _step = Step.Login;
            }

            googleButton.gameObject.SetActive((bootstrap.LoginRequired || bootstrap.Busy) && !bootstrap.IsSignedIn);
            googleButton.interactable = !bootstrap.Busy;
            buttonLabel.text = bootstrap.Busy ? "Чекаємо браузер…" : "Увійти через Google";

            if (bootstrap.IsSignedIn)
            {
                statusLabel.text = "Ви увійшли. Ласкаво просимо до VRoom!";
                if (_signedInAt < 0f)
                {
                    _signedInAt = Time.time;
                }

                if (Time.time - _signedInAt >= turnDelay)
                {
                    TurnToProjects();
                }
            }
            else
            {
                statusLabel.text = bootstrap.Status;
            }
        }

        private void TurnToProjects()
        {
            _step = Step.Turning;
            foreach (var page in loginPages)
            {
                page.SetActive(false);
            }

            pageFlip.Play(OnTurned);
        }

        private async void OnTurned()
        {
            _step = Step.Projects;
            _store = new ProjectStore(EngineFirebase.Current);
            projectPages.Show(_store);   // the empty list first, then what the player has saved
            try
            {
                await _store.LoadAsync();
                projectPages.Show(_store);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[VRoom] Could not load the projects: {exception.Message}", this);
            }
        }
    }
}
