# 詳細設計書: VRChat アバター改変 設定管理ツール（仮称）

> **【廃止】** この設計書は v1 であり、`DesignDoc_avatar-vcs.md`（v2）に置き換えられました。判断の経緯を追う目的でのみ参照してください。

> 対応PRD: `PRD_avatar_config_tool.md`。本ドキュメントはPRDセクション8（技術方針）を実装可能なレベルまで具体化したもの。PoC（PRDセクション12）の実装ガイドとして使う。

---

## 1. モジュール構成

Unity Editor拡張としての内部構造（PRD 8.0）に対応する形で、名前空間/フォルダを分離する。

```
Packages/com.yourname.avatarconfig/
├── Runtime/                      # 実行時に含めないため基本空。将来ランタイム参照が必要な場合のみ使用
├── Editor/
│   ├── Core/                     # ドメインロジック（Unity Editor APIに依存しすぎない層）
│   │   ├── Model/
│   │   │   ├── AvatarConfigSnapshot.cs
│   │   │   ├── ComponentState.cs
│   │   │   ├── FieldValue.cs
│   │   │   └── SchemaVersion.cs
│   │   ├── Serialization/
│   │   │   ├── SnapshotJsonSerializer.cs      # 内部保持用（フル情報）
│   │   │   ├── SnapshotExportSerializer.cs    # エクスポート用（sensitive除外）
│   │   │   └── ReferenceResolver.cs           # パス/GUID解決
│   │   ├── Diff/
│   │   │   ├── SnapshotDiffer.cs
│   │   │   ├── DiffEntry.cs
│   │   │   └── DiffKind.cs (Added/Changed/Unresolvable/Unchanged)
│   │   └── Capture/
│   │       ├── ComponentCapturer.cs           # SerializedObject→ComponentState
│   │       └── ComponentApplier.cs            # ComponentState→SerializedObject
│   ├── NdmfPlugin/
│   │   └── AvatarConfigNdmfPlugin.cs          # ビルド時フック
│   ├── UI/
│   │   ├── SnapshotManagerWindow.cs           # メイン管理画面
│   │   ├── DiffPreviewView.cs                 # plan相当のプレビューUI
│   │   ├── HistoryPanelView.cs                # チェックポイント一覧
│   │   └── SensitiveFieldEditorView.cs
│   ├── Storage/
│   │   └── LocalHistoryStore.cs               # ローカル履歴の永続化
│   └── package.json / AvatarConfig.asmdef
└── Schema/
    └── avatarconfig.schema.json               # JSON Schema（セクション14対応）
```

**設計原則:** `Core/` はできる限り `UnityEditor` 名前空間に依存させず、`SerializedObject` を扱うのは `Capture/` に閉じ込める。これにより将来ユニットテストを書きやすくし、Unity依存部分の変更影響範囲を限定する。

---

## 2. データスキーマ

### 2.1 JSON構造（内部保持用）

```json
{
  "schemaVersion": 1,
  "avatarName": "MyAvatar_v2",
  "checkpointLabel": "衣装A装着後・軽量化前",
  "timestamp": "2026-08-20T10:00:00+09:00",
  "toolVersion": "0.1.0",
  "supportedComponentRange": {
    "modularAvatar": ">=1.10.0 <2.0.0",
    "avatarOptimizer": ">=1.7.0 <2.0.0"
  },
  "components": [
    {
      "path": "Body/Armature/Hip/Chest",
      "type": "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature",
      "fields": [
        { "key": "prefix", "value": "", "type": "string", "sensitive": false },
        { "key": "matchAvatarWriteDefaults", "value": "true", "type": "bool", "sensitive": false }
      ],
      "assetRefs": [
        { "key": "m_Mesh", "guid": "3f1a...", "localId": 4300000, "sensitive": false }
      ]
    }
  ],
  "scaleOffset": { "x": 1.02, "y": 1.0, "z": 1.02, "sensitive": true }
}
```

**設計判断:**
- `fields` は key/value/type/sensitive の**配列**で持つ（Dictionaryを直接JSON化しない）。理由: Newtonsoft.Jsonでも良いが、配列にしておくと将来スキーマの後方互換を保ちやすく、順序も安定する（PRD 8節のJsonUtility制約の議論を踏まえ、配列形式に統一して両対応可能にする）。
- `type` フィールドで値の型情報を保持し、apply時の型変換ミスを防ぐ。
- `assetRefs` は `fields` と分離し、GUID参照であることを明示する（レビューで指摘された「参照の安定化」を型レベルで強制）。
- `supportedComponentRange` はPRD 8節の「対応バージョン範囲」をJSON側にも記録し、apply時に警告判定できるようにする。

### 2.2 C#モデル定義

```csharp
namespace AvatarConfig.Core.Model
{
    [Serializable]
    public class AvatarConfigSnapshot
    {
        public int schemaVersion = 1;
        public string avatarName;
        public string checkpointLabel;
        public string timestamp;      // ISO 8601
        public string toolVersion;
        public SupportedRange supportedComponentRange;
        public List<ComponentState> components = new();
        public ScaleOffset scaleOffset;
    }

    [Serializable]
    public class ComponentState
    {
        public string path;           // Transform相対パス
        public string type;           // フルクラス名
        public List<FieldValue> fields = new();
        public List<AssetRef> assetRefs = new();
    }

    [Serializable]
    public class FieldValue
    {
        public string key;
        public string value;          // 常に文字列化して保持（型はtypeで判別）
        public string type;           // "string" | "bool" | "float" | "int" | "enum" など
        public bool sensitive;
    }

    [Serializable]
    public class AssetRef
    {
        public string key;
        public string guid;
        public long localId;
        public bool sensitive;
    }
}
```

---

## 3. 参照解決の設計（ReferenceResolver）

PRD 8節・レビュー指摘（Transform相対パスの脆さ、Prefabオーバーライド整合性）を踏まえた設計。

### 3.1 パス解決ルール

1. **基準点はアバタールート（`VRCAvatarDescriptor` が付いたGameObject）。** ここからの相対パスで全参照を表現する。
2. パス文字列は `/` 区切り、Unityの `Transform.Find` と互換の形式にする。
3. **解決失敗時のフォールバック順序（MVP範囲）:**
   - 完全一致 → 失敗なら **解決不能** として扱い、apply時にプレビューへ表示（6.2相当）。
   - ファジーマッチ（名前の類似度判定など）は **MVP範囲外**（PRD未解決論点、v1.x検討）。

### 3.2 GUID解決ルール

- `AssetDatabase.GUIDToAssetPath` で実体確認。存在しなければ「アセット未所持」として解決不能扱い。
- `localId`（旧FileID相当）まで一致させ、同一GUID内の複数サブアセット（例: FBX内の複数マテリアル）を区別する。

### 3.3 Prefabオーバーライド整合性（PoC検証必須項目）

- `PrefabUtility.RecordPrefabInstancePropertyModifications` を明示的に呼び出し、apply時の変更がインスタンスのオーバーライドとして記録されることを保証する。
- Prefabアセット本体（Project上の `.prefab`）を直接編集してしまわないよう、**apply対象は常にシーン上のインスタンスに限定**する（Prefabモード編集中の誤爆を防ぐガード処理を`ComponentApplier`に入れる）。

---

## 4. コンポーネント取得/適用（Capture / Apply）

### 4.1 取得（ComponentCapturer）

```csharp
public static ComponentState Capture(Component component, Transform avatarRoot)
{
    var so = new SerializedObject(component);
    var state = new ComponentState
    {
        path = GetRelativePath(component.transform, avatarRoot),
        type = component.GetType().FullName,
    };

    var prop = so.GetIterator();
    bool enterChildren = true;
    while (prop.NextVisible(enterChildren))
    {
        enterChildren = false;
        if (ShouldSkip(prop)) continue; // m_Script等の除外ルール

        if (prop.propertyType == SerializedPropertyType.ObjectReference)
        {
            state.assetRefs.Add(CaptureAssetRef(prop));
        }
        else
        {
            state.fields.Add(CaptureFieldValue(prop));
        }
    }
    return state;
}
```

- **除外ルール（`ShouldSkip`）:** `m_Script`（コンポーネント種別はtypeで既に持っているため不要）、Unity内部専用フィールド（`m_ObjectHideFlags`等）は明示的にスキップリストで除外。
- **未知フィールドの扱い（PRD 8節対応）:** apply時、JSON側に存在するがコンポーネント側に存在しない `key` があれば `ComponentApplier` が警告ログを出す（無視はしない）。

### 4.2 適用（ComponentApplier）

```csharp
public static ApplyResult Apply(ComponentState state, GameObject targetRoot)
{
    var target = ResolvePath(state.path, targetRoot);
    if (target == null) return ApplyResult.PathUnresolved(state.path);

    var component = target.GetComponent(Type.GetType(state.type));
    if (component == null) return ApplyResult.ComponentMissing(state);

    Undo.RecordObject(component, "AvatarConfig Apply"); // Unity標準Undoとの最小限の連携
    var so = new SerializedObject(component);

    foreach (var field in state.fields)
    {
        var prop = so.FindProperty(field.key);
        if (prop == null) { Log.Warn($"未知フィールド: {field.key}"); continue; }
        WriteValue(prop, field);
    }
    foreach (var assetRef in state.assetRefs)
    {
        var prop = so.FindProperty(assetRef.key);
        var asset = ResolveAsset(assetRef.guid, assetRef.localId);
        if (asset == null) { /* 解決不能として記録 */ continue; }
        prop.objectReferenceValue = asset;
    }

    so.ApplyModifiedProperties();
    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    return ApplyResult.Success(state.path);
}
```

**Undo統合について（PRD 6.3の決定を反映）:** `Undo.RecordObject` は最小限（Unity側の`Ctrl+Z`で直前のapply操作を一応取り消せる程度）にとどめ、ツールの独自履歴機能（`LocalHistoryStore`）をメインの復元手段として位置づける。二重管理で混乱しないよう、UI上では「元に戻すのは履歴パネルから」と明示する。

### 4.3 Apply モード（Merge / Replace）

**設計上の重要な区別。** 単に「JSONに書いてあるものを書き込む」だけでは復元要件を満たせない（後から追加されたコンポーネントが残ってしまうため）。以下2モードを明確に分ける。

| モード | 挙動 | Terraform対応 | 主な用途 |
|---|---|---|---|
| **Merge（追記）** | JSON に存在するものだけ適用。JSON にないものは一切触らない | `-target` 的な部分適用 | 別アバターへの横展開（PRD 6.2） |
| **Replace（収束）** | JSON の状態に完全一致させる。管理範囲内で JSON にないコンポーネントは**削除** | 通常の `terraform apply`（desired state への収束） | チェックポイントからの復元（PRD 6.3） |

```csharp
public enum ApplyMode { Merge, Replace }
```

### 4.4 復元（Replace モード）の安全設計

Replace で無条件削除すると事故るため、以下3点をセットで実装する。

**1. 管理対象範囲の記録**

スナップショット取得時に「本ツールが把握しているコンポーネントの集合」を `managedScope` として JSON に保存する。

```json
{
  "managedScope": [
    { "path": "Body/Armature/Hip/Chest", "type": "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature" }
  ]
}
```

Replace 時の削除候補は **`managedScope` に含まれる、かつ現在のシーンに存在する、かつ復元先 JSON の `components` に存在しないもの** に限定する。ツールが関知しないうちに他ツールやユーザーが追加したコンポーネントは削除対象外とする（誤爆防止）。

**2. `DiffKind` に `Removed` を追加**

```csharp
public enum DiffKind { Added, Changed, Removed, Unresolvable, Unchanged }
```

`DiffPreviewView` では `Removed` の行を赤字・削除アイコン付きで強調表示し、「この操作で N 件のコンポーネントが削除されます」というサマリを別枠で明示する。

**3. 削除の明示的確認**

削除が1件でも含まれる場合、確認チェックボックス（「N件の削除を理解しました」）にチェックしないと apply ボタンを有効化しない。値の変更のみの場合はこの確認を省略してよい（過剰な確認ダイアログは形骸化するため、削除時のみに限定する）。

**4. 復元前の自動チェックポイント**

Replace 実行の直前に、現在の状態を自動でスナップショットして履歴に保存する（ラベル例: `[自動] 復元前の状態 2026-08-20 15:00`）。これにより「復元したがやはり戻す前が良かった」に対応できる。自動チェックポイントは UI 上で通常のチェックポイントと視覚的に区別する（グレー表示など）。

## 4.5 GameObject / Prefab 配置の扱い（重要な設計上の限界）

**現在のデータモデルは「既存の GameObject に付いたコンポーネントのパラメータ」を対象としており、GameObject そのものの生成・Prefab のインスタンス化は対象外である。** この区別を曖昧にすると実装が破綻するため、明示的に整理する。

### 4.5.1 できること / できないこと

| 対象 | MVP | 備考 |
|---|---|---|
| 既存コンポーネントのパラメータ変更 | ✅ | `SerializedObject` 経由の書き込み |
| 既存 GameObject へのコンポーネント追加 | ✅ | `DiffKind.Added`、`AddComponent` で対応可 |
| 管理範囲内のコンポーネント削除 | ✅ | Replace モード（4.4） |
| **Prefab（衣装など）のインスタンス化・配置** | ❌ | 4.5.2 参照 |
| **GameObject の新規生成・親子関係の変更** | ❌ | 同上 |

### 4.5.2 Prefab 配置を MVP から外す理由

- **アセット実体への依存が発生する。** 「この衣装 Prefab を配置する」を JSON で表現するには GUID 参照を持つことになるが、これは PRD の「アセット非包含」原則そのものは満たすものの、**受け手がその Prefab を所持していなければ何も起きない**ため、体験として成立しにくい。
- **Transform（位置・回転・スケール）の記録が必要になり、データモデルが一段複雑化する。** 現在の `ComponentState` は「パス上の既存オブジェクト」を前提としているが、新規生成では「どこに、どの親の下に、どんな Transform で」という情報が追加で必要になる。
- **冪等性の担保が難しい。** 同じ JSON を2回 apply したとき、Prefab が2つ配置されないことを保証する仕組み（インスタンス識別子の付与など）が別途必要になる。

### 4.5.3 実運用上の想定ワークフロー（MVP）

MVP では以下を前提とする。この前提はユーザー向けドキュメントにも明記する。

1. **ユーザーが衣装 Prefab を手動でアバター配下に配置する**（ここは既存の Modular Avatar のワークフローそのまま）。
2. 本ツールはその状態から、**MA/AAO のパラメータ設定のみ**をスナップショット・apply する。

つまり本ツールの守備範囲は「配置済みの構成に対する設定の再現」であり、「構成そのものの自動生成」ではない。PRD 1.1 の「再現可能なインスタンス化」も、この範囲での再現を指す。

### 4.5.4 将来拡張（v1.x 検討）

Prefab 配置まで含めた完全な宣言的構成管理を目指す場合、以下が必要になる:

```json
{
  "prefabInstances": [
    {
      "instanceKey": "outfit_a_001",        // 冪等性のための一意キー
      "prefabGuid": "3f1a...",
      "parentPath": "Armature",
      "localPosition": { "x": 0, "y": 0, "z": 0 },
      "localRotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
      "localScale": { "x": 1, "y": 1, "z": 1 }
    }
  ]
}
```

- `instanceKey` を GameObject 側にマーカーコンポーネントとして埋め込み、2回目の apply では既存インスタンスを再利用することで冪等性を担保する。
- ただしこれは実装コストが大きく、PRD の YAGNI 方針に照らして MVP では採用しない。ユーザーからの要望が実際に多かった場合に v1.x で検討する。

---

## 5. 差分計算（SnapshotDiffer）

PRD 6.2で要求された構造化プレビューを生成する。

```csharp
public class DiffEntry
{
    public string path;
    public string componentType;
    public string fieldKey;
    public string beforeValue;   // 現在のシーンの値（driftしていればここが変わる）
    public string afterValue;    // JSON側の値
    public DiffKind kind;        // Added / Changed / Unresolvable / Unchanged
    public string unresolvedReason; // "PathNotFound" | "ComponentMissing" | "AssetGuidNotFound" 等
}
```

**アルゴリズム概要:**
1. JSON側の各 `ComponentState` について、対象パスを現在のシーンから解決を試みる。
2. 解決できた場合、現在の `SerializedObject` の値と JSON の値を1フィールドずつ比較。
   - 値が違う → `Changed`（ドリフト検知はここに包含される。現在の値が「手動変更された値」であっても区別なく `Changed` として警告表示に含める）
   - コンポーネント自体が存在しない → `Added`
3. 解決できない場合 → `Unresolvable`、理由を記録。
4. UI（`DiffPreviewView`）はこの `List<DiffEntry>` をテーブル表示し、`Changed` の行は「現在の値 → JSON側の値」を強調表示してドリフト上書きであることを視覚的に警告する（PRD 6.2のドリフト検知要件に対応）。

---

## 6. NDMFプラグインのライフサイクル

```csharp
namespace AvatarConfig.NdmfPlugin
{
    public class AvatarConfigNdmfPlugin : Plugin<AvatarConfigNdmfPlugin>
    {
        public override string DisplayName => "Avatar Config Manager";

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .Run("Apply pending AvatarConfig snapshot", ctx =>
                {
                    // シーン上の「適用予約」コンポーネントを検出し、
                    // ビルド時に確定的にapplyを実行する（GUI経由の即時applyとは別経路）。
                    var marker = ctx.AvatarRootObject.GetComponent<PendingSnapshotMarker>();
                    if (marker == null) return;
                    ComponentApplier.ApplyAll(marker.Snapshot, ctx.AvatarRootObject);
                });
        }
    }
}
```

**設計意図:** GUI操作で即座にapplyする経路（EditorWindowから直接 `ComponentApplier` を呼ぶ）と、NDMFのビルド時に確定させる経路の2つを用意することで、「ビルドのたびに必ずJSON定義通りの状態に収束させたい」という宣言的モデル（PRD 1.1のTerraformアナロジー）を維持できる。MVPではGUI即時apply経路を優先実装し、NDMF経由の自動収束は任意機能として後段で検討（過剰実装を避ける）。

---

## 7. ローカル履歴の永続化（LocalHistoryStore）

- 保存先: `ProjectSettings/AvatarConfigHistory/` 配下に、チェックポイントごとの `.json` ファイル（`{timestamp}_{label}.json`）として保存。
- **DBやSQLiteは使わない**（PRD容量要件はKBオーダーなので、ファイルベースで十分。依存ライブラリを増やさない方針）。
- 一覧表示は起動時にディレクトリをスキャンしてインデックス化（`index.json` にメタデータのみ集約してI/O回数を削減）。

```
ProjectSettings/AvatarConfigHistory/
├── index.json                          # {label, timestamp, filename}[]
├── 20260820T100000_衣装A装着後.json
└── 20260820T110000_軽量化調整前.json
```

**注意:** `ProjectSettings/` はプロジェクトごとに存在するため、複数アバタープロジェクトを横断した履歴管理はMVP範囲外（PRDペルソナAの「多頭飼い横断」ニーズは将来、履歴ファイルのプロジェクト間コピー機能等で補う）。

---

## 8. エクスポート処理の分離（PRD 6.4対応）

```csharp
public static class SnapshotExportSerializer
{
    public static string ToExportJson(AvatarConfigSnapshot snapshot, ExportOptions options)
    {
        var clone = DeepCloneWithoutSensitive(snapshot, options.IncludeSensitive);
        return JsonConvert.SerializeObject(clone, Formatting.Indented);
    }

    private static AvatarConfigSnapshot DeepCloneWithoutSensitive(AvatarConfigSnapshot src, bool includeSensitive)
    {
        // fields/assetRefsのうちsensitive=trueのものを除外 or ダミー値化
        // includeSensitiveがtrueの場合のみ、明示的な警告UIを経た上で含める
    }
}
```

`SnapshotJsonSerializer`（内部保持用）とは**別クラス**として明確に分離し、同一シリアライザの条件分岐で出し分けない（レビュー指摘を反映、実装ミスによる情報漏洩を構造的に防ぐ）。

---

## 8.5 選択的エクスポート（Partial Export）

PRD 6.6 対応。`SnapshotExportSerializer` を拡張し、エクスポート対象を絞り込む。

### 8.5.1 選択構造

```csharp
public class ExportSelection
{
    // MVP: コンポーネント単位（path + type の組でユニークに特定）
    public HashSet<(string path, string type)> IncludedComponents = new();

    // v1.1: フィールド/配列要素単位まで絞る場合に使用
    public Dictionary<(string path, string type), HashSet<string>> IncludedFieldKeys = new();
}
```

```csharp
public static string ToExportJson(AvatarConfigSnapshot snapshot, ExportSelection selection, ExportOptions options)
{
    var filtered = snapshot.components
        .Where(c => selection.IncludedComponents.Contains((c.path, c.type)))
        .Select(c => ApplyFieldSelection(c, selection))  // v1.1: フィールド単位フィルタ
        .ToList();

    var clone = new AvatarConfigSnapshot { /* ...snapshotのメタ情報をコピー... */ components = filtered };
    return SnapshotExportSerializer.ToExportJson(clone, options); // sensitive除外は既存ロジックを再利用
}
```

**設計判断:** 選択フィルタは既存の `SnapshotExportSerializer`（sensitive除外）の**前段**に挟む形にする。「選択されなかったもの」と「sensitiveでマスクされたもの」は扱いが異なる（前者は配列から完全除去、後者はマスク/ダミー値化）ため、処理を混同しないよう別ステップとして明確に分離する。

### 8.5.2 BlendShape（シェイプキー）の配列要素単位選択（v1.1向け設計メモ）

`SkinnedMeshRenderer` の `m_BlendShapeWeights` はインデックスベースの配列で、Mesh側の `GetBlendShapeName(index)` と対応付けないと「どのシェイプキーか」が分からない。v1.1では `ComponentCapturer` 側でBlendShape用の特別ハンドリングを追加する:

```csharp
// 通常のfieldsとは別に、名前解決済みのBlendShape専用リストを持たせる案
public class BlendShapeEntry
{
    public string shapeName;   // Mesh.GetBlendShapeName(index) で解決
    public int index;          // 元の配列インデックス（適用時の再解決に使用、Mesh側の並び順が変わらない前提）
    public float weight;
    public bool sensitive;
}
```

- **注意点:** Mesh側のシェイプキー並び順は基本的に安定しているが、Mesh自体が差し替えられた場合はインデックスがずれる可能性がある。適用時は `index` より **`shapeName` を優先して名前ベースで解決**し、`index` はフォールバック用に留める。
- この専用ハンドリングにより、UI（`SensitiveFieldEditorView` 相当のツリー）でシェイプキー名を一覧表示し、個別にチェックボックスで選択・sensitiveフラグ付与ができるようになる。

---

## 9. PoC実装計画（具体タスク分解）

PRDマイルストーンの「PoC」を、実装可能な単位まで分解する。

| # | タスク | 検証内容 | 完了条件 |
|---|---|---|---|
| 1 | `ComponentCapturer` の最小実装 | `ModularAvatarMergeArmature` 1種類のみ対象にSerializedObjectから全フィールド取得 | JSON文字列として出力できる |
| 2 | `ComponentApplier` の最小実装 | 取得したJSONを同一コンポーネントに書き戻す | Inspector上で値が変化することを目視確認 |
| 3 | Transform相対パスの解決 | 異なるGameObject階層間でのパス解決 | 別アバターのシーンに同じコンポーネント構成があれば適用できる |
| 4 | **Prefabオーバーライド確認** | applyした変更がPrefabのオーバーライドとして記録されるか | Prefabアセット本体が意図せず変更されていないことを確認 |
| 5 | GUID参照の解決 | マテリアル参照フィールドを含むコンポーネントで検証 | GUID不一致時に解決不能として検出できる |
| 6 | 差分計算の最小実装 | 1フィールド変更時に `Changed` として検出 | `DiffEntry` のリストが正しく生成される |
| 7 | BlendShape名の解決確認（8.5.2向け） | `Mesh.GetBlendShapeName(index)` で名前とインデックスの対応が安定して取れるか | 同一Meshで複数回取得しても順序・名前が一致することを確認 |

**この6項目が通れば、PRDの技術方針（セクション8）の前提が崩れないことが確認でき、MVP実装に進める。**

---

## 10. 未確定事項（実装しながら決める）

- `WriteValue` での型変換ロジック（enum、Vector3などUnity特有型の文字列表現ルール）は、実装しながら `SerializedPropertyType` ごとのハンドラを追加していく方式にする（最初から全網羅を狙わない）。
- `Undo.RecordObject` の粒度（1フィールドごとか、apply全体でグループ化するか）はPoC中に使用感を見て決定。
