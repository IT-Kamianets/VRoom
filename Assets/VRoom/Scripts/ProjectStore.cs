using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ITKamianets.Engine.Firebase;

namespace VRoom
{
    [Serializable]
    public class ProjectInfo
    {
        public string id;
        public string name;
        public string createdAt;
    }

    [Serializable]
    public class ProjectList
    {
        public List<ProjectInfo> items = new List<ProjectInfo>();
    }

    /// <summary>
    /// The signed-in player's VRoom projects. They are kept in the player's own saved progress
    /// (<c>users/{uid}/progress/projects</c>), which the Firestore rules already allow, so they follow the player to
    /// any device. A project is only its id and name for now; the room itself will be saved under it later.
    /// </summary>
    public class ProjectStore
    {
        private const string Key = "projects";
        private const int MaxNameLength = 40;

        private readonly EngineFirebase _firebase;

        public ProjectStore(EngineFirebase firebase)
        {
            _firebase = firebase;
        }

        public List<ProjectInfo> Items { get; } = new List<ProjectInfo>();

        public async Task LoadAsync()
        {
            var list = await _firebase.LoadProgressAsync(Key, new ProjectList());
            Items.Clear();
            if (list?.items != null)
            {
                Items.AddRange(list.items);
            }
        }

        public async Task<ProjectInfo> CreateAsync(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("Введіть назву проєкту.");
            }

            if (name.Length > MaxNameLength)
            {
                name = name.Substring(0, MaxNameLength);
            }

            var project = new ProjectInfo { id = Guid.NewGuid().ToString("N"), name = name, createdAt = DateTime.UtcNow.ToString("o") };
            Items.Add(project);
            try
            {
                await SaveAsync();
            }
            catch
            {
                Items.Remove(project);
                throw;
            }

            return project;
        }

        public async Task DeleteAsync(string id)
        {
            var project = Items.Find(p => p.id == id);
            if (project == null)
            {
                return;
            }

            var index = Items.IndexOf(project);
            Items.RemoveAt(index);
            try
            {
                await SaveAsync();
            }
            catch
            {
                Items.Insert(index, project);
                throw;
            }
        }

        private Task SaveAsync()
        {
            return _firebase.SaveProgressAsync(Key, new ProjectList { items = new List<ProjectInfo>(Items) });
        }
    }
}
