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

        private enum Step { Waiting, Login, Turning, Projects }

        private Step _step = Step.Waiting;
        private float _signedInAt = -1f;
        private ProjectStore _store;

        private void Awake()
        {
            googleButton.onClick.AddListener(() => bootstrap.SignInWithGoogle());
        }

        private void Update()
        {
            if (_step == Step.Turning || _step == Step.Projects)
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
