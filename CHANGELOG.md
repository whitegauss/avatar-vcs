# Changelog

All notable changes to this package are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Window → AvatarVCS → Clean Up Orphaned History.** An avatar's identity is the id minted onto its `[AvatarVCS]` root, so deleting that root and setting it up again starts a fresh history and strands the old one under `ProjectSettings/AvatarVcs/avatars/` forever. Nothing ever removed those. The new command lists the histories no avatar in the project claims any more, with commit counts and sizes, and deletes them on confirmation — keeping the most recently committed one, in case the root was removed by mistake (an orphan with no commits never takes that slot). If the project can't be searched reliably — assets serialised as binary, an unreadable file, a cancelled scan — nothing is reported as orphaned at all. Deciding what is orphaned searches **every** scene and prefab in the project plus everything currently loaded, not just the open scene, so an avatar living in a scene you don't have open is never mistaken for garbage.
- Orphaned-history cleanup now also runs **on its own**, once per Unity session, when you open the AvatarVCS window — so the folder doesn't grow forever waiting for someone to remember the menu command. It costs nothing in the normal case: before touching the project it checks whether there are even enough unclaimed histories for the retention rule to remove one, and skips the scan entirely when there aren't. Every safeguard the manual command has still applies — an incomplete scan deletes nothing, and the most recently committed orphan is always kept. Toggle it under **Window → AvatarVCS → Clean Up Orphaned History Automatically**.

### Fixed

- **lilToon / Poiyomi shader settings are now recorded for real avatars.** Supported shaders were matched by exact name, and the only lilToon name on the list was the plain `lilToon`. Real materials are almost always variants — `Hidden/lilToonOutline`, `Hidden/lilToonTransparent`, `_lil/[Optional] lilToonOverlay` and 60 others — and every one of them was skipped, silently, because an unsupported shader is not something the tool warns about. The result was that an entire avatar could commit no shader settings at all and a checkout had nothing to put back, which looked exactly like "the colour isn't being restored". Shaders are now matched by family, so all lilToon variants, Poiyomi's `Poiyomi Toon` / `Poiyomi Pro` / locked-in shaders, and both MToon generations are covered. lilToon's internal pass shaders (`Hidden/ltspass_*`, `Hidden/ltsother_*`) are still excluded.
- **Commits no longer record the pose of every bone in your Armature.** Only an accessory that is a prefab instance in its own right (dropped onto a bone, bypassing containers) is supposed to have its Transform tracked; bone pose never was. The check asked whether an object came from a prefab, which is true of *everything* inside a prefab instance — and a real avatar is one — so it excluded nothing. In one real project 534 of a commit's 678 captured components were bones, and dropping them takes that project's stored history from 11.8 MB to 9.3 MB (about 21% smaller; bones are 36% of the captured component data). Existing history keeps working — the surplus entries just restore the pose they already recorded.

### Changed

- Checkout no longer loads every sub-asset of a referenced file just to resolve a reference to that file's main asset. An avatar's AnimatorController can hold hundreds of sub-assets, and the full load happened once per recorded reference per checkout. As a side effect, a damaged internal reference in one of your own assets no longer makes Unity print `Broken text PPtr in file(...)` on every checkout with an AvatarVCS stack trace under it — the underlying problem is in the asset, but AvatarVCS was what forced it into view.

## [0.4.2-poc] - 2026-09-03

### Fixed

- **The commit-deletion cleanup can no longer remove one of your own assets or a whole folder.** Deleting a commit removes the duplicate materials it generated, and the "does this look AvatarVCS-generated?" check ignored the file extension entirely — so a corrupt or badly merged `generatedAssets` list naming a `.prefab`, a `.controller`, or a *folder* GUID would have deleted it (recursively, for a folder). The check now requires a `.mat` and explicitly refuses folders, and is shared with the code that creates the duplicates so the two can't drift apart again.
- A prefab dropped loose under `[AvatarVCS]` no longer jumps when it is auto-wrapped into a container at commit time. The wrapper was zeroed *after* the prefab was already inside it, so on an avatar not standing at the world origin every adopted prefab was displaced by the avatar's own offset. (#73)
- A commit whose JSON contains an explicit `null` list (from a hand-edit or a botched merge) no longer throws while diffing. Because the "do you have uncommitted changes?" check runs through the same diff, a single such commit used to make **Switch Branch** and **Checkout** fail outright; both now degrade the diff instead.
- Committing no longer aborts when a material's shader asset has been deleted or failed to import — the affected slot is skipped, as it already was inside containers.

## [0.4.1-poc] - 2026-09-03

### Fixed

- **Shader settings on a material inside a container now survive a checkout.** An outfit or hairstyle dropped under `[AvatarVCS]` is auto-wrapped into a container, and containers regenerate their prefab instances from the prefab on every checkout — so a lilToon/Poiyomi/MToon main colour (or any other recorded Color/Float property) tweaked on one of those materials reverted to the prefab default. Containers now record those values alongside the BlendShape weights, material slots, and active/tag/layer they already versioned, and re-apply them onto a duplicated material after regeneration. The source `.mat` asset is still never modified.
- Duplicate materials generated for a container's inner slots are now listed in the commit's `generatedAssets`, so they are reused across checkouts of the same commit instead of piling up, and are cleaned up when that commit is deleted.
- The diff view now shows a `material settings …` line when a shader value inside a container changes, instead of reporting the container as unchanged.

## [0.4.0-poc] - 2026-09-03

### Added

- Containers now version the BlendShape weights, material slots, and active/tag/layer state you adjust *inside* their prefab instances. On checkout the container is still regenerated from the prefab, then those recorded adjustments are re-applied on top — so "swap this outfit prefab" history and "keep my tweaks to it" no longer conflict.

### Fixed

- Checkout no longer stamps a prefab-instance override onto every tracked renderer. `AvatarReferenceApplier` now writes a BlendShape weight or material slot only when it actually differs from the live value (matching `GameObjectStateApplier`), so restoring a commit whose values already match leaves the prefab instance clean and keeps the Inspector's Overrides dropdown usable.
- Commit/index/config writes now flush to disk before the atomic rename, so a power loss right after a commit can no longer leave a truncated JSON file that breaks history loading for that avatar.
- Compare mode no longer leaks an unhandled exception when a selected commit can't be loaded (deleted or corrupt); it reports a "Checkout Failed" dialog like every other checkout path.
- Closing the AvatarVCS window during a script recompile / play-mode switch no longer runs a scene-mutating checkout mid-domain-reload; compare state is preserved and the window reopens still in compare mode so you can exit it cleanly.

### Changed

- `Ensure Root` no longer creates an empty `[AvatarVCS]/container_1`. Default property tracking already covers the common case, and a loose prefab dropped under `[AvatarVCS]` is auto-wrapped into a container at commit time. README clarified: containers are for prefab add/remove/swap; property tracking is for BlendShape/material/field values.
- Hierarchy "untracked" markers are now memoized per editor frame instead of re-walking every row's ancestors on every repaint — noticeably lighter with a deep Armature open in the Hierarchy.

### Internal

- Capture/apply diagnostics now flow through a `DiagnosticLog` returned to the caller (one console sink) instead of scattered `Debug.LogWarning` calls; console output is byte-identical.
- `AvatarVcsWindow` split into a UnityEditor-free `AvatarVcsPresenter` (state + transitions, unit-tested) behind `IHistoryStore` / `IAvatarGateway` / `IUserPrompt` ports, plus thin Editor adapters.
- New scene-free `AvatarVcs.Tests.Core` assembly with ~70 pure tests; `SnapshotDifferTests` and others relocated there.
- README's 開発 section records where the (gitignored) design doc lives.

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
