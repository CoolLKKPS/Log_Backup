using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Log_Backup
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency("com.rune580.LethalCompanyInputUtils", BepInDependency.DependencyFlags.SoftDependency)]
    public class Log_BackupPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "Log_Backup";
        public const string PLUGIN_NAME = "Log_Backup";
        public const string PLUGIN_VERSION = "1.0.0";
        public const string PLUGIN_VERSION_FULL = PLUGIN_VERSION + ".0";
        private const string MarkerToken = "Log_Backup::MARKER";
        private const string CrashMarker = "Crash!!!";
        private const string InputUtilsGuid = "com.rune580.LethalCompanyInputUtils";
        public static ManualLogSource logger;
        public static Log_BackupPlugin Instance;
        private bool _markerPressed;
        private bool _quitHandled;
        private bool _inputUtilsReady;
        private Key _markerFallbackKey;
        private ConfigEntry<bool> _runningCode;
        private string _backupFolder;
        private string _playerLogPath;
        private string _playerPrevLogPath;
        private string _logOutputPath;
        private LogBackupInputs _inputs;

        private void Awake()
        {
            Instance = this;
            logger = base.Logger;
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            _markerFallbackKey = Config.Bind("Log_Backup", "MarkerFallbackKey", Key.F9, "Fallback key used when InputUtils is not loaded.").Value;

            _runningCode = Config.Bind("Log_Backup", "RunningCode", false, "When enabled, records which Update/LateUpdate/FixedUpdate methods ran in the last 5 seconds and prints them when the marker key is pressed. This requires patching those methods.");

            _playerLogPath = Application.consoleLogPath;
            _playerPrevLogPath = Path.Combine(Path.GetDirectoryName(_playerLogPath), "Player-prev.log");
            _logOutputPath = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");

            _backupFolder = Path.Combine(Paths.BepInExRootPath, "Log_Backup", DateTime.Now.ToString("MM-dd-yyyy_HH-mm-ss"));
            TryBackupPrevLog();
            SetupMarkerInput();
            if (_runningCode.Value)
            {
                try { RunningCode.Apply(new Harmony(PLUGIN_GUID)); }
                catch { }
            }
        }

        private void Update()
        {
            if (_inputUtilsReady || Keyboard.current == null)
                return;
            if (Keyboard.current[_markerFallbackKey].wasPressedThisFrame)
                OnMarkerPerformed(default);
        }

        private void OnMarkerPerformed(InputAction.CallbackContext context)
        {
            _markerPressed = true;
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Debug.Log($"[Log_Backup] {MarkerToken} {time}");
            if (_runningCode.Value) RunningCode.Pending = true;

            try
            {
                if (HUDManager.Instance != null)
                    HUDManager.Instance.DisplayTip("Log_Backup", $"Marker logged at {time}", false, false, "Log_Backup_Tip");
            }
            catch
            {
            }
        }

        private void OnApplicationQuit()
        {
            if (_quitHandled || !_markerPressed)
                return;
            _quitHandled = true;
            CopyLog(_playerLogPath, "Player");
            CopyLog(_logOutputPath, "LogOutput");
        }

        private void CopyLog(string source, string tag)
        {
            if (!File.Exists(source))
                return;
            EnsureBackupFolder();
            var dest = Path.Combine(_backupFolder, $"{tag}.log");
            try
            {
                File.Copy(source, dest, true);
                logger.LogInfo($"[Log_Backup] backed up {dest}");
            }
            catch (Exception e)
            {
                logger.LogError($"[Log_Backup] {e}");
            }
        }

        private void TryBackupPrevLog()
        {
            if (!File.Exists(_playerPrevLogPath))
                return;

            string prevLog;
            try
            {
                prevLog = File.ReadAllText(_playerPrevLogPath);
            }
            catch (Exception e)
            {
                logger.LogError($"[Log_Backup] {e}");
                return;
            }

            if (!prevLog.Contains(CrashMarker) && !prevLog.Contains(MarkerToken))
                return;

            EnsureBackupFolder();
            var dest = Path.Combine(_backupFolder, "Player-prev.log");
            try
            {
                File.Copy(_playerPrevLogPath, dest, true);
                logger.LogInfo($"[Log_Backup] backed up {dest}");
            }
            catch (Exception e)
            {
                logger.LogError($"[Log_Backup] {e}");
            }
        }

        private void EnsureBackupFolder()
        {
            if (!Directory.Exists(_backupFolder))
                Directory.CreateDirectory(_backupFolder);
        }

        private void SetupMarkerInput()
        {
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(InputUtilsGuid))
            {
                try
                {
                    _inputs = new LogBackupInputs();
                    _inputs.MarkerKey.performed += OnMarkerPerformed;
                    _inputUtilsReady = true;
                }
                catch
                {
                    _inputUtilsReady = false;
                }
            }
        }

        private static class RunningCode
        {
            private static readonly Dictionary<string, float> Seen = new Dictionary<string, float>();
            private static readonly Dictionary<string, MethodBase> Methods = new Dictionary<string, MethodBase>();
            private static readonly HashSet<MethodBase> ThisFrame = new HashSet<MethodBase>();
            private static int _frame = -1;
            public static bool Pending;

            public static void Apply(Harmony harmony)
            {
                var postfix = new HarmonyMethod(AccessTools.Method(typeof(RunningCode), nameof(Capture)));
                foreach (var type in AccessTools.AllTypes())
                {
                    // Skip abstract/interface/generic types (no patchable method body) and our own plugin (self-noise).
                    if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || type == typeof(Log_BackupPlugin))
                        continue;
                    // Unity only invokes Update/LateUpdate/FixedUpdate on MonoBehaviours.
                    if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                        continue;
                    foreach (var m in AccessTools.GetDeclaredMethods(type))
                    {
                        // Only non-static lifecycle callbacks are what we watch.
                        if ((m.Name != "Update" && m.Name != "LateUpdate" && m.Name != "FixedUpdate") || m.IsStatic)
                            continue;
                        try { harmony.Patch(m, postfix: postfix); }
                        catch { }
                    }
                }
            }

            public static void Capture(MethodBase __originalMethod)
            {
                try
                {
                    if (Time.frameCount != _frame)
                    {
                        _frame = Time.frameCount;
                        ThisFrame.Clear();
                    }
                    if (ThisFrame.Add(__originalMethod))
                    {
                        var name = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
                        Seen[name] = Time.time;
                        Methods[name] = __originalMethod;
                    }

                    if (!Pending)
                        return;
                    Pending = false;

                    var cutoff = Time.time - 5f;
                    var lines = Seen.Where(kv => kv.Value >= cutoff)
                                    .OrderByDescending(kv => kv.Value)
                                    .Select(kv => "  " + (IsModPatched(Methods[kv.Key]) ? "* " : "") + kv.Key);
                    Debug.Log("[Log_Backup] Running in the last 5 seconds:\n" + string.Join("\n", lines));
                }
                catch { }
            }

            private static bool IsModPatched(MethodBase m)
            {
                try
                {
                    var info = Harmony.GetPatchInfo(m);
                    if (info == null)
                        return false;
                    return info.Prefixes.Any(p => p.owner != PLUGIN_GUID)
                        || info.Postfixes.Any(p => p.owner != PLUGIN_GUID)
                        || info.Transpilers.Any(p => p.owner != PLUGIN_GUID)
                        || info.Finalizers.Any(p => p.owner != PLUGIN_GUID);
                }
                catch { return false; }
            }
        }
    }
}
