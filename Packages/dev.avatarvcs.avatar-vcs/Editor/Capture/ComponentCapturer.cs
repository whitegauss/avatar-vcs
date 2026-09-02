using System;
using AvatarVcs.Core.Diagnostics;
using AvatarVcs.Core.Model;
using AvatarVcs.Core.Reflection;
using AvatarVcs.Editor.Diagnostics;
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
        /// <summary>
        /// avatarRoot is used only to resolve scene references (fields
        /// pointing at other live GameObjects/Components, as opposed to
        /// assets) by path; it defaults to containerRoot when omitted, which
        /// degrades scene-reference paths to being container-relative rather
        /// than failing outright.
        /// </summary>
        public static ComponentState Capture(Component component, Transform containerRoot, Transform avatarRoot = null,
            DiagnosticLog log = null)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (containerRoot == null) throw new ArgumentNullException(nameof(containerRoot));
            avatarRoot ??= containerRoot;

            // KAN-20: a caller mid-operation passes its own DiagnosticLog; a
            // direct caller (tests) passes none, so make one and flush it to
            // the console here so existing LogAssert expectations still fire.
            var ownsLog = log == null;
            log ??= new DiagnosticLog();
            try
            {
                return CaptureCore(component, containerRoot, avatarRoot, log);
            }
            finally
            {
                if (ownsLog) UnityDiagnosticSink.Flush(log);
            }
        }

        private static ComponentState CaptureCore(Component component, Transform containerRoot, Transform avatarRoot, DiagnosticLog log)
        {
            var componentType = component.GetType();
            var siblings = component.GetComponents(componentType);
            var state = new ComponentState
            {
                path = ReferenceResolver.GetRelativePath(component.transform, containerRoot),
                type = componentType.FullName,
                componentIndex = Array.IndexOf(siblings, component),
            };

            var so = new SerializedObject(component);
            var prop = so.GetIterator();
            var enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = prop.propertyType == SerializedPropertyType.Generic;

                if (ReservedPropertyNames.Names.Contains(prop.name))
                {
                    enterChildren = false;
                    continue;
                }

                if (prop.propertyType == SerializedPropertyType.Generic)
                    continue; // container node (struct/array); its leaves are visited next

                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    CaptureObjectReference(state, prop, avatarRoot, log);
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
                    log.Warn($"[AvatarVCS] Unsupported property type '{prop.propertyType}' at '{prop.propertyPath}' on {state.type} was skipped.");
                }
            }

            return state;
        }

        /// <summary>
        /// Asset references (EditorUtility.IsPersistent) go through
        /// AssetDatabase GUID+localId, same as before. Everything else is a
        /// live scene object (e.g. VRCPhysBone.rootTransform,
        /// ModularAvatarMergeArmature pointing at a bone on the avatar's own
        /// Armature): those are captured by path relative to avatarRoot
        /// instead, since resolving them via AssetDatabase would silently
        /// come back empty and null the field out on restore.
        /// </summary>
        private static void CaptureObjectReference(ComponentState state, SerializedProperty prop, Transform avatarRoot, DiagnosticLog log)
        {
            var reference = prop.objectReferenceValue;

            if (reference == null || EditorUtility.IsPersistent(reference))
            {
                var guid = string.Empty;
                long localId = 0;
                // TryGetGUIDAndLocalFileIdentifier throws on a null Object; an
                // unset reference (e.g. Light.cookie) is common and just means
                // "no reference" (empty guid).
                if (reference != null)
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out guid, out localId);

                state.assetRefs.Add(new AssetRef
                {
                    key = prop.propertyPath,
                    guid = guid ?? string.Empty,
                    localId = localId,
                });
                return;
            }

            var referenceTransform = reference switch
            {
                GameObject go => go.transform,
                Component c => c.transform,
                _ => null,
            };

            if (referenceTransform == null)
            {
                log.Warn($"[AvatarVCS] Scene reference '{prop.propertyPath}' on {state.type} is neither a GameObject nor a Component and was skipped.");
                return;
            }

            try
            {
                var path = ReferenceResolver.GetRelativePath(referenceTransform, avatarRoot);
                state.sceneRefs.Add(new SceneRef
                {
                    key = prop.propertyPath,
                    path = path,
                    type = reference.GetType().FullName,
                });
            }
            catch (ArgumentException)
            {
                log.Warn($"[AvatarVCS] Scene reference '{prop.propertyPath}' on {state.type} points outside the avatar hierarchy and was skipped.");
            }
        }
    }
}
