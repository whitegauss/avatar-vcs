using System;
using AvatarVcs.Editor.Model;
using AvatarVcs.Editor.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarVcs.Editor.Apply
{
    public enum ApplyResultKind
    {
        Success,
        PathUnresolved,
        ComponentMissing,
        ComponentTypeUnresolved,
        PrefabAssetGuard,
    }

    public class ApplyResult
    {
        public ApplyResultKind Kind { get; }
        public string Message { get; }
        public bool IsSuccess => Kind == ApplyResultKind.Success;

        private ApplyResult(ApplyResultKind kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        public static ApplyResult Success() => new(ApplyResultKind.Success, null);
        public static ApplyResult PathUnresolved(string path) => new(ApplyResultKind.PathUnresolved, $"Path '{path}' could not be resolved.");
        public static ApplyResult ComponentMissing(string type) => new(ApplyResultKind.ComponentMissing, $"Component '{type}' not found on target and createIfMissing was false.");
        public static ApplyResult ComponentTypeUnresolved(string type) => new(ApplyResultKind.ComponentTypeUnresolved, $"Component type '{type}' could not be resolved.");
        public static ApplyResult PrefabAssetGuard() => new(ApplyResultKind.PrefabAssetGuard, "Refusing to apply to a prefab asset; apply targets scene instances only.");
    }

    /// <summary>
    /// Writes a ComponentState back onto a live Component via SerializedObject.
    /// v1 design doc section 4.2/3.3.
    /// </summary>
    public static class ComponentApplier
    {
        /// <summary>
        /// avatarRoot resolves sceneRefs' paths; it defaults to containerRoot
        /// when omitted (matching ComponentCapturer's default), which is fine
        /// as long as the component has no scene references pointing outside
        /// the container.
        /// </summary>
        public static ApplyResult Apply(ComponentState state, GameObject containerRoot, GameObject avatarRoot = null, bool createIfMissing = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (containerRoot == null) throw new ArgumentNullException(nameof(containerRoot));
            avatarRoot ??= containerRoot;

            if (PrefabUtility.IsPartOfPrefabAsset(containerRoot))
                return ApplyResult.PrefabAssetGuard();

            var target = ReferenceResolver.ResolvePath(state.path, containerRoot.transform);
            if (target == null)
                return ApplyResult.PathUnresolved(state.path);

            var type = TypeResolver.Resolve(state.type);
            if (type == null)
                return ApplyResult.ComponentTypeUnresolved(state.type);

            var component = target.GetComponent(type);
            if (component == null)
            {
                if (!createIfMissing)
                    return ApplyResult.ComponentMissing(state.type);
                component = Undo.AddComponent(target.gameObject, type);
            }

            Undo.RecordObject(component, "AvatarVCS Apply");
            var so = new SerializedObject(component);

            foreach (var field in state.fields)
            {
                var prop = so.FindProperty(field.key);
                if (prop == null)
                {
                    Debug.LogWarning($"[AvatarVCS] Unknown field '{field.key}' on {state.type} at '{state.path}' was skipped.");
                    continue;
                }
                if (!FieldCodec.TryDecode(prop, field.type, field.value))
                    Debug.LogWarning($"[AvatarVCS] Could not decode field '{field.key}' (type '{field.type}') on {state.type}.");
            }

            foreach (var assetRef in state.assetRefs)
            {
                var prop = so.FindProperty(assetRef.key);
                if (prop == null)
                {
                    Debug.LogWarning($"[AvatarVCS] Unknown asset reference '{assetRef.key}' on {state.type} at '{state.path}' was skipped.");
                    continue;
                }

                prop.objectReferenceValue = string.IsNullOrEmpty(assetRef.guid)
                    ? null
                    : ReferenceResolver.ResolveAsset(assetRef.guid, assetRef.localId);
            }

            foreach (var sceneRef in state.sceneRefs)
            {
                var prop = so.FindProperty(sceneRef.key);
                if (prop == null)
                {
                    Debug.LogWarning($"[AvatarVCS] Unknown scene reference '{sceneRef.key}' on {state.type} at '{state.path}' was skipped.");
                    continue;
                }

                var referencedTransform = ReferenceResolver.ResolvePath(sceneRef.path, avatarRoot.transform);
                if (referencedTransform == null)
                {
                    Debug.LogWarning($"[AvatarVCS] Scene reference path '{sceneRef.path}' for '{sceneRef.key}' on {state.type} could not be resolved and was skipped.");
                    continue;
                }

                prop.objectReferenceValue = ReferenceResolver.ResolveSceneReference(referencedTransform, sceneRef.type);
            }

            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);

            return ApplyResult.Success();
        }
    }
}
