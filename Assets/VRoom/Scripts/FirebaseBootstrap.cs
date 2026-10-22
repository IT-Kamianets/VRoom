using System;
using System.Threading.Tasks;
using ITKamianets.Engine.Core;
using ITKamianets.Engine.Firebase;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// Connects VRoom to its Firebase project (vroom-8ef42) on start and signs the player in with email.
    /// The account saved on this device is reused; without one, a simple login window asks for email and password.
    /// Put it on one object in the first scene and assign Assets/VRoom/Firebase/google-services.json.
    /// </summary>
    public class FirebaseBootstrap : MonoBehaviour
    {
        private const string LogCategory = "VRoom.Firebase";

        [SerializeField] private TextAsset googleServicesJson;

        private EngineFirebase _firebase;
        private bool _showLogin;
        private bool _busy;
        private string _email = "";
        private string _password = "";
        private string _status = "";

        private async void Start()
        {
            if (EngineFirebase.Current != null)
            {
                return;
            }

            try
            {
                _firebase = EngineFirebase.Initialize(FirebaseConfig.FromGoogleServicesJson(googleServicesJson.text));
                if (await _firebase.Auth.TrySignInAgainAsync())
                {
                    Connected();
                }
                else
                {
                    _showLogin = true;
                }
            }
            catch (Exception exception)
            {
                EngineLog.Error(LogCategory, $"Firebase connection failed: {exception.Message}", this);
            }
        }

        private void Connected()
        {
            _showLogin = false;
            EngineLog.Info(LogCategory, $"Connected to {_firebase.Config.ProjectId} as {_firebase.Auth.Uid}.", this);
        }

        private async void Run(Func<Task> action, string doneStatus = null)
        {
            _busy = true;
            _status = "";
            try
            {
                await action();
                if (_firebase.Auth.IsSignedIn)
                {
                    Connected();
                }

                _status = doneStatus ?? "";
            }
            catch (Exception exception)
            {
                _status = exception.Message;
                EngineLog.Warning(LogCategory, $"Sign-in failed: {exception.Message}", this);
            }
            finally
            {
                _busy = false;
            }
        }

        // A temporary login window until VRoom has its own menus; it shows on the PC screen, not in the headset.
        private void OnGUI()
        {
            if (!_showLogin)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(Screen.width / 2f - 160, Screen.height / 2f - 110, 320, 220), GUI.skin.box);
            GUILayout.Label("VRoom: вхід");
            GUILayout.Label("Пошта");
            _email = GUILayout.TextField(_email);
            GUILayout.Label("Пароль");
            _password = GUILayout.PasswordField(_password, '*');

            GUI.enabled = !_busy;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Увійти"))
            {
                Run(() => _firebase.Auth.SignInWithEmailAsync(_email.Trim(), _password));
            }

            if (GUILayout.Button("Створити акаунт"))
            {
                Run(() => _firebase.Auth.SignUpWithEmailAsync(_email.Trim(), _password));
            }

            GUILayout.EndHorizontal();
            if (GUILayout.Button("Забули пароль?"))
            {
                Run(() => _firebase.Auth.SendPasswordResetAsync(_email.Trim()), "Лист для скидання пароля надіслано.");
            }

            GUI.enabled = true;
            GUILayout.Label(_status);
            GUILayout.EndArea();
        }
    }
}
