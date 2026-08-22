# Changelog

All notable changes to this package are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0-poc] - 2026-08-21

Initial public proof-of-concept release. Design doc Phase 1-4 scope:

- Managed-container lifecycle (destroy/regenerate idempotent prefab-composition tracking)
- Component field / asset reference / scene reference capture and restore
- Track Properties: BlendShape, material, and generic component-field tracking on marked subtrees (avatar body/armature/root), including active/inactive state and prefab-instance transforms
- Material settings duplication and reapplication (dynamic shader property enumeration)
- Commit history, branches, structured diff, branch compare mode
- Standalone BlendShape preset export/import
- EditorWindow UI
