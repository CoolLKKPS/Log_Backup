using BepInEx;
using BepInEx.Logging;
using System;
using System.IO;
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

            _playerLogPath = Application.consoleLogPath;
            _playerPrevLogPath = Path.Combine(Path.GetDirectoryName(_playerLogPath), "Player-prev.log");
            _logOutputPath = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");

            _backupFolder = Path.Combine(Paths.BepInExRootPath, "Log_Backup", DateTime.Now.ToString("MM-dd-yyyy_HH-mm-ss"));
            TryBackupPrevLog();
            SetupMarkerInput();
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
            Debug.Log("[Log_Backup] Stack trace:\n" + Environment.StackTrace);

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
    }
}
