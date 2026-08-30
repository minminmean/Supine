using System.Collections.Generic;
using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;

namespace Supine
{
    class ExParameterComparer : IEqualityComparer<ExpressionParameter>
    {
        /// <summary>要素ごとに生成しないよう使い回す</summary>
        public static readonly ExParameterComparer Instance = new ExParameterComparer();

        public bool Equals(ExpressionParameter x, ExpressionParameter y)
        {
            return x.name == y.name && x.valueType == y.valueType;
        }

        public int GetHashCode(ExpressionParameter parameter)
        {
            return ( parameter.name + parameter.valueType.ToString()).GetHashCode();
        }
    }
}
