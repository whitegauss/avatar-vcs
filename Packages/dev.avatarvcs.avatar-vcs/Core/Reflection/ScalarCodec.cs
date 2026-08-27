using System;
using System.Globalization;

namespace AvatarVcs.Core.Reflection
{
    /// <summary>
    /// Comma-joined float/int list encoding shared by FieldCodec's
    /// component-wise types (color, vector*, rect*, bounds*, quaternion).
    /// </summary>
    public static class ScalarCodec
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        // Sane upper bound for an array-size write. Without this, a crafted
        // or corrupted FieldValue targeting "someArray.Array.size" (a real,
        // FindProperty-resolvable SerializedProperty path independent of
        // whether it was ever legitimately captured that way) could set
        // prop.intValue to e.g. 2,000,000,000 and have Unity attempt a huge
        // array resize, hanging or OOM-ing the Editor. No legitimate capture
        // from this tool's own ComponentCapturer produces anything remotely
        // close to this.
        public const int MaxArraySize = 100_000;

        public static bool IsAcceptableArraySize(int value) => value >= 0 && value <= MaxArraySize;

        public static string Join(params float[] values) =>
            string.Join(",", Array.ConvertAll(values, v => v.ToString("R", Culture)));

        public static float[] ParseFloats(string value) =>
            Array.ConvertAll(value.Split(','), s => float.Parse(s, Culture));

        public static int[] ParseInts(string value) =>
            Array.ConvertAll(value.Split(','), s => int.Parse(s, Culture));
    }
}
