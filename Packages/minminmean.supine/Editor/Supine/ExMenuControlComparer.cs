using System.Collections.Generic;
using ExpressionsMenuControl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

class ExMenuControlComparer : IEqualityComparer<ExpressionsMenuControl>
{
    public bool Equals(ExpressionsMenuControl x, ExpressionsMenuControl y)
    {
        return x.name == y.name && x.type == y.type && x.value == y.value;
    }

    public int GetHashCode(ExpressionsMenuControl control)
    {
        return (control.name + control.type.ToString() + control.value.ToString()).GetHashCode();
    }
}
