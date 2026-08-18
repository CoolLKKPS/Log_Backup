using LethalCompanyInputUtils.Api;
using UnityEngine.InputSystem;

namespace Log_Backup
{
    public class LogBackupInputs : LcInputActions
    {
        [InputAction("<Keyboard>/f9", Name = "Log Backup Marker", ActionId = "log_backup_marker")]
        public InputAction MarkerKey { get; set; }
    }
}
