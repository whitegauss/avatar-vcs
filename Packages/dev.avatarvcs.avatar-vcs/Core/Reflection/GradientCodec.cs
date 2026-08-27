using System;
using System.Globalization;
using UnityEngine;

namespace AvatarVcs.Core.Reflection
{
    /// <summary>
    /// Encodes/decodes a Gradient to/from the string format FieldCodec
    /// stores in commit JSON.
    /// </summary>
    public static class GradientCodec
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>
        /// "{mode}|{colorKey1};{colorKey2};...|{alphaKey1};{alphaKey2};..."
        /// where a color key is "r,g,b,time" and an alpha key is "alpha,time".
        /// Alpha is carried separately (GradientAlphaKey), not through a
        /// color key's own alpha component, which Unity ignores.
        /// </summary>
        public static string Encode(Gradient gradient)
        {
            var colorParts = new string[gradient.colorKeys.Length];
            for (var i = 0; i < gradient.colorKeys.Length; i++)
            {
                var k = gradient.colorKeys[i];
                colorParts[i] = string.Join(",", new[]
                {
                    k.color.r.ToString("R", Culture), k.color.g.ToString("R", Culture),
                    k.color.b.ToString("R", Culture), k.time.ToString("R", Culture),
                });
            }

            var alphaParts = new string[gradient.alphaKeys.Length];
            for (var i = 0; i < gradient.alphaKeys.Length; i++)
            {
                var k = gradient.alphaKeys[i];
                alphaParts[i] = $"{k.alpha.ToString("R", Culture)},{k.time.ToString("R", Culture)}";
            }

            return $"{(int)gradient.mode}|{string.Join(";", colorParts)}|{string.Join(";", alphaParts)}";
        }

        public static Gradient Decode(string value)
        {
            var parts = value.Split('|');
            var mode = (GradientMode)int.Parse(parts[0], Culture);

            var colorKeyParts = parts[1].Length > 0 ? parts[1].Split(';') : Array.Empty<string>();
            var colorKeys = new GradientColorKey[colorKeyParts.Length];
            for (var i = 0; i < colorKeyParts.Length; i++)
            {
                var f = colorKeyParts[i].Split(',');
                colorKeys[i] = new GradientColorKey(
                    new Color(float.Parse(f[0], Culture), float.Parse(f[1], Culture), float.Parse(f[2], Culture)),
                    float.Parse(f[3], Culture));
            }

            var alphaKeyParts = parts[2].Length > 0 ? parts[2].Split(';') : Array.Empty<string>();
            var alphaKeys = new GradientAlphaKey[alphaKeyParts.Length];
            for (var i = 0; i < alphaKeyParts.Length; i++)
            {
                var f = alphaKeyParts[i].Split(',');
                alphaKeys[i] = new GradientAlphaKey(float.Parse(f[0], Culture), float.Parse(f[1], Culture));
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            gradient.mode = mode;
            return gradient;
        }
    }
}
