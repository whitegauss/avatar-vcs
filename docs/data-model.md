# データモデル — コミット JSON の構造

コミットは `ProjectSettings/AvatarVcs/avatars/{avatarGuid}/commits/{commitId}.json` に保存される JSON ファイルです。シリアライザに `JsonUtility` を使用しているため、型名・名前空間はファイルに記録されません。

---

## スキーマバージョン

```json
{
  "schemaVersion": 2
}
```

| バージョン | 変更内容 |
|---|---|
| 1 | 初期リリース |
| 2 | `BlendShapeRef.path` の追加（ターゲット自身の場合は absent = `""` として扱う。後方互換） |

`schemaVersion` がビルドが認識している最大バージョンを超える場合、そのコミットの読み込みは拒否されます（警告を出し null を返す）。

---

## トップレベル構造

```json
{
  "schemaVersion": 2,
  "commitId": "a1b2c3d4...",          // 32文字小文字 hex (Guid.NewGuid().ToString("N"))
  "parentCommitId": "e5f6a7b8...",    // 親コミット ID（初回コミットは null / ""）
  "branch": "main",                   // コミット時のブランチ名
  "message": "髪ロング版に変更",       // ユーザー入力メッセージ
  "timestamp": "2026-09-03T12:00:00Z",
  "avatarGuid": "11223344...",        // AvatarVcsRoot に保存されたアバター識別 GUID
  "avatarName": "MyAvatar",           // コミット時の GameObject 名（参照用）
  "containers": [ ... ],
  "avatarReferences": [ ... ],
  "materialSettings": [ ... ],
  "generatedAssets": [ "guid1", "guid2" ],
  "assetVersions": [ ... ]
}
```

---

## `containers[]` — コンテナスナップショット

```json
{
  "containerId": "hair_long",         // [AvatarVCS] 直下の GameObject 名
  "containerGuid": "aabbccdd...",     // AvatarVcsContainer に保存された GUID
  "prefabGuids": [
    "deadbeef..."                     // このコンテナに含まれる Prefab の GUID
  ],
  "localPosition": { "x": 0, "y": 0, "z": 0 },
  "localRotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
  "localScale":    { "x": 1, "y": 1, "z": 1 },
  "tag": "Untagged",
  "activeSelf": true,
  "layer": 0,
  "components": [ ... ],             // コンテナルートのコンポーネントフィールド値
  "blendShapes": [ ... ],            // コンテナ内 Prefab インスタンスへの調整値 (KAN-70)
  "materials": [ ... ],
  "objectStates": [ ... ],
  "materialSettings": [ ... ]        // コンテナ内マテリアル設定 (KAN-73)
}
```

> **後方互換ルール**: `tag` は absent のとき `"Untagged"` 扱い。`activeSelf` は absent のとき `true` 扱い。`layer` は absent のとき `0` 扱い。`blendShapes` / `materials` / `objectStates` / `materialSettings` は absent のとき空配列扱い。

---

## `avatarReferences[]` — プロパティ追跡の記録

Track Properties の各追跡ターゲット（`AvatarVcsTrackedReference` が付いた GameObject）ごとのスナップショット。

```json
{
  "path": "Body",                    // アバタールートからの相対パス（"" = アバタールート自身）
  "blendShapes": [
    { "path": "", "name": "mouth_open", "weight": 75.0 },
    { "path": "Face", "name": "eye_blink_L", "weight": 0.0 }
  ],
  "materials": [
    { "path": "", "slot": 0, "guid": "deadbeef..." }
  ],
  "components": [ ... ],
  "objectStates": [
    { "path": "Accessory/Hat", "activeSelf": false, "tag": "Untagged", "layer": 0 }
  ]
}
```

### `BlendShapeRef.path` の意味

`path` は **追跡ターゲット自身からの相対パス** で、その GameObjcet が持つ `SkinnedMeshRenderer` を指します。

- `""` (空文字列 or absent): 追跡ターゲット自身の SkinnedMeshRenderer
- `"Face"`: 追跡ターゲット配下の `Face` という GameObject の SkinnedMeshRenderer

---

## `components[]` — コンポーネントフィールド値

```json
{
  "typeName": "VRCAvatarDescriptor",  // コンポーネントの型名（短縮名）
  "path": "",                          // 追跡ターゲットからの相対パス
  "fields": [
    { "name": "lipSync", "value": "2" },
    { "name": "customEyeLookSettings.leftEye", "value": "GUID:aabbccdd..." }
  ]
}
```

### フィールド値のエンコード形式

| 型 | エンコード形式 |
|---|---|
| プリミティブ (`int`, `float`, `bool`, `string`) | そのまま文字列化 |
| `Vector2 / Vector3 / Vector4` | `"(x,y,z)"` |
| `Color` | `"RGBA(r,g,b,a)"` |
| `Quaternion` | `"(x,y,z,w)"` |
| `Rect` | `"(x,y,width,height)"` |
| `Bounds` | `"Center:(x,y,z) Extents:(x,y,z)"` |
| `Gradient` | `"GRADIENT:..."` (独自形式) |
| `AnimationCurve` | `"CURVE:..."` (独自形式) |
| アセット参照 (`Object`) | `"GUID:32文字hex"` |
| シーン内参照 | `"SCENEREF:パス"` (アバタールートからのパス) |

---

## `materialSettings[]` — マテリアル設定

lilToon / Poiyomi / MToon などのサポート対象シェーダーのプロパティ値を保持します。checkout 時にマテリアルを複製して設定を再適用します。

```json
{
  "targetPath": "Body",               // 追跡ターゲットからの相対パス
  "slot": 0,                          // マテリアルスロット番号
  "guid": "deadbeef...",             // 元マテリアルの GUID
  "generatedGuid": "aabbccdd...",    // checkout で生成した複製マテリアルの GUID
  "properties": [
    { "name": "_MainColor", "value": "RGBA(1,0,0,1)" },
    { "name": "_Cutoff", "value": "0.5" }
  ]
}
```

---

## `assetVersions[]` — アセット内容ハッシュ

checkout 時に警告を出すためのハッシュ記録。コミット後にアセットが上書き更新された場合に検知します。

```json
{
  "guid": "deadbeef...",
  "hash": "sha256ハッシュ値"
}
```

---

## インデックスファイル (`index.json`)

```json
{
  "commitIds": [ "a1b2c3...", "b2c3d4...", ... ],
  "branches": {
    "main": "a1b2c3...",
    "long-hair": "b2c3d4..."
  },
  "currentBranch": "main"
}
```

## ブランチ設定ファイル (`config.json`)

```json
{
  "branches": {
    "main": "a1b2c3..."
  },
  "currentBranch": "main"
}
```

## GUID 再マッピングファイル (`ProjectSettings/AvatarVcs/guid-remapping.json`)

```json
{
  "entries": [
    { "fromGuid": "oldGuid", "toGuid": "newGuid" }
  ]
}
```

---

## 関連ドキュメント

- [アーキテクチャ (Architecture)](./architecture.md)
- [ストレージレイアウト (Storage Layout)](./storage-layout.md)
- [← 目次に戻る](./index.md)
