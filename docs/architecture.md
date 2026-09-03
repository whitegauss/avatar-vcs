# アーキテクチャ

## アセンブリ構成

avatar-vcs は UnityEditor 依存を持つコードと持たないコードを明確に分離しています。

```
┌─────────────────────────────────────────────────────┐
│                   AvatarVcs.Editor                   │  (Editor-only)
│  UI / Menu / Apply / Capture / History / Operations  │
└──────────────┬───────────────────────────┬──────────┘
               │ uses                      │ uses (Runtime marker types:
               │                           │  AvatarVcsContainer, etc.)
┌──────────────▼───────────────────────────┐          │
│                   AvatarVcs.Core         │          │  (Editor + Tests)
│   Model / History / Diff / Presentation  │          │
│   Naming / Diagnostics / MaterialSettings│          │
└──────────────┬───────────────────────────┘          │
               │ uses                                 │
┌──────────────▼──────────────────────────────────────▼┐
│                  AvatarVcs.Runtime                   │  (Runtime + Editor)
│            Components (マーカーコンポーネント)          │
└──────────────────────────────────────────────────────┘
```

| アセンブリ | 名前空間プレフィックス | 役割 |
|---|---|---|
| `AvatarVcs.Runtime` | `AvatarVcs.Runtime` | シーン配置コンポーネント（MonoBehaviour） |
| `AvatarVcs.Core` | `AvatarVcs.Core.*` | UnityEditor 非依存のロジック・モデル（Runtime に依存） |
| `AvatarVcs.Editor` | `AvatarVcs.Editor.*` | Unity Editor API を使う実装層（Core および Runtime に依存。`ContainerCapture` や `ContainerRestore` が Runtime マーカー型を参照） |

---

## レイヤー詳細

### Runtime レイヤー (`AvatarVcs.Runtime`)

シーンや Prefab に配置されるマーカーコンポーネント群。**UnityEditor への依存なし**。

| クラス | 役割 |
|---|---|
| `AvatarVcsRoot` | `[AvatarVCS]` GameObject に付与。`avatarGuid`（不変）を保持。アバター複製時に GUID 重複を自動検知・再発行（`OnValidate`） |
| `AvatarVcsContainer` | コンテナ GameObject に付与。`containerGuid`（不変）を保持。重複は OnValidate で自動解決 |
| `AvatarVcsTrackedReference` | プロパティ追跡対象の GameObject に付与するオプトインマーカー |
| `AvatarVcsUntracked` | プロパティ追跡から除外するサブツリーに付与するオプトアウトマーカー |
| `GuidShape` | 32 文字小文字 hex GUID の形式バリデーション（Runtime から参照できるよう分離） |

### Core レイヤー (`AvatarVcs.Core`)

ビジネスロジックとデータモデル。**UnityEditor API を使わない**ため、`AvatarVcs.Tests.Core` アセンブリからシーン・AssetDatabase なしで直接テスト可能。

#### Model サブディレクトリ

| クラス | 役割 |
|---|---|
| `Commit` | 1コミットのスナップショット全体。`schemaVersion`、`commitId`、`branch`、`message`、`containers[]`、`avatarReferences[]`、`materialSettings[]` 等を保持 |
| `ContainerSnapshot` | コンテナ1件の記録。Prefab GUID 列・Transform・tag/active/layer・コンポーネントフィールド・BlendShape/Material/ObjectState の調整値 |
| `AvatarReferenceState` | Track Properties の1追跡ターゲット分の記録。`path`（相対パス）・BlendShape・Material・コンポーネント・objectStates |
| `BlendShapeRef` | BlendShape 1件。`path`（ターゲット相対）・`name`・`weight` |
| `MaterialRef` | マテリアルスロット1件。`path`・`slot`・`guid` |
| `ObjectStateRef` | GameObject 状態1件。`path`・`activeSelf`・`tag`・`layer` |
| `ComponentState` | コンポーネント1件のフィールド値。`typeName`・`path`・`fields[]` |
| `FieldValue` | フィールド1つの値。`name`・`value`（文字列エンコード） |
| `MaterialSettingsState` | マテリアル設定1件（lilToon 等のシェーダープロパティ値群） |
| `AssetVersionEntry` | コミット時のアセット内容ハッシュ記録。checkout 時の変更警告に使用 |
| `BranchConfig` | ブランチ名 → HEAD コミット ID のマッピング + カレントブランチ名 |
| `CommitIndex` | コミット ID 列とブランチ情報のインデックス |

#### History サブディレクトリ

| クラス | 役割 |
|---|---|
| `CommitPaths` | ファイルパスの組み立て。`ProjectSettings/AvatarVcs/avatars/{avatarGuid}/...` |
| `CommitIdentifier` | GUID/コミット ID のパス安全性バリデーション（パストラバーサル防止） |
| `CommitIndexOps` | コミットインデックスの CRUD ヘルパー |
| `BranchConfigOps` | ブランチ HEAD の取得・更新ヘルパー |
| `CommitDeletionPlanner` | コミット削除計画（HEAD でないコミットのみ対象）の算出 |
| `CheckoutResult` | checkout の結果型。`Success` / `MissingPrefabs`・バージョン警告・診断ログ |
| `AssetVersionComparer` | コミット時と現在のアセット内容ハッシュを比較して変更を検知 |
| `GuidRemapResolver` | missing GUID → 新 GUID のマッピングを管理 |

#### Diff サブディレクトリ

| クラス | 役割 |
|---|---|
| `SnapshotDiffer` | 2つの `Commit` を比較し `ContainerDiff[]` を生成する純粋関数 |
| `DiffRowFormatter` | diff 行をテキスト表現に変換（UI・テスト用） |

#### Presentation サブディレクトリ（MVP パターン）

| クラス/インターフェース | 役割 |
|---|---|
| `AvatarVcsPresenter` | ウィンドウの状態・遷移ロジック（ブランチ切り替え・compare モード・diff など）。依存性は下記3ポートのみ |
| `IHistoryStore` | コミット・インデックス・設定の読み書き。Editor 実装: `EditorHistoryStore` |
| `IAvatarGateway` | シーン上のアバターへの操作。Editor 実装: `EditorAvatarGateway` |
| `IUserPrompt` | モーダルダイアログ表示。Editor 実装: `EditorUserPrompt` |
| `WindowMessages` | ダイアログ文言の定数 |

### Editor レイヤー (`AvatarVcs.Editor`)

Unity Editor API を使う実装層。

| ディレクトリ | 主要クラス | 役割 |
|---|---|---|
| `Core/` | `ContainerManager` | `[AvatarVCS]` ルートとコンテナの作成・検索・バリデーション・ルーズ Prefab 自動ラップ |
| `Capture/` | `ComponentCapturer` | GameObject のコンポーネントフィールドを `ComponentState` として記録 |
| `Apply/` | `ComponentApplier`, `GameObjectStateApplier` | `ComponentState` / `ObjectStateRef` をシーンに適用 |
| `History/` | `CommitStore`, `CommitBuilder`, `CheckoutOperation`, `BranchManager`, `AssetVersionChecker`, `GuidRemapper` | コミット永続化・ビルド・checkout 実行・ブランチ操作 |
| `AvatarReferences/` | `AvatarReferenceCapture`, `AvatarReferenceApplier`, `AvatarReferenceCollector`, `BlendShapePresetIO` | Track Properties の記録・適用・BlendShape プリセット入出力 |
| `MaterialSettings/` | `MaterialSettingsCapture`, `MaterialSettingsApplier` | lilToon 等シェーダープロパティの記録・適用 |
| `Operations/` | `ContainerCapture`, `ContainerRestore` | コンテナの記録・Prefab からの再生成 |
| `Reflection/` | `FieldCodec`, `ReferenceResolver` | フィールド値のシリアライズ/デシリアライズ、シーン内参照の解決 |
| `Diagnostics/` | `UnityDiagnosticSink` | `DiagnosticLog` → `Debug.Log` ブリッジ |
| `UI/` | `AvatarVcsWindow` (partial) | EditorWindow 本体。partial クラスで機能エリアに分割 |
| `Menu/` | `AvatarVcsMenu` | `GameObject → AvatarVCS` メニューコマンド |

---

## MVP アーキテクチャ（UI 分離）

`AvatarVcsWindow` は描画とユーザー操作のディスパッチのみを行います。状態保持・遷移ロジックは `AvatarVcsPresenter` に集約されており、Editor 依存のない `AvatarVcs.Tests.Core` から直接テストできます。

```
                        ┌──────────────────────────┐
                        │    AvatarVcsWindow        │  (IMGUI 描画・dispatch)
                        │  ← partial クラス分割 →  │
                        │  .cs / .History.cs        │
                        │  .CommitBranch.cs         │
                        │  .Compare.cs              │
                        │  .Remap.cs                │
                        └────────────┬─────────────┘
                                     │ 呼び出し
                        ┌────────────▼─────────────┐
                        │   AvatarVcsPresenter      │  (状態・遷移)
                        └──┬──────────┬───────────┬┘
                           │          │           │
               ┌───────────▼──┐ ┌────▼────┐ ┌───▼──────────┐
               │ IHistoryStore│ │IAvatarGw│ │ IUserPrompt  │
               └───────────┬──┘ └────┬────┘ └───┬──────────┘
                           │          │           │
               ┌───────────▼──┐ ┌────▼──────┐ ┌─▼────────────┐
               │EditorHistory │ │EditorAvatar│ │EditorUser    │
               │   Store      │ │  Gateway   │ │  Prompt      │
               └──────────────┘ └───────────┘ └──────────────┘
```

---

## テスト構成

| アセンブリ | 対象 | 実行環境 |
|---|---|---|
| `AvatarVcs.Tests.Core` | Core 層の純粋ロジック（SnapshotDiffer・CommitIdentifier・BranchConfigOps など） | EditMode (シーン不要) |
| `AvatarVcs.Tests.Editor` | Editor 層の統合テスト（コンテナ操作・checkout・ブランチ切り替えなど） | EditMode (Unity シーン必要) |

テストの実行方法: `TestProject` を Unity 2022.3 で開き、**Test Runner (EditMode)** から実行。CI でも push / PR ごとに同じテストが自動実行されます（`game-ci/unity-test-runner`）。

---

## 関連ドキュメント

- [データモデル (Data Model)](./data-model.md)
- [ストレージレイアウト (Storage Layout)](./storage-layout.md)
- [← 目次に戻る](./index.md)
