using System;
using System.Globalization;
using UnityEngine;

namespace AvatarVcs.Core.Reflection
{
    /// <summary>
    /// Encodes/decodes an AnimationCurve to/from the string format FieldCodec
    /// stores in commit JSON.
    /// </summary>
    public static class AnimationCurveCodec
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>
        /// "{preWrapMode},{postWrapMode}|{key1};{key2};..." where each key is
        /// "time,value,inTangent,outTangent,inWeight,outWeight,weightedMode".
        /// </summary>
        public static string Encode(AnimationCurve curve)
        {
            var keys = curve.keys;
            var keyParts = new string[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                keyParts[i] = string.Join(",", new[]
                {
                    k.time.ToString("R", Culture),
                    k.value.ToString("R", Culture),
                    k.inTangent.ToString("R", Culture),
                    k.outTangent.ToString("R", Culture),
                    k.inWeight.ToString("R", Culture),
                    k.outWeight.ToString("R", Culture),
                    ((int)k.weightedMode).ToString(Culture),
                });
            }

            return $"{(int)curve.preWrapMode},{(int)curve.postWrapMode}|{string.Join(";", keyParts)}";
        }

        public static AnimationCurve Decode(string value)
        {
            var parts = value.Split('|');
            var wrapModes = parts[0].Split(',');
            var preWrapMode = (WrapMode)int.Parse(wrapModes[0], Culture);
            var postWrapMode = (WrapMode)int.Parse(wrapModes[1], Culture);

            var keyParts = parts.Length > 1 && parts[1].Length > 0
                ? parts[1].Split(';')
                : Array.Empty<string>();
            var keys = new Keyframe[keyParts.Length];
            for (var i = 0; i < keyParts.Length; i++)
            {
                var f = keyParts[i].Split(',');
                keys[i] = new Keyframe(
                    float.Parse(f[0], Culture),
                    float.Parse(f[1], Culture),
                    float.Parse(f[2], Culture),
                    float.Parse(f[3], Culture),
                    float.Parse(f[4], Culture),
                    float.Parse(f[5], Culture))
                {
                    weightedMode = (WeightedMode)int.Parse(f[6], Culture),
                };
            }

            return new AnimationCurve(keys) { preWrapMode = preWrapMode, postWrapMode = postWrapMode };
        }
    }
}
