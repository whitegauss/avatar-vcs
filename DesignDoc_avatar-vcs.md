# 詳細設計書 v2: avatar-vcs

> v1（`DesignDoc_v1_superseded.md`）からの方針転換版。主役を「設定管理」から「**構成のバージョン管理**」に変更し、冪等性の解法として**管理下コンテナ方式**を採用した。v1 の Merge/Replace モードや managedScope による削除判定は本設計で不要になったため廃止する。

---

## 0. 方針転換のサマリ

| 項目 | v1（旧） | v2（本設計） |
|---|---|---|
| 主役 | 設定（パラメータ）の再現 | **構成のバージョン管理**（設定再現は副次効果） |
| Prefab 配置 | スコープ外 | **スコープ内**（復元に必須のため） |
| 冪等性の担保 | managedScope による差分削除判定 | **管理下コンテナの破棄→再生成**（自明に成立） |
| apply モード | Merge / Replace の2種 | **再生成のみ**（モード分岐が不要になった） |
| ブランチ | なし | **あり**（髪A/髪B の並行管理・比較） |
| アバター本体 | 一部管理対象 | **構造はスコープ外。ただし BlendShape 値とマテリアル参照のみホワイトリストで管理**（1.4 参照） |

---

## 1. 中核アーキテクチャ: 管理下コンテナ方式

### 1.1 構造

ツールが管理する領域を、専用の親 GameObject（コンテナ）として物理的に分離する。

```
Avatar (VRCAvatarDescriptor)
├── Body                              ← ユーザー領域（本ツールは一切触らない）
├── Armature                          ← ユーザー領域（同上）
│   └── Hips/Spine/...
└── [AvatarVCS]                    ← ツール管理ルート（マーカーコンポーネント付き）
    ├── outfit_a                      ← コンテナ（1つの構成単位）
    │   ├── Outfit_A (Prefab Instance)
    │   └── ModularAvatarMergeArmature 等
    └── hair_long                     ← 別コンテナ
        └── Hair_Long (Prefab Instance)
```

### 1.2 なぜこれで冪等性が成立するか

- 復元 = **該当コンテナを丸ごと `DestroyImmediate` → JSON から再生成**。
- 個別オブジェクトの照合が不要なため、「2回 apply したら衣装が2つ生える」問題が原理的に起きない。
- 「どこまでがツールの管理範囲か」を判定するロジック（v1 の `managedScope`）も不要。**コンテナの内側＝ツール領域、外側＝ユーザー領域**という物理的境界で自明に分離される。
- 思想的には Docker のコンテナ再作成、Kubernetes の Pod 再生成と同じ。**差分適用より再生成の方が状態管理が単純**という原則に従う。

### 1.3 スコープ境界（重要）

- **アバター本体（Body / Armature 等）は完全にスコープ外。** ここに対する変更（ボーン追加、BlendShape 変更など）は本ツールの管理対象外であり、復元によって巻き戻ることもない。
- ユーザーがアバター本体側に直接 MA コンポーネントを付けた場合、それはツールの管理外として扱う（警告表示は行うが、削除も復元もしない）。
- **前提:** ユーザーは復元に必要な Prefab（衣装・髪など）をプロジェクト内に所持しているものとする。所持していない場合は復元不能として明示エラーにする。

### 1.3.1 コンテナの識別と命名

- **命名はユーザーが自由に付ける。** Prefab 名からの自動生成はデフォルト値としてのみ提示し、変更可能とする（例: `Hair_Long.prefab` → 初期値 `hair_long`）。
- **識別子は `containerId`（GameObject 名）。** ただし GameObject 名は同一階層で重複しうるため、以下で担保する:
  - コンテナ作成時に重複チェックを行い、既存と衝突する場合は UI で拒否する。
  - コンテナルート（`[AvatarVCS]`）直下にのみコンテナを置き、ネストは許可しない（識別の一意性を階層構造で保証する）。
- **マーカーコンポーネント（`AvatarVcsContainer`）を各コンテナに付与し、内部に不変の `containerGuid` を持たせる。** ユーザーがコンテナ名を変更しても追跡できるようにするため。JSON 側は `containerId`（表示名）と `containerGuid`（同一性判定用）の両方を記録する。

### 1.3.2 コンテナの粒度

- **1コンテナ = 1つの機能単位**（1つの衣装、1つの髪型、1つのギミック）を原則とする。
- 1コンテナに複数の Prefab を含めることは許可する（例: 衣装本体 + 付属アクセサリ）。この場合 `prefabGuid` は配列になる。
- 粒度をユーザー判断に委ねるため強制はしないが、UI 上で「ブランチ比較は コンテナ単位で行われる」ことを明示し、比較したい単位で分けるよう誘導する。

### 1.3.3 複数アバターの扱い

- **アバターごとに `[AvatarVCS]` ルートを持つ。** 1シーン内に複数アバターがあっても、それぞれ独立して管理される。
- コミット履歴も **アバター単位で分離**する（ストレージ設計はセクション4参照）。アバターの識別には `VRCAvatarDescriptor` が付いた GameObject に付与するマーカーコンポーネントの GUID を使う（GameObject 名の変更に耐えるため）。

## 1.4 アバター本体プロパティのホワイトリスト管理

セクション1.3で「アバター本体はスコープ外」としたが、**構造を変えない限定的なプロパティのみ**、例外的に管理対象とする。コンテナ方式（破棄→再生成）とは扱いが異なるため、別領域として設計する。

### 1.4.1 管理対象（ホワイトリスト）

| 対象 | 実体 | 複製の要否 | MVP |
|---|---|---|---|
| BlendShape の**値** | `SkinnedMeshRenderer.m_BlendShapeWeights`（float配列） | 不要（Mesh は共有のまま） | ✅ |
| マテリアルの**参照** | `Renderer.m_Materials`（アセット参照配列） | 不要（参照を差し替えるだけ） | ✅ |
| **シェーダー設定（liltoon 等）** | `Material` アセット内のプロパティ | **必要**（1.4.3参照） | ✅ |
| テクスチャの差し替え | Material 内のテクスチャスロット | 必要 | ❌ v1.x |

**線引きの原則:** そのデータが **シーン上のコンポーネント側にあるか、アセット側にあるか**。コンポーネント側（Renderer のフィールド）なら安全に直接書き換えられる。アセット側（`.mat` の中身）なら共有アセットへの副作用が出るため複製が必要になる。

**シェーダー設定を MVP に含める理由（更新耐性）:** Prefab は衣装アップデートで構造（ボーン名・階層・オブジェクト名）が変わると記録したパスが総崩れになるのに対し、**シェーダープロパティ名（`_Color`、`_OutlineWidth` 等）は安定している**。そのため「衣装が v1.2 → v1.5 に更新されても、色・アウトライン等の設定だけは新しいマテリアルにそのまま当て直せる」という、セクション 6 のアセット更新問題に対する現実的な回答になる。Prefab 配置の復元が更新に弱いのに対し、この機能は独立して価値を持つ。

### 1.4.2 データ構造と復元時の挙動

`containers` と並列に `avatarReferences` を持つ。

```json
{
  "containers": [ ... ],
  "avatarReferences": [
    {
      "path": "Body",
      "blendShapes": [
        { "name": "貫通対策_胸", "weight": 100 }
      ],
      "materials": [
        { "slot": 0, "guid": "3f1a..." }
      ]
    }
  ]
}
```

**重要: コンテナとは復元の挙動が異なる。**

| 領域 | 復元時の挙動 |
|---|---|
| `containers`（ツール管理領域） | 破棄 → 再生成（JSON にないものは消える） |
| `avatarReferences`（アバター本体） | **上書きのみ**（JSON にない BlendShape は一切触らない） |

理由: アバター本体はユーザー領域であり、ツールが把握していない値を勝手にリセットすると事故につながるため。BlendShape は名前ベースで解決し、対象の名前が存在しなければ警告してスキップする。

### 1.4.3 シェーダー設定の複製方式（MVP）

マテリアルは共有アセットのため、直接書き換えると **同じマテリアルを使う全アバター・全シーンに波及する**。したがって複製方式を採る。

**データ構造:**

```json
{
  "materialSettings": [
    {
      "targetPath": "Body",
      "slot": 0,
      "sourceMaterialGuid": "8b2c1f...",
      "shader": "lilToon",
      "properties": [
        { "name": "_Color", "type": "color", "value": "1,1,1,1" },
        { "name": "_OutlineWidth", "type": "float", "value": "0.05" }
      ]
    }
  ]
}
```

**適用フロー:**

1. `sourceMaterialGuid` から元マテリアルを取得（**読み取りのみ、絶対に書き換えない**）。
2. 元マテリアルを複製し、生成物として保存。
3. 複製に対して `properties` の値を適用。
4. `Renderer.m_Materials[slot]` の参照を複製に向ける。

**生成物の運用:**

- **配置:** 元マテリアルと同じディレクトリ配下（どのアセットの派生物か自明になるため）。
  - 購入アセットのフォルダに書き込むため、衣装アップデート時に上書き・消失するリスクがある。消失時は再生成で復旧できる設計とする（元マテリアル + JSON があれば再現可能）。
- **GC:** 生成物にコミット ID を紐づけて記録し、**コミット削除時に紐づく生成物も併せて削除**する。
- **`.gitignore` の対象にしない:** 生成物は GUID を持つアセットであり、除外すると参照が切れるため、通常のアセットとして扱う。

**シェーダー対応:**

- プロパティ名はシェーダー固有のため、シェーダーごとの対応表を持つ。
- **MVP は liltoon のみ対応。** 他シェーダーはスコープ外とし、検出時は「未対応シェーダー」として警告表示・スキップする。
- 対応表は外部 JSON として持ち、将来のシェーダー追加をコード変更なしで行えるようにする。

**更新耐性（本機能の主な価値）:**

衣装がアップデートされ Prefab の構造が変わっても、`shader` と `properties.name` が一致すれば設定を再適用できる。「Prefab は置き直したが、色調整だけは前回の設定を当て直す」というワークフローを成立させる。

### 1.4.4 エクスポートとの関係（配布の安全性）

- **エクスポートされるのは JSON（設定ファイル）のみ**であり、生成された複製マテリアル等のアセットは一切含まれない。この原則は v1 から変わらない。
- BlendShape の配布は **値（weight）のみ**を対象とする。Mesh 本体や BlendShape そのものの追加は配布対象外（Mesh 改変となり、規約上も配布不可）。
- 将来的に共通規格化する場合も（PRD セクション14）、配布単位は値と GUID 参照に限定する。作者側で配布可否を設定できる仕組み（sensitive フラグ等）で制御する。

---

## 2. データモデル

### 2.1 コミット（チェックポイント）

Git の commit に相当。1つのコミットは「その時点の全コンテナの状態」を丸ごと保持する。

```json
{
  "schemaVersion": 2,
  "commitId": "c3f1a9e2",
  "parentCommitId": "b2e8d1c4",
  "branch": "hair-comparison",
  "message": "髪をロングに変更",
  "timestamp": "2026-08-20T15:00:00+09:00",
  "avatarGuid": "a1b2c3d4",
  "avatarName": "MyAvatar_v2",
  "containers": [
    {
      "containerId": "hair_long",
      "containerGuid": "e5f6a7b8",
      "prefabGuids": ["3f1a8e..."],
      "localPosition": { "x": 0, "y": 0, "z": 0 },
      "localRotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
      "localScale": { "x": 1, "y": 1, "z": 1 },
      "components": [
        {
          "path": "",
          "type": "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature",
          "fields": [
            { "key": "prefix", "value": "", "type": "string", "sensitive": false }
          ],
          "assetRefs": []
        }
      ]
    }
  ],
  "avatarReferences": [
    {
      "path": "Body",
      "blendShapes": [{ "name": "貫通対策_胸", "weight": 100 }],
      "materials": [{ "slot": 0, "guid": "3f1a..." }]
    }
  ],
  "materialSettings": [
    {
      "targetPath": "Body",
      "slot": 0,
      "sourceMaterialGuid": "8b2c1f...",
      "shader": "lilToon",
      "properties": [{ "name": "_OutlineWidth", "type": "float", "value": "0.05" }]
    }
  ],
  "assetVersions": [
    { "guid": "3f1a8e...", "assetName": "Outfit_A.prefab", "contentHash": "a3f2..." }
  ]
}
```

**v1 からの変更点:**
- 最上位が `containers` になり、その下に `components` がぶら下がる2階層構造に変更。
- 各コンテナが `prefabGuids`（複数 Prefab 対応）と Transform 情報を持つ。
- `containerGuid` / `avatarGuid` は名前変更に耐える同一性判定用（1.3.1、1.3.3）。
- `avatarReferences`（1.4.2）、`materialSettings`（1.4.3）、`assetVersions`（6.3）を追加。
- `components` の `path` はコンテナルートからの相対パス（空文字 = コンテナ直下）。

### 2.2 ブランチ

```json
{
  "branches": {
    "main": "c3f1a9e2",
    "hair-comparison": "d4a2b8f1"
  },
  "currentBranch": "main"
}
```

- ブランチ = 「コミットIDへのポインタ」という Git と同じモデル。
- **用途例:** `hair-long` ブランチと `hair-short` ブランチを作り、切り替えて見比べる。
- **マージは実装しない（MVP）。** ブランチ間の自動マージはコンフリクト解決の複雑さに見合わないため、「切り替えて比較する」用途に限定する。統合したい場合はユーザーが手動でコンテナを再構成する。

### 2.3 C# モデル

```csharp
namespace AvatarVcs.Core.Model
{
    [Serializable]
    public class Commit
    {
        public int schemaVersion = 2;
        public string commitId;
        public string parentCommitId;
        public string branch;
        public string message;
        public string timestamp;
        public string avatarName;
        public List<ContainerState> containers = new();
    }

    [Serializable]
    public class ContainerState
    {
        public string containerId;     // コンテナ GameObject 名と対応
        public string prefabGuid;      // 配置する Prefab
        public TransformState transform;
        public List<ComponentState> components = new();
    }

    [Serializable]
    public class ComponentState
    {
        public string path;            // コンテナルートからの相対パス
        public string type;
        public List<FieldValue> fields = new();
        public List<AssetRef> assetRefs = new();
    }
}
```

---

## 3. 主要操作

### 3.1 コミット（現在の状態を記録）

```csharp
public static Commit CreateCommit(GameObject avatarRoot, string message, string branch)
{
    var configRoot = avatarRoot.transform.Find("[AvatarVCS]");
    if (configRoot == null) return Commit.Empty(message, branch);

    var containers = new List<ContainerState>();
    foreach (Transform container in configRoot)
    {
        containers.Add(CaptureContainer(container));
    }

    return new Commit
    {
        commitId = GenerateId(),
        parentCommitId = HeadStore.GetHead(branch),
        branch = branch,
        message = message,
        timestamp = DateTime.Now.ToString("o"),
        containers = containers,
    };
}
```

- コンテナ配下の Prefab インスタンスから `PrefabUtility.GetCorrespondingObjectFromSource` で元 Prefab を辿り、GUID を記録する。
- コンテナ内の全コンポーネントを v1 と同じ `SerializedObject` 方式で取得する（この部分は v1 設計を流用）。

### 3.2 チェックアウト（復元 / ブランチ切り替え）

```csharp
public static CheckoutResult Checkout(Commit commit, GameObject avatarRoot)
{
    // 1. 事前検証: 必要な Prefab が全て存在するか
    var missing = commit.containers
        .Where(c => AssetDatabase.GUIDToAssetPath(c.prefabGuid) == "")
        .ToList();
    if (missing.Any()) return CheckoutResult.MissingPrefabs(missing);

    // 2. 自動コミット（現在の状態を退避）
    AutoCommitBeforeCheckout(avatarRoot);

    // 3. 管理ルート配下を全破棄
    var configRoot = EnsureConfigRoot(avatarRoot);
    foreach (Transform child in configRoot.Cast<Transform>().ToList())
        Undo.DestroyObjectImmediate(child.gameObject);

    // 4. コミット定義から再生成
    foreach (var container in commit.containers)
        InstantiateContainer(container, configRoot);

    return CheckoutResult.Success();
}
```

**設計上の要点:**
- **事前検証を必ず先に行う。** Prefab が1つでも欠けていたら破棄処理に入る前に中断する（破棄してから失敗すると復元不能になるため）。
- **破棄前に自動コミット。** 「戻したがやはり戻す前が良かった」に対応（v1 の設計を踏襲）。
- `Undo.DestroyObjectImmediate` / `Undo.RegisterCreatedObjectUndo` を使い、Unity 標準の Undo にも最低限乗せる。

### 3.3 差分表示（コミット間の比較）

Git の `diff` に相当。ブランチ比較（髪A vs 髪B）の主機能。

```csharp
public class ContainerDiff
{
    public string containerId;
    public DiffKind kind;          // Added / Removed / Changed / Unchanged
    public string prefabNameBefore;
    public string prefabNameAfter;
    public List<FieldDiff> fieldDiffs = new();
}
```

- **コンテナ単位の差分**（髪Aコンテナ → 髪Bコンテナに置き換わった、など）をまず表示し、展開すると内部のフィールド差分が見える2階層構成にする。
- v1 で設計した「ドリフト検知」（手動変更の警告）は、**現在のシーン状態 vs HEAD コミット**の差分として表示する（Git の `git status` 相当）。

---

## 4. ストレージ設計

```
ProjectSettings/AvatarVcs/
├── guid-remapping.json            # プロジェクト全体の GUID 再マッピング（6.4）
└── avatars/
    ├── a1b2c3d4/                  # アバター単位で分離（1.3.3）
    │   ├── config.json            # currentBranch, branches マップ
    │   ├── index.json             # コミット一覧のメタ情報
    │   └── commits/
    │       ├── c3f1a9e2.json
    │       └── b2e8d1c4.json
    └── f9e8d7c6/
        └── ...
```

- コミットは**フルスナップショット**で保存する（KB オーダーなので差分保存の最適化は不要、v1 の判断を踏襲）。
- `parentCommitId` を辿ることで履歴グラフを構築し、UI でツリー表示する。
- **孤児コミットの GC は実装しない（MVP）。** ブランチから到達不能なコミットも残す（容量が問題にならないため、むしろ復元可能性を優先）。
- **生成マテリアル（1.4.3）の紐づけ:** 各コミットに `generatedAssets` として生成物の GUID リストを記録し、コミット削除時に併せて削除する。

---

## 5. UI 設計

### 5.1 メインウィンドウ構成

```
┌─────────────────────────────────────────┐
│ Branch: [main ▼]  [+ New Branch]        │
├──────────────┬──────────────────────────┤
│ コミット履歴  │ 差分ビュー                │
│ ● 髪をロング  │ ~ hair コンテナ           │
│ │            │   - Hair_Short.prefab     │
│ ● 衣装A追加   │   + Hair_Long.prefab      │
│ │            │ = outfit_a コンテナ (変更なし)│
│ ● 初期状態    │                          │
├──────────────┴──────────────────────────┤
│ [Commit] [Checkout] [Compare]            │
└─────────────────────────────────────────┘
```

- 左: コミット履歴ツリー（ブランチ分岐を視覚化）。
- 右: 選択したコミットと現在の状態、またはコミット同士の差分。
- 未コミットの変更がある場合、上部に `git status` 相当の警告バーを表示。

### 5.2 ブランチ比較モード

「髪だけ比較したい」という主要ユースケースに対応する専用モード。

- 2つのブランチ（またはコミット）を選び、**交互に checkout して見比べる**トグルボタンを提供する。
- 切り替えのたびに自動コミットが走ると履歴が汚れるため、**比較モード中は自動コミットを抑制**し、変更があった場合のみ終了時に確認する。

---

## 6. アセット更新への対応

### 6.1 構造的な限界（先に明示する）

本ツールは **アセット実体を持たない**（軽量さと権利面の安全性の根拠）。したがって「過去のバージョンの Prefab に戻す」ことは **原理的にできない**。Git がファイル内容を全て保持して過去を完全再現できるのに対し、ここが本ツールの明確な境界である。

この点はドキュメントに明記し、旧バージョンを保持したいユーザーには自身でのアセットバックアップを案内する。

### 6.2 ケース別の挙動と対策

| ケース | 挙動 | 対策 |
|---|---|---|
| Prefab が上書き更新（GUID 維持） | 新しい Prefab で再生成される | **バージョン記録と警告**（6.3） |
| 新フォルダにインポート（GUID 変化） | GUID 解決失敗 → checkout 不能 | **GUID 再マッピング UI**（6.4） |
| ボーン構成・名前が変わった | パス解決失敗 → 設定が当たらない | `Unresolvable` として部分適用 |
| BlendShape 名が変わった | 名前解決失敗 | 警告してスキップ（1.4.2） |
| **シェーダー設定** | **プロパティ名が安定しているため影響を受けにくい** | **そのまま再適用可能（1.4.1）** |

### 6.3 バージョン記録と警告

コミット時に、参照している各アセットの状態を記録する。

```json
{
  "assetVersions": [
    {
      "guid": "3f1a8e...",
      "assetName": "Outfit_A.prefab",
      "contentHash": "a3f2...",
      "recordedAt": "2026-08-20T15:00:00+09:00"
    }
  ]
}
```

- checkout 時に現在のアセットとハッシュを比較し、不一致なら警告を表示する（例: 「このコミットは記録時点のバージョンと異なる Outfit_A を参照します」）。
- **復元は続行できる**（阻止しない）。ユーザーに「なぜ見た目がズレるのか」を理解させることが目的。

### 6.4 GUID 再マッピング UI

衣装を別フォルダに再インポートすると GUID が変わり、**過去のコミットが全て解決不能になる**。これを救済する仕組みは実質必須。

- checkout 時に未解決 GUID を検出したら、再マッピング UI を表示する。
- ユーザーが「旧 GUID → 現在のアセット」を手動で指定する。
- **マッピングはプロジェクト単位で永続化**し、以後のすべてのコミットに自動適用する（毎回聞かない）。

```json
{
  "guidRemapping": {
    "3f1a8e...": "9c4d2b..."
  }
}
```

---

## 7. PoC 実装計画（v2）

### 7.1 フェーズ1: 中核仮説の検証（最優先）

| # | タスク | 完了条件 |
|---|---|---|
| 1 | 管理ルート `[AvatarVCS]` とコンテナの生成・検出 | マーカーコンポーネント付きで生成され、再実行時に重複しない |
| 2 | Prefab インスタンスの GUID 取得 | `GetCorrespondingObjectFromSource` で元 Prefab の GUID が取れる |
| 3 | **コンテナの破棄→再生成（冪等性の実証）** | 同じコミットを2回 checkout しても結果が同一 |
| 4 | Transform 情報の記録・復元 | 位置/回転/スケールが正しく再現される |

**タスク3が本設計の中核仮説であり、ここが崩れると設計全体を見直す必要がある。最優先で検証する。**

### 7.2 フェーズ2: 設定の記録・復元

| # | タスク | 完了条件 |
|---|---|---|
| 5 | コンテナ内コンポーネントの取得・適用 | v1 設計の `SerializedObject` 方式が2階層構造でも動く |
| 6 | BlendShape 値の記録・復元 | 名前ベースで解決し、JSON にない BlendShape は変化しない |
| 7 | マテリアル参照の記録・復元 | `Renderer.m_Materials` の GUID 差し替えが元マテリアルに影響しない |
| 8 | **liltoon マテリアル設定の複製・適用** | 元マテリアルが無変更のまま、複製に設定が適用され参照が向く |

### 7.3 フェーズ3: バージョン管理機能

| # | タスク | 完了条件 |
|---|---|---|
| 9 | コミット・履歴の永続化 | ファイルベースで保存され、一覧表示できる |
| 10 | ブランチ切り替え | 2ブランチ間を往復して、それぞれの状態が正しく復元される |
| 11 | 差分表示 | コンテナ単位の差分が構造化して表示される |

### 7.4 フェーズ4: 堅牢性

| # | タスク | 完了条件 |
|---|---|---|
| 12 | アセットバージョンの記録・警告 | ハッシュ不一致時に警告が出る |
| 13 | GUID 再マッピング UI | 未解決 GUID をユーザーが手動対応でき、以後自動適用される |

---

## 8. v1 から引き継ぐ設計

以下は v1 設計書（`DesignDoc_v1_superseded.md`）の内容をそのまま流用する:

- `SerializedObject` / `SerializedProperty` によるコンポーネント値の取得・適用（v1 セクション4）
- GUID + localId によるアセット参照の解決（v1 セクション3.2）
- Prefab オーバーライドの整合性確保（`RecordPrefabInstancePropertyModifications`、v1 セクション3.3）
- sensitive フィールドのマスキングと内部/エクスポート用シリアライザの分離（v1 セクション8）
- 選択的エクスポート（v1 セクション8.5）— コンテナ単位の切り出しとして自然に対応可能

---

## 9. 未解決の論点（v2）

- 複数アバターを1プロジェクトで扱う場合、コミット履歴をアバターごとに分離するか統合するか。
- ブランチのマージを将来サポートするか（コンテナ単位の選択式マージなら実装は現実的。3-way マージは不要）。
- アバター本体側の変更（スコープ外）をユーザーがどう認識するか。警告の出し方。
- 生成マテリアル（v1.1）が衣装アップデートで消失した場合の再生成トリガーをどう設計するか。
- コンテナの命名はユーザーが自由に付ける方針で確定。命名重複時のハンドリングのみ要検討。
