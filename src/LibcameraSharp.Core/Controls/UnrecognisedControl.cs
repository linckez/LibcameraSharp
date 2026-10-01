using System.Globalization;

namespace LibcameraSharp;

/// <summary>A control id with no generated key, described from the value libcamera stored. Its name is its id.</summary>
internal sealed class UnrecognisedControl(uint id, ControlType type, bool isArray)
    : ControlKey(id, id.ToString(CultureInfo.InvariantCulture), type, ControlDirection.Out, isArray, fixedLength: null, typeof(object))
{
    internal override object BoxInt32(int value) => value;
}
