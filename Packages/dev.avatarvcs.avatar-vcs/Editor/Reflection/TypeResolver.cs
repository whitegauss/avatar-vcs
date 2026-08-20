using System;

namespace AvatarVcs.Editor.Reflection
{
    /// <summary>
    /// Resolves a Type from its full name across all loaded assemblies.
    /// Type.GetType(name) alone only searches the calling assembly and mscorlib,
    /// which fails for most Unity/UnityEditor/package types.
    /// </summary>
    public static class TypeResolver
    {
        public static Type Resolve(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;

            var direct = Type.GetType(fullTypeName);
            if (direct != null) return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullTypeName);
                if (type != null) return type;
            }

            return null;
        }
    }
}
