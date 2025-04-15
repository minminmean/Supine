using UnityEngine;
using UnityEditor;
using System.IO;
using Supine.Utilities;
using System.Security.Policy;

namespace Supine
{
    public class SupineCombinerEditor : EditorWindow
    {
        private GameObject avatar;
        private SupineCombiner supineCombiner;

        private int _language = 0;

        private bool _canCombine = false;
        private bool _shouldInheritOriginalAnimation = true;
        private bool _disableJumpMotion = true;
        private bool _enableJumpAtDesktop = true;
        private bool _shouldCleanCombinedSupine = true;

        private int _sittingPose1 = 0;
        private int _sittingPose2 = 1;

        [MenuItem("Tools/MinMinMart/Supine Combiner")]
        private static void Create()
        {
            GetWindow<SupineCombinerEditor>("Supine Combiner");
        }

        private void OnGUI()
        {
            // 言語選択＆辞書取得
            string[] _languages = {"Japanese", "English"};
            _language = EditorGUILayout.Popup("Language", _language, _languages);
            LocalizeDictionary localizeDict = JsonHelper.GetLocalizedTexts(_language);

            EditorGUILayout.Space();

            // アバター取得
            using (new GUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                avatar = EditorGUILayout.ObjectField(localizeDict.avatar, avatar, typeof(GameObject), true) as GameObject;
                
                if (EditorGUI.EndChangeCheck())
                {
                    _canCombine = false;
                }
            }

            EditorGUILayout.Space();

            // 元の立ち、しゃがみ、伏せアニメーションを継承するか
            _shouldInheritOriginalAnimation = EditorGUILayout.ToggleLeft(localizeDict.inherit_original, _shouldInheritOriginalAnimation);
            
            // ジャンプモーションを無効にするか
            _disableJumpMotion = EditorGUILayout.ToggleLeft(localizeDict.disable_jump_motion, _disableJumpMotion);
            using (new EditorGUI.DisabledGroupScope(!_disableJumpMotion))
            {
                EditorGUI.indentLevel++;
                _enableJumpAtDesktop = EditorGUILayout.ToggleLeft(localizeDict.enable_jump_at_desktop, _enableJumpAtDesktop);
                if (!_disableJumpMotion)
                {
                    _enableJumpAtDesktop = false;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // 座り方選択
            string[] _sittingPoses =
                {
                    localizeDict.petan,
                    localizeDict.tatehiza_girl,
                    localizeDict.agura,
                    localizeDict.tatehiza_boy
                };
            _sittingPose1 = EditorGUILayout.Popup(localizeDict.sit1, _sittingPose1, _sittingPoses);
            _sittingPose2 = EditorGUILayout.Popup(localizeDict.sit2, _sittingPose2, _sittingPoses);

            EditorGUILayout.Space();

            // 結合済みのごろ寝を削除するか
            _shouldCleanCombinedSupine = EditorGUILayout.ToggleLeft(localizeDict.clean_combined_supine, _shouldCleanCombinedSupine);

            EditorGUILayout.Space();

            // Checkボタン
            using (new GUILayout.VerticalScope())
            {
                using (new EditorGUI.DisabledGroupScope(!avatar))
                {
                    if (GUILayout.Button(localizeDict.check))
                    {
                        supineCombiner = new SupineCombiner(avatar);
                        if (supineCombiner.canCombine)
                        {
                            _canCombine = true;
                            EditorUtility.DisplayDialog(localizeDict.check_successful, localizeDict.check_successful_message, "OK");
                            Debug.Log("[VRCSupine] Check OK.");
                        }
                        else
                        {
                            _canCombine = false;
                            EditorUtility.DisplayDialog(localizeDict.check_failure, localizeDict.check_failure_message, "OK");
                            Debug.Log("[VRCSupine] Check failed.");
                        }
                    }
                }

                // Prefab生成ボタン
                using (new EditorGUI.DisabledGroupScope(!_canCombine))
                {
                    if (GUILayout.Button(localizeDict.create_ma_prefab, GUILayout.Height(40), GUILayout.MinWidth(150)))
                    {
                        try
                        {
                            supineCombiner.CreateMAPrefab(
                                shouldInheritOriginalAnimation: _shouldInheritOriginalAnimation,
                                disableJumpMotion: _disableJumpMotion,
                                enableJumpAtDesktop: _enableJumpAtDesktop,
                                sittingPoseOrder1: _sittingPose1,
                                sittingPoseOrder2: _sittingPose2,
                                shouldCleanCombinedSupine: _shouldCleanCombinedSupine
                            );
                        }
                        catch (IOException e)
                        {
                            EditorUtility.DisplayDialog(localizeDict.ma_prefab_create_failure, localizeDict.ma_prefab_create_failure_message, "OK");
                            throw e;
                        }
                        EditorUtility.DisplayDialog(localizeDict.ma_prefab_created, localizeDict.ma_prefab_created_message, "OK");
                        _canCombine = false;
                    }
                }

            }
        }

        // protected virtual SupineCombiner InstantiateCombiner(GameObject avatar)
        // {
        //     return new SupineCombiner(avatar);
        // }
    }
}
