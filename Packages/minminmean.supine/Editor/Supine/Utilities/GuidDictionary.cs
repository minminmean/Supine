using System;
using UnityEditor;
using UnityEditor.Animations;

namespace Supine.Utilities
{
    /// <summary>
    /// 各パッケージが持つ guids.json のスキーマ。
    /// バリアント（通常版 / EX版）はそれぞれ自分のパッケージ内の guids.json に
    /// variant ノードを持ち、そこから自分用のPrefabとコントローラを指す。
    /// JsonUtilityは欠けたフィールドを既定値にするため、
    /// EX版のようにvariantしか持たないJSONも同じ構造体で読める。
    /// </summary>
    [Serializable]
    public struct GuidDictionary
    {
        public SupineVariant variant;
        public Animations animations;
        public VRChat vrchat;

        /// <summary>
        /// VRChat SDKが持つアセットのGUID。
        /// バリアント間で共通のため、ごろ寝システム本体の guids.json だけが持つ。
        /// </summary>
        [Serializable]
        public struct VRChat
        {
            public string default_locomotion;
        }

        [Serializable]
        public struct Animations
        {
            public Sitting sitting;

            [Serializable]
            public struct Sitting
            {
                public string petan;
                public string tatehiza_girl;
                public string agura;
                public string tatehiza_boy;
            }
        }
    }

    /// <summary>
    /// ごろ寝システム1バリアントを構成するアセットのGUID。
    /// バージョン文字列はパッケージの package.json から取得するため、ここには含めない。
    /// </summary>
    [Serializable]
    public struct SupineVariant
    {
        public string prefab;
        public string controller;

        public bool IsValid => !string.IsNullOrEmpty(prefab) && !string.IsNullOrEmpty(controller);

        /// <summary>
        /// このバリアントのテンプレートコントローラを読む。読めなければ null。
        /// 読むだけで、このアセットを書き換えてはいけない。
        /// </summary>
        public AnimatorController LoadController()
        {
            string path = AssetDatabase.GUIDToAssetPath(controller);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }
    }
}
