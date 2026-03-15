using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HowardIssue.HOWARD), "HowardSkillIssue", "1.0.0", "Nano")]
[assembly: MelonColor(127, 52, 235, 131)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace HowardIssue
{
    public class HOWARD : MelonMod
    {
        internal static HOWARD Instance { get; private set; }
        private const float ColumnHalfGap = 0.10f;

        private string _playerName = "Player";
        private int _playerDeaths;
        private int _howardDeaths;
        private GameObject _worldUiRoot;
        private GameObject _rotationPivot;
        private TextMeshPro _playerTextMesh;
        private TextMeshPro _howardTextMesh;
        private TMP_FontAsset _rumbleUiFont;
        private float _nextNameRefreshTime;
        private MelonPreferences_Category _prefsCategory;
        private MelonPreferences_Entry<string> _nameOverrideEntry;
        private PlayerManager _playerManager;
        private string _currentScene = "Loader";

        public override void OnInitializeMelon()
        {
            Instance = this;
            _prefsCategory = MelonPreferences.CreateCategory("HowardSkillIssue");
            _nameOverrideEntry = _prefsCategory.CreateEntry("DisplayNameOverride", "");
            _playerName = "Player";
            HarmonyInstance.PatchAll(typeof(HOWARD).Assembly);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _currentScene = sceneName ?? "Unknown";
            if (_worldUiRoot != null)
            {
                UnityEngine.Object.Destroy(_worldUiRoot);
                _worldUiRoot = null;
                _rotationPivot = null;
                _playerTextMesh = null;
                _howardTextMesh = null;
            }
        }

        public override void OnUpdate()
        {
            if (_currentScene == "Loader")
            {
                return;
            }

            if (Time.unscaledTime >= _nextNameRefreshTime)
            {
                _nextNameRefreshTime = Time.unscaledTime + 2f;
                RefreshPlayerNameFromGame();
            }

            if (_worldUiRoot == null)
            {
                TryCreateWorldUi();
            }

            if (_worldUiRoot == null)
            {
                return;
            }

            var cam = FindActiveCamera();
            if (cam != null)
            {
                ApplyTextFacing(cam);
            }

            UpdateWorldText();
        }

        private void TryCreateWorldUi()
        {
            var cam = FindActiveCamera();
            if (cam == null)
            {
                return;
            }

            _worldUiRoot = new GameObject("HowardDeathCounterUI");
            _worldUiRoot.hideFlags = HideFlags.HideAndDontSave;
            _worldUiRoot.transform.localScale = Vector3.one * 0.05f;
            UnityEngine.Object.DontDestroyOnLoad(_worldUiRoot);

            _rotationPivot = new GameObject("RotationPivot");
            _rotationPivot.transform.SetParent(_worldUiRoot.transform, false);
            _rotationPivot.transform.localPosition = Vector3.zero;

            _playerTextMesh = CreateColumnText("PlayerColumn", new Vector3(-ColumnHalfGap, 0f, 0f), true);
            _howardTextMesh = CreateColumnText("HowardColumn", new Vector3(ColumnHalfGap, 0f, 0f), false);

            ApplySceneAnchor(cam);
            ApplySceneColumnPositions();
            UpdateWorldText();
            MelonLogger.Msg($"[HowardSkillIssue] UI created for scene '{_currentScene}'.");
        }

        private TextMeshPro CreateColumnText(string name, Vector3 localOffset, bool isLeftColumn)
        {
            var go = new GameObject(name);
            go.name = name;
            go.transform.SetParent(_worldUiRoot.transform, false);
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);

            Il2CppSystem.Type tmpType = null;
            var tmpTypeNames = new[]
            {
                "TMPro.TextMeshPro, Unity.TextMeshPro",
                "TMPro.TextMeshPro, UnityEngine.TextCoreTextEngineModule",
                "TextMeshPro, Unity.TextMeshPro"
            };
            foreach (var typeName in tmpTypeNames)
            {
                tmpType = Il2CppSystem.Type.GetType(typeName);
                if (tmpType != null)
                {
                    MelonLogger.Msg($"[HowardSkillIssue] Using TMP type '{typeName}' for '{name}'.");
                    break;
                }
            }

            if (tmpType == null)
            {
                MelonLogger.Warning($"[HowardSkillIssue] Could not find TextMeshPro runtime type for '{name}'.");
                return null;
            }

            var component = go.AddComponent(tmpType);
            var textMesh = component != null ? component.TryCast<TextMeshPro>() : null;
            if (textMesh == null)
            {
                MelonLogger.Warning($"[HowardSkillIssue] Failed to get TextMeshPro for '{name}'.");
                return null;
            }

            // Center alignment so each text rotates around its own center.
            ((TMP_Text)textMesh).alignment = (TextAlignmentOptions)514;
            ((TMP_Text)textMesh).enableWordWrapping = false;
            ((TMP_Text)textMesh).fontSize = 5f;
            var rumbleFont = ResolveRumbleUiFont();
            if (rumbleFont != null)
            {
                ((TMP_Text)textMesh).font = rumbleFont;
            }
            ((TMP_Text)textMesh).color = Color.white;
            ((TMP_Text)textMesh).SetOutlineColor(new Color32(0, 0, 0, byte.MaxValue));
            ((TMP_Text)textMesh).outlineWidth = 0.2f;

            MelonLogger.Msg($"[HowardSkillIssue] Created text object '{name}' at local {localOffset}.");

            return textMesh;
        }

        private TMP_FontAsset ResolveRumbleUiFont()
        {
            if (_rumbleUiFont != null)
            {
                return _rumbleUiFont;
            }

            try
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts == null || fonts.Length == 0)
                {
                    return null;
                }

                foreach (var font in fonts)
                {
                    if (font == null || string.IsNullOrWhiteSpace(font.name))
                    {
                        continue;
                    }

                    var name = font.name;
                    if (name.IndexOf("TMP_GoodDogPlain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("GoodDogPlain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Good Dog Plain", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _rumbleUiFont = font;
                        MelonLogger.Msg($"[HowardSkillIssue] Using RUMBLE UI font '{name}'.");
                        return _rumbleUiFont;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private void ApplySceneAnchor(Camera cam)
        {
            if (_worldUiRoot == null || cam == null)
            {
                return;
            }

            Vector3 position;
            Vector3 scale;

            if (_currentScene == "Gym")
            {
                // Fixed gym anchor (requested): same style as world text mods.
                position = new Vector3(8.858f, 2.7522f, -24.1645f);
                scale = new Vector3(1f, 1f, 1.318f);
            }
            else if (_currentScene == "Map0")
            {
                position = new Vector3(17.951f, 2.15f, 3.2618f);
                scale = Vector3.one * 0.1f;
            }
            else if (_currentScene == "Map1")
            {
                position = new Vector3(2.8163f, 6.1f, 10.7819f);
                scale = Vector3.one * 0.1f;
            }
            else
            {
                var camTransform = cam.transform;
                position = camTransform.position + camTransform.forward * 2.0f + camTransform.up * 0.2f;
                scale = Vector3.one * 0.06f;
            }

            _worldUiRoot.transform.position = position;
            _worldUiRoot.transform.localScale = scale;
            var faceCamera = Quaternion.LookRotation(cam.transform.position - _worldUiRoot.transform.position, Vector3.up);
            _worldUiRoot.transform.rotation = faceCamera * Quaternion.Euler(0f, 180f, 0f);
        }

        private void ApplySceneColumnPositions()
        {
            if (_playerTextMesh == null || _howardTextMesh == null)
            {
                return;
            }

            if (_currentScene == "Gym")
            {
                // Keep exact world positions from UnityExplorer.
                _playerTextMesh.transform.position = new Vector3(5.7545f, 1.6522f, -24.1384f);
                _howardTextMesh.transform.position = new Vector3(13.3516f, 1.6522f, -20.4851f);
            }

            UpdateRotationPivotPosition();
        }

        private void ApplyTextFacing(Camera cam)
        {
            if (cam == null)
            {
                return;
            }

            UpdateRotationPivotPosition();
            if (_rotationPivot == null)
            {
                return;
            }

            var sharedFacing = Quaternion.LookRotation(cam.transform.position - _rotationPivot.transform.position, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);

            if (_playerTextMesh != null)
            {
                _playerTextMesh.transform.rotation = sharedFacing;
            }

            if (_howardTextMesh != null)
            {
                _howardTextMesh.transform.rotation = sharedFacing;
            }
        }

        private void UpdateRotationPivotPosition()
        {
            if (_rotationPivot == null || _playerTextMesh == null || _howardTextMesh == null)
            {
                return;
            }

            _rotationPivot.transform.position = (_playerTextMesh.transform.position + _howardTextMesh.transform.position) * 0.5f;
        }

        private void UpdateWorldText()
        {
            if (_playerTextMesh != null)
            {
                ((TMP_Text)_playerTextMesh).text = $"{_playerName} Deaths\n{_playerDeaths}";
            }

            if (_howardTextMesh != null)
            {
                ((TMP_Text)_howardTextMesh).text = $"Howard Deaths\n{_howardDeaths}";
            }
        }

        private void RefreshPlayerNameFromGame()
        {
            var overrideName = _nameOverrideEntry?.Value;
            if (IsPlausiblePlayerName(overrideName))
            {
                _playerName = overrideName.Trim();
                return;
            }

            var managerName = TryReadNameFromPlayerManager();
            if (IsPlausiblePlayerName(managerName))
            {
                _playerName = managerName;
                return;
            }

            var textName = TryReadNameFromTextMeshes();
            if (IsPlausiblePlayerName(textName))
            {
                _playerName = textName;
            }
        }

        private string TryReadNameFromPlayerManager()
        {
            try
            {
                if (_playerManager == null)
                {
                    var managerObject = GameObject.Find("Game Instance/Initializable/PlayerManager");
                    _playerManager = managerObject != null ? managerObject.GetComponent<PlayerManager>() : null;
                    if (_playerManager == null)
                    {
                        return null;
                    }
                }

                var players = _playerManager.AllPlayers;
                if (players == null || players.Count <= 0)
                {
                    return null;
                }

                // Gym/solo usually has one player, and index 0 is our local player.
                if (players.Count == 1)
                {
                    return players[0]?.Data?.GeneralData?.PublicUsername;
                }

                // In multiplayer, prefer first valid public username as a safe fallback.
                for (var i = 0; i < players.Count; i++)
                {
                    Player player = players[i];
                    var name = player?.Data?.GeneralData?.PublicUsername;
                    if (IsPlausiblePlayerName(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static Camera FindActiveCamera()
        {
            if (Camera.main != null)
            {
                return Camera.main;
            }

            var anyCamera = UnityEngine.Object.FindObjectsOfType<Camera>()
                .FirstOrDefault(c => c != null && c.enabled && c.gameObject.activeInHierarchy);
            return anyCamera;
        }

        private static string TryReadNameFromTextMeshes()
        {
            try
            {
                var candidates = Resources.FindObjectsOfTypeAll<TextMesh>();
                if (candidates == null || candidates.Length == 0)
                {
                    return null;
                }

                var best = candidates
                    .Where(t => t != null && IsPlausiblePlayerName(t.text))
                    .OrderByDescending(t => t.fontSize)
                    .ThenByDescending(t => t.characterSize)
                    .FirstOrDefault();

                return best?.text?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPlausiblePlayerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.Length < 2 || trimmed.Length > 24)
            {
                return false;
            }

            if (trimmed.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (trimmed.IndexOf("howard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            var hasLetterOrDigit = false;
            foreach (var c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                {
                    hasLetterOrDigit = true;
                    continue;
                }

                if (c == '_' || c == '-' || c == '.')
                {
                    continue;
                }

                return false;
            }

            return hasLetterOrDigit;
        }

        private void OnPlayerDeathDetected()
        {
            _playerDeaths++;
            UpdateWorldText();
            MelonLogger.Msg($"[HowardSkillIssue] Player death detected. Total: {_playerDeaths}");
        }

        private void OnHowardDeathDetected()
        {
            _howardDeaths++;
            UpdateWorldText();
            MelonLogger.Msg($"[HowardSkillIssue] Howard death detected. Total: {_howardDeaths}");
        }

        [HarmonyPatch]
        private static class HowardSpeedrunPlayerDeathBridge
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method("HowardSpeedrun.main+Patch0:Postfix");
            }

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static void Postfix(int _)
            {
                Instance?.OnPlayerDeathDetected();
            }
        }

        [HarmonyPatch]
        private static class HowardSpeedrunHowardDeathBridge
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method("HowardSpeedrun.main+Patch1:Postfix");
            }

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static void Postfix()
            {
                Instance?.OnHowardDeathDetected();
            }
        }
    }


}
