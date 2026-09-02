using System;
using System.Globalization;
using AvatarVcs.Core.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Reflection
{
    /// <summary>
    /// Encodes/decodes SerializedProperty leaf values to/from strings for JSON
    /// storage. Types not listed here (ManagedReference, ExposedReference,
    /// ...) are intentionally unsupported for now (v1 design doc section 10:
    /// add type handlers incrementally, don't front-load all of them).
    /// </summary>
    public static class FieldCodec
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static bool TryEncode(SerializedProperty prop, out string value, out string type)
        {
            switch (prop.propertyType)
            {
                // Integer covers both int and 64-bit (long/ulong) fields;
                // .intValue silently truncates a long to 32 bits, so the
                // underlying field type (prop.type) decides which accessor
                // to use. ulong's bit pattern round-trips through .longValue
                // like any other 64-bit storage; only the string
                // representation needs to reinterpret it as unsigned.
                case SerializedPropertyType.Integer when prop.type == "ulong":
                    value = unchecked((ulong)prop.longValue).ToString(Culture);
                    type = "ulong";
                    return true;
                case SerializedPropertyType.Integer when prop.type == "long":
                    value = prop.longValue.ToString(Culture);
                    type = "long";
                    return true;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.ArraySize:
                    value = prop.intValue.ToString(Culture);
                    type = "int";
                    return true;
                case SerializedPropertyType.Boolean:
                    value = prop.boolValue ? "true" : "false";
                    type = "bool";
                    return true;
                case SerializedPropertyType.Float:
                    value = prop.floatValue.ToString("R", Culture);
                    type = "float";
                    return true;
                case SerializedPropertyType.String:
                    value = prop.stringValue;
                    type = "string";
                    return true;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    value = ScalarCodec.Join(c.r, c.g, c.b, c.a);
                    type = "color";
                    return true;
                case SerializedPropertyType.LayerMask:
                    value = prop.intValue.ToString(Culture);
                    type = "layerMask";
                    return true;
                case SerializedPropertyType.Enum:
                    value = prop.intValue.ToString(Culture);
                    type = "enum";
                    return true;
                case SerializedPropertyType.Character:
                    value = prop.intValue.ToString(Culture);
                    type = "character";
                    return true;
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    value = ScalarCodec.Join(v2.x, v2.y);
                    type = "vector2";
                    return true;
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    value = ScalarCodec.Join(v3.x, v3.y, v3.z);
                    type = "vector3";
                    return true;
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    value = ScalarCodec.Join(v4.x, v4.y, v4.z, v4.w);
                    type = "vector4";
                    return true;
                case SerializedPropertyType.Vector2Int:
                    var v2i = prop.vector2IntValue;
                    value = $"{v2i.x},{v2i.y}";
                    type = "vector2Int";
                    return true;
                case SerializedPropertyType.Vector3Int:
                    var v3i = prop.vector3IntValue;
                    value = $"{v3i.x},{v3i.y},{v3i.z}";
                    type = "vector3Int";
                    return true;
                case SerializedPropertyType.Rect:
                    var r = prop.rectValue;
                    value = ScalarCodec.Join(r.x, r.y, r.width, r.height);
                    type = "rect";
                    return true;
                case SerializedPropertyType.RectInt:
                    var ri = prop.rectIntValue;
                    value = $"{ri.x},{ri.y},{ri.width},{ri.height}";
                    type = "rectInt";
                    return true;
                case SerializedPropertyType.Bounds:
                    var b = prop.boundsValue;
                    value = ScalarCodec.Join(b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z);
                    type = "bounds";
                    return true;
                case SerializedPropertyType.BoundsInt:
                    var bi = prop.boundsIntValue;
                    value = $"{bi.position.x},{bi.position.y},{bi.position.z},{bi.size.x},{bi.size.y},{bi.size.z}";
                    type = "boundsInt";
                    return true;
                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue;
                    value = ScalarCodec.Join(q.x, q.y, q.z, q.w);
                    type = "quaternion";
                    return true;
                case SerializedPropertyType.AnimationCurve:
                    value = AnimationCurveCodec.Encode(prop.animationCurveValue);
                    type = "animationCurve";
                    return true;
                case SerializedPropertyType.Gradient:
                    value = GradientCodec.Encode(prop.gradientValue);
                    type = "gradient";
                    return true;
                default:
                    value = null;
                    type = null;
                    return false;
            }
        }

        /// <summary>
        /// Never throws, regardless of how malformed value is (truncated,
        /// wrong component count, non-numeric, ...) -- value ultimately comes
        /// from commit JSON on disk, which can be corrupted (crash mid-write,
        /// bad merge) independent of any deliberate tampering. Callers rely
        /// on a false return to warn-and-skip a field rather than aborting
        /// whatever destructive operation (e.g. checkout) is already
        /// underway.
        /// </summary>
        public static bool TryDecode(SerializedProperty prop, string type, string value)
        {
            try
            {
                return TryDecodeCore(prop, type, value);
            }
            catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or ArgumentNullException or NullReferenceException)
            {
                return false;
            }
        }

        private static bool TryDecodeCore(SerializedProperty prop, string type, string value)
        {
            // JsonUtility leaves a FieldValue.value that had no key in the JSON
            // as null rather than "" (the repo notes this in
            // BlendShapePresetIO.cs). "int"/"float"/... funnel that into
            // int.Parse(null) -> ArgumentNullException (already caught), but
            // the component-wise types ("vector3", "color", "gradient", ...)
            // reach value.Split(...) first and NRE instead. Reject null up
            // front for every type so the caller warn-and-skips this one
            // field rather than the exception aborting an in-flight checkout.
            if (value == null)
                return false;

            switch (type)
            {
                case "int":
                case "layerMask":
                case "enum":
                case "character":
                {
                    var intValue = int.Parse(value, Culture);
                    if (prop.propertyType == SerializedPropertyType.ArraySize && !ScalarCodec.IsAcceptableArraySize(intValue))
                        return false;
                    prop.intValue = intValue;
                    return true;
                }
                case "long":
                    prop.longValue = long.Parse(value, Culture);
                    return true;
                case "ulong":
                    prop.longValue = unchecked((long)ulong.Parse(value, Culture));
                    return true;
                case "bool":
                    prop.boolValue = value == "true";
                    return true;
                case "float":
                    prop.floatValue = float.Parse(value, Culture);
                    return true;
                case "string":
                    prop.stringValue = value;
                    return true;
                case "color":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.colorValue = new Color(p[0], p[1], p[2], p[3]);
                    return true;
                }
                case "vector2":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.vector2Value = new Vector2(p[0], p[1]);
                    return true;
                }
                case "vector3":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.vector3Value = new Vector3(p[0], p[1], p[2]);
                    return true;
                }
                case "vector4":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.vector4Value = new Vector4(p[0], p[1], p[2], p[3]);
                    return true;
                }
                case "vector2Int":
                {
                    var p = ScalarCodec.ParseInts(value);
                    prop.vector2IntValue = new Vector2Int(p[0], p[1]);
                    return true;
                }
                case "vector3Int":
                {
                    var p = ScalarCodec.ParseInts(value);
                    prop.vector3IntValue = new Vector3Int(p[0], p[1], p[2]);
                    return true;
                }
                case "rect":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.rectValue = new Rect(p[0], p[1], p[2], p[3]);
                    return true;
                }
                case "rectInt":
                {
                    var p = ScalarCodec.ParseInts(value);
                    prop.rectIntValue = new RectInt(p[0], p[1], p[2], p[3]);
                    return true;
                }
                case "bounds":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.boundsValue = new Bounds(new Vector3(p[0], p[1], p[2]), new Vector3(p[3], p[4], p[5]));
                    return true;
                }
                case "boundsInt":
                {
                    var p = ScalarCodec.ParseInts(value);
                    prop.boundsIntValue = new BoundsInt(new Vector3Int(p[0], p[1], p[2]), new Vector3Int(p[3], p[4], p[5]));
                    return true;
                }
                case "quaternion":
                {
                    var p = ScalarCodec.ParseFloats(value);
                    prop.quaternionValue = new Quaternion(p[0], p[1], p[2], p[3]);
                    return true;
                }
                case "animationCurve":
                    prop.animationCurveValue = AnimationCurveCodec.Decode(value);
                    return true;
                case "gradient":
                    prop.gradientValue = GradientCodec.Decode(value);
                    return true;
                default:
                    return false;
            }
        }
    }
}
