using System;

namespace Supine.Utilities
{
    public enum SupineLanguage
    {
        Japanese = 0,
        English  = 1
    }

    [Serializable]
    public struct LocalizeDictionary
    {
        public string check;
        public string combine_mode;
        public string combine_mode_standard;
        public string combine_mode_add;
        public string inherit_original;
        public string inherit_standing_state;
        public string inherit_crouching_state;
        public string inherit_prone_state;
        public string add_target;
        public string add_target_auto;
        public string add_target_resolved;
        public string add_target_vrc_default;
        public string entry_state;
        public string prone_state;
        public string state_none;
        public string inherit_state_help;
        public string add_state_help;
        public string add_state_conflict;
        public string add_state_already_combined;
        public string disable_jump_motion;
        public string enable_jump_at_desktop;
        public string sit1;
        public string sit2;
        public string petan;
        public string tatehiza_boy;
        public string tatehiza_girl;
        public string agura;
        public string avatar;
        public string check_successful;
        public string check_successful_message;
        public string check_successful_warning;
        public string check_successful_warning_message;
        public string check_failure;
        public string check_failure_message;
        public string check_failure_variant_message;
        public string check_failure_add_target_message;
        public string check_failure_add_layer_message;
        public string check_failure_add_entry_message;
        public string create_ma_prefab;
        public string ma_prefab_created;
        public string ma_prefab_created_message;
        public string ma_prefab_create_failure;
        public string ma_prefab_create_failure_message;
    }
}
