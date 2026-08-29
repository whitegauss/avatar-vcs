using System;
using System.Collections.Generic;

namespace AvatarVcs.Core.Reflection
{
    /// <summary>
    /// Resolves a Type from its full name across all loaded assemblies.
    /// Type.GetType(name) alone only searches the calling assembly and mscorlib,
    /// which fails for most Unity/UnityEditor/package types.
    /// </summary>
    public static class TypeResolver
    {
        // Assembly scanning is done once per name (including misses, cached as
        // null) since a capture/apply pass can resolve the same component type
        // repeatedly across many containers.
        private static readonly Dictionary<string, Type> Cache = new();

        public static Type Resolve(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;

            if (Cache.TryGetValue(fullTypeName, out var cached))
                return cached;

            var resolved = Type.GetType(fullTypeName);
            if (resolved == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    resolved = assembly.GetType(fullTypeName);
                    if (resolved != null) break;
                }
            }

            Cache[fullTypeName] = resolved;
            return resolved;
        }
    }
}
