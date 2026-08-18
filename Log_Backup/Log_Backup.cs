using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace Log_Backup
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Log_BackupPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Instance = this;
            logger = base.Logger;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        public const string PLUGIN_GUID = "Log_Backup";
        public const string PLUGIN_NAME = "Log_Backup";
        public const string PLUGIN_VERSION = "1.0.0";
        public const string PLUGIN_VERSION_FULL = PLUGIN_VERSION + ".0";

        public static ManualLogSource logger;
        public static Log_BackupPlugin Instance;
    }
}
