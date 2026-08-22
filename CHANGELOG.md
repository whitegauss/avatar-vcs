# Changelog

All notable changes to this package are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
