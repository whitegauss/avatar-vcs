# ストレージレイアウト

avatar-vcs のコミットデータはすべて Unity プロジェクトの `ProjectSettings/AvatarVcs/` ディレクトリに保存されます。

---

## ディレクトリ構造

```
ProjectSettings/
└── AvatarVcs/
    ├── guid-remapping.json              # GUID 再マッピング設定（プロジェクト全体で共有）
    └── avatars/
        └── {avatarGuid}/               # アバターごとのディレクトリ（32文字 hex）
            ├── config.json             # ブランチ設定（ブランチ名 → HEAD コミット ID）
            ├── index.json              # コミット ID 一覧 + ブランチ情報
            └── commits/
                ├── {commitId}.json     # コミットのスナップショット本体
                ├── {commitId}.json
                └── ...
```

---

## ファイルごとの説明

### `config.json`

現在のブランチとブランチ別 HEAD コミット ID を管理します。

```json
{
  "branches": {
    "main": "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
    "long-hair": "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7"
  },
  "currentBranch": "main"
}
```

- ブランチ切り替えのたびに `currentBranch` が更新されます
- コミットのたびに対応ブランチの HEAD（値）が更新されます

### `index.json`

コミット ID の一覧を保持します。削除・一覧表示 UI はここを参照します。

```json
{
  "commitIds": [
    "a1b2c3...",
    "b2c3d4..."
  ]
}
```

### `commits/{commitId}.json`

コミット本体。[data-model.md](./data-model.md) を参照。

ファイル書き込みは **アトミック操作**（一時ファイルへの書き込み → リネーム）で行われるため、書き込み中断によるファイル破損が防止されています。

### `guid-remapping.json`

アセット再インポート後の GUID 変更を追跡するマッピングテーブル。

```json
{
  "entries": [
    { "fromGuid": "oldGuid32hex", "toGuid": "newGuid32hex" }
  ]
}
```

---

## セキュリティ

`avatarGuid` と `commitId` はファイルパスに直接展開されます。`CommitIdentifier.EnsureValid()` により、**32 文字小文字 16 進数** 以外の値（`../../../` 等のパストラバーサル文字列を含む）はパス展開前に拒否されます。

この検証は:
- Unity が直接デシリアライズする `SerializeField` 値（ユーザーが手編集した scene/prefab に任意の値が入りえる）
- ディスクから読み込んだ commit JSON 内の commitId

の両方で行われます。

---

## バックアップと Git 管理

`ProjectSettings/AvatarVcs/` ディレクトリはプロジェクトのリポジトリに含めることで、コミット履歴ごとバックアップできます。

`.gitignore` にすでに除外指定がある場合は、以下を追加してください:

```gitignore
# AvatarVCS コミット履歴を Git で管理する場合
!ProjectSettings/AvatarVcs/
```

> **注意**: `generatedAssets` に記録される複製マテリアル（`Assets/` 内に生成される）は、checkout ごとに再生成されます。これらは Git に含める必要はありません。

---

## 生成アセット

checkout 時に `materialSettings` の内容から複製マテリアルが `Assets/` 内に生成されます。

- 生成先パス: `Assets/AvatarVcs/GeneratedMaterials/{avatarGuid}/{commitId}/{...}.mat`（実装に依存）
- 生成された GUID は `commit.generatedAssets[]` に記録されます
- コミットを削除すると、対応する生成アセットも `AssetDatabase.DeleteAsset()` で削除されます（ただし GUID が AvatarVCS 生成のものと確認できた場合のみ）

---

## 関連ドキュメント

- [データモデル (Data Model)](./data-model.md)
- [アーキテクチャ (Architecture)](./architecture.md)
- [← 目次に戻る](./index.md)
