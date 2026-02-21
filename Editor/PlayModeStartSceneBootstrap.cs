using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Jisetu.Editor.PlayModeStartSceneBootstrap
{
    /// <summary>
    /// Play Mode 開始シーンを管理するEditor拡張
    /// Project Settings ウィンドウに統合し、チームで設定を共有可能にする
    /// </summary>
    internal static class PlayModeStartSceneBootstrap
    {
        private const string SessionStateKey = "PlayModeStartScene_PreviousScene";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Domain Reload後も確実に設定を適用
            ApplySettings();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void ApplySettings()
        {
            var settings = PlayModeStartSceneSettings.Load();
            if (settings == null || !settings.Enabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            if(settings.StartScene == null)
            {
                foreach (var scene in EditorBuildSettings.scenes)
                {
                   if (scene.enabled)
                   {
                       EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);;
                       break;
                   }
                }
            } else {
                EditorSceneManager.playModeStartScene = settings.StartScene;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // Play前に現在のシーンパスを記憶（Play終了後に戻すため）
                    var currentScene = EditorSceneManager.GetActiveScene().path;
                    SessionState.SetString(SessionStateKey, currentScene);
                    ApplySettings();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // Play終了後に元のシーンに戻す
                    var settings = PlayModeStartSceneSettings.Load();
                    if (settings != null && settings.Enabled && settings.RestoreSceneOnExit)
                    {
                        var previousScene = SessionState.GetString(SessionStateKey, string.Empty);
                        if (!string.IsNullOrEmpty(previousScene) &&
                            previousScene != EditorSceneManager.GetActiveScene().path)
                        {
                            EditorSceneManager.OpenScene(previousScene);
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 設定データ本体。ProjectSettingsフォルダに保存してGit共有可能にする
    /// </summary>
    [FilePath("ProjectSettings/Jisetu.Editor.PlayModeStartSceneBootstrapSettings.asset",
              FilePathAttribute.Location.ProjectFolder)]
    internal class PlayModeStartSceneSettings : ScriptableSingleton<PlayModeStartSceneSettings>
    {
        [SerializeField] private bool enabled;
        [SerializeField] private SceneAsset startScene;
        [SerializeField] private bool restoreSceneOnExit = true;

        public bool Enabled => enabled;
        public SceneAsset StartScene => startScene;
        public bool RestoreSceneOnExit => restoreSceneOnExit;

        public static PlayModeStartSceneSettings Load()
        {
            return instance;
        }

        public void SetEnabled(bool value)
        {
            enabled = value;
            Save(true);
        }

        public void SetStartScene(SceneAsset scene)
        {
            startScene = scene;
            Save(true);
        }

        public void SetRestoreSceneOnExit(bool value)
        {
            restoreSceneOnExit = value;
            Save(true);
        }
    }

    /// <summary>
    /// Project Settings ウィンドウに統合する設定UI
    /// </summary>
    internal class PlayModeStartSceneSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/Play Mode Start Scene";

        private PlayModeStartSceneSettingsProvider()
            : base(SettingsPath, SettingsScope.Project) { }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new PlayModeStartSceneSettingsProvider
            {
                keywords = new[] { "play", "mode", "start", "scene", "boot", "launch" }
            };
        }

        public override void OnGUI(string searchContext)
        {
            var settings = PlayModeStartSceneSettings.Load();
            if (settings == null) return;

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawSettings(settings);
                    DrawStatus(settings);
                }
            }
        }

        private static void DrawSettings(PlayModeStartSceneSettings settings)
        {
            // 有効/無効トグル
            EditorGUI.BeginChangeCheck();
            var enabled = EditorGUILayout.Toggle("Enabled", settings.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                settings.SetEnabled(enabled);
                // 即時反映
                EditorSceneManager.playModeStartScene =
                    enabled ? settings.StartScene : null;
            }

            using (new EditorGUI.DisabledScope(!settings.Enabled))
            {
                // シーン選択
                EditorGUI.BeginChangeCheck();
                var scene = (SceneAsset)EditorGUILayout.ObjectField(
                    "Start Scene", settings.StartScene, typeof(SceneAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    settings.SetStartScene(scene);
                    if (settings.Enabled)
                    {
                        EditorSceneManager.playModeStartScene = scene;
                    }
                }

                // シーン復元オプション
                EditorGUI.BeginChangeCheck();
                var restore = EditorGUILayout.Toggle(
                    new GUIContent("Restore Scene on Exit",
                        "Play Mode終了時に元のシーンに自動で戻す"),
                    settings.RestoreSceneOnExit);
                if (EditorGUI.EndChangeCheck())
                {
                    settings.SetRestoreSceneOnExit(restore);
                }
            }
        }

        private static void DrawStatus(PlayModeStartSceneSettings settings)
        {
            EditorGUILayout.Space(10);

            // 現在の状態をわかりやすく表示
            if (!settings.Enabled)
            {
                EditorGUILayout.HelpBox(
                    "無効: 現在開いているシーンからPlay Modeが開始されます。",
                    MessageType.Info);
                return;
            }

            if (settings.StartScene == null)
            {
                EditorGUILayout.HelpBox(
                    "シーンが未設定です。Play ModeはBuild Settingsの先頭シーンから開始されます。",
                    MessageType.Warning);
                return;
            }

            // Build Settings に含まれているか検証
            var scenePath = AssetDatabase.GetAssetPath(settings.StartScene);
            var inBuildSettings = IsSceneInBuildSettings(scenePath);

            if (!inBuildSettings)
            {
                EditorGUILayout.HelpBox(
                    $"'{settings.StartScene.name}' は Build Settings に含まれていません。\n" +
                    "Play Modeでは動作しますが、ビルドには含まれません。",
                    MessageType.Warning);

                if (GUILayout.Button("Build Settings に追加", GUILayout.Width(200)))
                {
                    AddSceneToBuildSettings(scenePath);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Play Mode は '{settings.StartScene.name}' から開始されます。",
                    MessageType.Info);
            }
        }

        private static bool IsSceneInBuildSettings(string scenePath)
        {
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene.path == scenePath) return true;
            }
            return false;
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    /// <summary>
    /// ショートカットメニュー（素早いトグル操作用）
    /// </summary>
    internal static class PlayModeStartSceneMenu
    {
        private const string MenuPath = "Tools/Play From Start Scene";

        [MenuItem(MenuPath, priority = 100)]
        private static void Toggle()
        {
            var settings = PlayModeStartSceneSettings.Load();
            if (settings == null) return;

            var newValue = !settings.Enabled;
            settings.SetEnabled(newValue);

            EditorSceneManager.playModeStartScene =
                newValue ? settings.StartScene : null;

            var sceneName = settings.StartScene != null ? settings.StartScene.name : "(未設定)";
            Debug.Log(newValue
                ? $"[PlayMode] 開始シーン有効: {sceneName}"
                : "[PlayMode] 開始シーン無効: 現在のシーンから開始");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            var settings = PlayModeStartSceneSettings.Load();
            Menu.SetChecked(MenuPath, settings != null && settings.Enabled);
            return true;
        }
    }
}