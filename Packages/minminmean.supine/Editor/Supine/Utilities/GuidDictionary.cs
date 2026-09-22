using System;
using UnityEditor;
using UnityEditor.Animations;

namespace Supine.Utilities
{
    /// <summary>
    /// ごろ寝システムの guids.json のスキーマ。
    /// </summary>
    [Serializable]
    public struct GuidDictionary
    {
        public SupineTemplate template;
        public Animations animations;
        public Crouch crouch;
        public VRChat vrchat;

        /// <summary>
        /// VRChat SDKが持つアセットのGUID。
        /// </summary>
        [Serializable]
        public struct VRChat
        {
            public string default_locomotion;
        }

        /// <summary>
        /// しゃがみポーズ切り替えのブレンドツリー。
        /// ポーズを足すときはこのツリーに子を足すだけでよく、
        /// メニューの項目はツリーの子から生成されるため二重管理にならない。
        /// </summary>
        [Serializable]
        public struct Crouch
        {
            public string pose_tree;
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
    /// 組込時にコピーして使うMA Prefabとコントローラのテンプレート。
    /// バージョン文字列はパッケージの package.json から取得するため、ここには含めない。
    /// </summary>
    [Serializable]
    public struct SupineTemplate
    {
        public string prefab;
        public string controller;

        public bool IsValid => !string.IsNullOrEmpty(prefab) && !string.IsNullOrEmpty(controller);

        /// <summary>
        /// テンプレートのコントローラを読む。読めなければ null。
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
