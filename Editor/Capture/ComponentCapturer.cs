using System;
using System.Collections.Generic;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Capture
{
    /// <summary>
    /// Captures a Component into a ComponentState by walking its SerializedObject
    /// down to leaf properties (struct fields and array elements included).
    /// v1 design doc section 4.1.
    /// </summary>
    public static class ComponentCapturer
    {
        private static readonly HashSet<string> SkippedPropertyNames = new()
        {
            "m_Script",
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_EditorClassIdentifier",
            "m_EditorHideFlags",
        };

        public static ComponentState Capture(Component component, Transform containerRoot)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (containerRoot == null) throw new ArgumentNullException(nameof(containerRoot));

            var state = new ComponentState
            {
                path = ReferenceResolver.GetRelativePath(component.transform, containerRoot),
                type = component.GetType().FullName,
            };

            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            var enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = prop.propertyType == SerializedPropertyType.Generic;

                if (SkippedPropertyNames.Contains(prop.name))
                {
                    enterChildren = false;
                    continue;
                }

                if (prop.propertyType == SerializedPropertyType.Generic)
                    continue; // container node (struct/array); its leaves are visited next

                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prop.objectReferenceValue, out var guid, out long localId);
                    state.assetRefs.Add(new AssetRef
                    {
                        key = prop.propertyPath,
                        guid = guid ?? string.Empty,
                        localId = localId,
                    });
                    continue;
                }

                if (FieldCodec.TryEncode(prop, out var value, out var type))
                {
                    state.fields.Add(new FieldValue
                    {
                        key = prop.propertyPath,
                        value = value,
                        type = type,
                    });
                }
                else
                {
                    Debug.LogWarning($"[AvatarVCS] Unsupported property type '{prop.propertyType}' at '{prop.propertyPath}' on {state.type} was skipped.");
                }
            }

            return state;
        }
    }
}
