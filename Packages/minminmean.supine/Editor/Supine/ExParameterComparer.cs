using System.Collections.Generic;
using ExpressionParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;

namespace Supine
{
    class ExParameterComparer : IEqualityComparer<ExpressionParameter>
    {
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
