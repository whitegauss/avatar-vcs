# Changelog

All notable changes to this package are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

- Checkout no longer stamps a prefab-instance override onto every tracked renderer. `AvatarReferenceApplier` now writes a BlendShape weight or material slot only when it actually differs from the live value (matching `GameObjectStateApplier`), so restoring a commit whose values already match leaves the prefab instance clean and keeps the Inspector's Overrides dropdown usable.
- `Ensure Root` no longer creates an empty `[AvatarVCS]/container_1`. Default property tracking already covers the common case, and a loose prefab dropped under `[AvatarVCS]` is auto-wrapped into a container at commit time. README clarified: containers are for prefab add/remove/swap; property tracking is for BlendShape/material/field values.
- Containers now version the BlendShape weights, material slots, and active/tag/layer state you adjust *inside* their prefab instances. On checkout the container is still regenerated from the prefab, then those recorded adjustments are re-applied on top — so "swap this outfit prefab" history and "keep my tweaks to it" no longer conflict.
- Commit/index/config writes now flush to disk before the atomic rename, so a power loss right after a commit can no longer leave a truncated JSON file that breaks history loading for that avatar.
- Hierarchy "untracked" markers are now memoized per editor frame instead of re-walking every row's ancestors on every repaint — noticeably lighter with a deep Armature open in the Hierarchy.
- Compare mode no longer leaks an unhandled exception when a selected commit can't be loaded (deleted or corrupt); it reports a "Checkout Failed" dialog like every other checkout path.
- Closing the AvatarVCS window during a script recompile / play-mode switch no longer runs a scene-mutating checkout mid-domain-reload; compare state is preserved and the window reopens still in compare mode so you can exit it cleanly.

## [0.3.0-poc] - 2026-09-02

### Fixed

- **Default-config avatars now actually record BlendShape / material / lilToon state.** After `Ensure Root` (which tracks the avatar root), capture only ever looked at the tracked target itself — which has no renderer — so a default-config avatar committed none of its BlendShape weights, material references, or shader settings until the user manually un-tracked the root. Capture now walks every renderer in the tracked subtree.
- **Checkout no longer aborts half-way on a corrupt commit.** A commit JSON with a missing value for a `vector3` / `color` / `gradient` / … field threw `NullReferenceException` past every guard, leaving the avatar with all its containers destroyed and the exception leaking into the editor. Such a field is now warned about and skipped.
- **`Untrack Properties Here` works in the default config.** It removed a marker that the avatar root's recursive capture ignored, so it did nothing. It now adds an `AvatarVcsUntracked` marker that excludes its whole subtree from capture — the "don't version-control this outfit" opt-out — and `Track Properties Here` lifts it.
- A failed **Commit** / **Create Branch** no longer corrupts the window's IMGUI layout (`Invalid GUILayout state` spam).
- **Create Container** no longer throws on its second use — the name auto-numbers (`new_container`, `new_container_1`, …).
- Loading a commit written by a **newer** AvatarVCS is refused (warn + skip) instead of silently restoring it with unknown fields dropped.
- Deleting a commit now checks each `generatedAssets` GUID looks AvatarVCS-generated before `AssetDatabase.DeleteAsset` — a hand-edited or corrupt commit can no longer delete your own assets.
- Capturing a hierarchy with **same-named sibling GameObjects** now warns: path resolution (`Transform.Find`) can't tell them apart, so their tracked state could restore onto the wrong one.

### Changed

- `BlendShapeRef` / `MaterialRef` gain a `path` field (relative to the tracked target; absent in older commits ⇒ the target itself, i.e. unchanged behaviour).
- Package no longer declares a `com.vrchat.avatars` dependency (no code referenced the VRChat SDK); `documentationUrl` is now populated.
- Release CI checks out the pushed tag, not `main`, so a tag not on `main`'s tip can't ship an artifact whose contents don't match its name.

### Breaking

- Extracted a `UnityEditor`-free `AvatarVcs.Core` assembly, which moves the public model and reflection types below from `AvatarVcs.Editor.*` to `AvatarVcs.Core.*`. Update any `using` directives that reference the old namespaces.
  - `AvatarVcs.Editor.Model.*` -> `AvatarVcs.Core.Model.*` (`AssetRef`, `AssetVersionEntry`, `AvatarReferenceState`, `BlendShapePreset`, `BranchConfig`, `Commit`, `CommitIndex`, `ComponentState`, `ContainerDiff`, `ContainerSnapshot`, `FieldValue`, `GuidRemapConfig`, `MaterialSettingsState`, `SceneRef`)
  - `AvatarVcs.Editor.History.SnapshotDiffer` -> `AvatarVcs.Core.Diff.SnapshotDiffer`
  - `AvatarVcs.Editor.History.CheckoutResult` / `CheckoutResultKind` -> `AvatarVcs.Core.History.CheckoutResult` / `CheckoutResultKind`
  - `AvatarVcs.Editor.Reflection.TypeResolver` / `ReservedPropertyNames` -> `AvatarVcs.Core.Reflection.TypeResolver` / `ReservedPropertyNames`
  - `AvatarVcs.Editor.MaterialSettings.ShaderPropertyMap` -> `AvatarVcs.Core.MaterialSettings.ShaderPropertyMap`

  Stored commit JSON is unaffected: `JsonUtility` serializes by field name only and records neither a type's namespace nor its assembly, so existing commit history under `ProjectSettings/AvatarVcs/` reads back exactly as before.

  Scenes and prefabs are unaffected too: the scene-referenced `Runtime` components (`AvatarVcsRoot`, `AvatarVcsContainer`, `AvatarVcsTrackedReference`) did not move, so existing scenes and prefabs keep working unchanged.

## [0.2.0-poc] - 2026-08-22

- Track Properties now also captures/restores GameObject tag and layer, alongside the existing active/inactive state
- Fixed several crash-on-corrupted-data bugs in the diff view and BlendShape preset import when handling hand-edited or malformed commit/preset JSON
- Added MIT license and full VPM package metadata (author, license, vpmDependencies, zipSHA256)
- Automated releases via GitHub Actions (tag push -> zip, GitHub Release, VPM repo index update)

## [0.1.0-poc] - 2026-08-21

Initial public proof-of-concept release. Design doc Phase 1-4 scope:

- Managed-container lifecycle (destroy/regenerate idempotent prefab-composition tracking)
- Component field / asset reference / scene reference capture and restore
- Track Properties: BlendShape, material, and generic component-field tracking on marked subtrees (avatar body/armature/root), including active/inactive state and prefab-instance transforms
- Material settings duplication and reapplication (dynamic shader property enumeration)
- Commit history, branches, structured diff, branch compare mode
- Standalone BlendShape preset export/import
- EditorWindow UI
