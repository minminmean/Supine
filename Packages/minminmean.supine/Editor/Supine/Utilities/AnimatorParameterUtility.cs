using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace Supine.Utilities
{
    /// <summary>
    /// AnimatorControllerのパラメータを扱うユーティリティ
    /// </summary>
    internal static class AnimatorParameterUtility
    {
        /// <summary>
        /// sourceのパラメータのうち、destinationに無いものだけを追加する。
        /// 同名で型が違うものは既存側を尊重し、追加せずに警告を積む。
        /// </summary>
        public static void MergeParameters(
            AnimatorController destination, AnimatorController source, List<string> warnings)
        {
            Dictionary<string, AnimatorControllerParameter> existing =
                new Dictionary<string, AnimatorControllerParameter>();
            foreach (AnimatorControllerParameter parameter in destination.parameters)
            {
                existing[parameter.name] = parameter;
            }

            List<AnimatorControllerParameter> merged =
                new List<AnimatorControllerParameter>(destination.parameters);
            bool changed = false;

            foreach (AnimatorControllerParameter parameter in source.parameters)
            {
                if (existing.TryGetValue(parameter.name, out AnimatorControllerParameter current))
                {
                    if (current.type != parameter.type)
                    {
                        warnings.Add(
                            "The parameter '" + parameter.name + "' already exists as " + current.type +
                            " but Supine expects " + parameter.type +
                            ". Kept the existing one, which may break the Supine behaviour.");
                    }
                    continue;
                }

                // AnimatorControllerParameterは参照型なので、複製元と共有しないよう作り直す
                merged.Add(new AnimatorControllerParameter
                    {
                        name         = parameter.name,
                        type         = parameter.type,
                        defaultBool  = parameter.defaultBool,
                        defaultFloat = parameter.defaultFloat,
                        defaultInt   = parameter.defaultInt
                    });
                existing[parameter.name] = parameter;
                changed = true;
            }

            if (changed)
            {
                destination.parameters = merged.ToArray();
            }
        }

        /// <summary>名前が一致するパラメータの既定値を書き換える</summary>
        /// <returns>該当するパラメータがあったか</returns>
        public static bool SetDefaultBool(AnimatorController controller, string name, bool value)
        {
            return SetDefault(controller, name, parameter => parameter.defaultBool = value);
        }

        /// <inheritdoc cref="SetDefaultBool"/>
        public static bool SetDefaultFloat(AnimatorController controller, string name, float value)
        {
            return SetDefault(controller, name, parameter => parameter.defaultFloat = value);
        }

        private static bool SetDefault(
            AnimatorController controller, string name, Action<AnimatorControllerParameter> apply)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            bool found = false;

            foreach (AnimatorControllerParameter parameter in parameters)
            {
                if (parameter.name != name) continue;

                apply(parameter);
                found = true;
            }

            // 配列は複製が返るため、書き戻さないと反映されない
            if (found) controller.parameters = parameters;
            return found;
        }
    }
}
