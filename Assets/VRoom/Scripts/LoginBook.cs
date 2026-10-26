using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>
    /// The book on the table in the entrance room: its right page holds the "sign in with Google" button.
    /// It shows while the player has no saved account and hides the button once they are in.
    /// Needs a world-space canvas with a Button and two Texts on the page, and the FirebaseBootstrap of the scene.
    /// </summary>
    public class LoginBook : MonoBehaviour
    {
        [SerializeField] private FirebaseBootstrap bootstrap;
        [SerializeField] private Button googleButton;
        [SerializeField] private TMP_Text buttonLabel;
        [SerializeField] private TMP_Text statusLabel;

        private void Awake()
        {
            googleButton.onClick.AddListener(() => bootstrap.SignInWithGoogle());
        }

        private void Update()
        {
            var loginOpen = bootstrap.LoginRequired;
            googleButton.gameObject.SetActive(loginOpen || bootstrap.Busy);
            googleButton.interactable = !bootstrap.Busy;
            buttonLabel.text = bootstrap.Busy ? "Чекаємо браузер…" : "Увійти через Google";

            if (bootstrap.IsSignedIn)
            {
                statusLabel.text = "Ви увійшли. Ласкаво просимо до VRoom!";
            }
            else
            {
                statusLabel.text = bootstrap.Status;
            }
        }
    }
}
