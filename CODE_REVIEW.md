# Code Review: avatar-vcs (Phase 1 & Phase 2)

**レビュー実施日**: 2026-08-20  
**対象コミット**: `351c684` (main)  
**対象範囲**: Packages/dev.avatarvcs.avatar-vcs/ (Runtime, Editor, Tests)

---

## 1. 総括・アーキテクチャ評価

[PRD_avatar-vcs.md](file:///home/katu25/avatar-vcs/PRD_avatar-vcs.md) および [DesignDoc_avatar-vcs.md](file:///home/katu25/avatar-vcs/DesignDoc_avatar-vcs.md) で定義された **「管理下コンテナ方式による冪等性の担保（破棄→再生成）」** と **「アバター本体への非破壊な設定・マテリアル複製適用」** が、設計意図に忠実にクリーンかつ堅牢に実装されています。

- **Phase 1（中核仮説の検証）**: ルート/コンテナ生成・重複防止、Prefab GUID 取得、破棄→再生成による冪等性実証、Transform 再現が完全に動作。
- **Phase 2（設定の記録・復元）**: SerializedObject 経由のコンポーネント設定、BlendShape / Material スロットのホワイトリスト管理、lilToon マテリアル設定の複製・適用が網羅され、ユニットテストで検証済み。

---

## 2. 優れた実装ポイント

### 2.1 冪等性と境界分離の徹底
* **物理的な境界分離**: [`ContainerManager`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Core/ContainerManager.cs) により、ツール管理領域 `[AvatarVCS]` とユーザー領域（Body / Armature 等）が物理的に分離され、コンテナの `DestroyImmediate` → 再生成というシンプルなモデルで状態収束を実現。
* **安全なマテリアル複製**: [`MaterialSettingsApplier`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/MaterialSettings/MaterialSettingsApplier.cs) において、元マテリアルを一切変更せず、複製（`*_avatarvcs.mat`）を作成して適用する方式が徹底されており、共有アセットへの意図しない副作用を防止。
* **上書き専用の本体プロパティ管理**: [`AvatarReferenceApplier`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/AvatarReferences/AvatarReferenceApplier.cs) では、JSON に定義されたプロパティのみを上書きし、未記載の BlendShape や Material をリセットしない仕様が順守されている。

### 2.2 Unity エディタ拡張としての品質
* **ロケール非依存の文字列エンコード**: [`FieldCodec`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Reflection/FieldCodec.cs) およびマテリアル設定部で `CultureInfo.InvariantCulture` が一貫して使用されており、OS ロケール起因の浮動小数点パースバグ（カンマ/ピリオド問題）を完全に防止。
* **Undo システムへの配慮**: `Undo.RegisterCreatedObjectUndo`、`Undo.DestroyObjectImmediate`、`Undo.RecordObject`、`Undo.SetTransformParent` が適切に組み込まれている。
* **Prefab モディフィケーションの記録**: [`ComponentApplier`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Apply/ComponentApplier.cs) で `PrefabUtility.RecordPrefabInstancePropertyModifications` が呼ばれており、シーン/プレハブ上の変更追跡が正常に機能。
* **テストの安定性**: `OneTimeSetUp` / `OneTimeTearDown` を活用し、テスト実行時の Unity アセットインポートループを回避する実践的な設計。

---

## 3. 潜在的課題と改善推奨事項

### 3.1 【重要】シーン内オブジェクト参照（Hierarchy / Component 参照）の解決
* **現状**: [`ComponentCapturer`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Capture/ComponentCapturer.cs) では `SerializedPropertyType.ObjectReference` を一律 `AssetDatabase.TryGetGUIDAndLocalFileIdentifier` で処理。
* **課題**: Modular Avatar（例: `ModularAvatarMergeArmature.target`）や VRChat コンポーネント（例: `VRCPhysBone.rootTransform`）など、**シーン上の別 GameObject / Transform や他コンポーネントを参照している場合**、アセットではないため GUID が取得できず空文字になり、復元時に参照が `null` にクリアされる。
* **推奨対応**: 参照先がシーン内オブジェクト（`!EditorUtility.IsPersistent(reference)`）の場合、コンテナまたはアバタールートからの「Transform 相対パス」（+ 必要に応じてコンポーネント型）として記録し、[`ReferenceResolver.ResolvePath`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Reflection/ReferenceResolver.cs) で解決・復元する仕組みを拡張する。

### 3.2 【仕様確認】コンテナ配下の子オブジェクト上のコンポーネント走査
* **現状**: [`ContainerCapture.CaptureContainer`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Operations/ContainerCapture.cs) は、コンテナのルート GameObject に直接付与されたコンポーネントのみをキャプチャしている。
* **課題**: ユーザーが配置した衣装 Prefab の子 GameObject（特定のボーンやメッシュなど）に MA コンポーネント（`ModularAvatarBoneProxy` など）を追加した場合、それらはキャプチャされないため、復元（再インスタンス化）時に消失する。
* **推奨対応**: 「1コンテナ = 1機能単位（コンテナ直下に設定を集約する運用）」としての前提をドキュメント化するか、将来的にコンテナ配下を再帰的に走査して Prefab 外で追加されたコンポーネントを `path` つきで記録・復元できるように拡張する。

### 3.3 マテリアル複製アセットの増殖と GC（ライフサイクル管理）
* **現状**: [`MaterialSettingsApplier.Apply`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/MaterialSettings/MaterialSettingsApplier.cs) では、適用ごとに `AssetDatabase.GenerateUniqueAssetPath` で `*_avatarvcs.mat` を新規作成。
* **課題**: 適用を繰り返すと `*_avatarvcs 1.mat`、`*_avatarvcs 2.mat` のようにマテリアルアセットがプロジェクト内に蓄積し続ける。
* **推奨対応**: [DesignDoc 4節](file:///home/katu25/avatar-vcs/DesignDoc_avatar-vcs.md#L396) にある通り、Phase 3 のコミット永続化時に生成アセットの GUID を記録・追跡し、不要になった古い生成物をクリーンアップする GC ロジックの導入が必要。

### 3.4 [`TypeResolver.Resolve`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/Reflection/TypeResolver.cs) のキャッシュ化
* **現状**: 型の解決時に毎回 `AppDomain.CurrentDomain.GetAssemblies()` を全走査している。
* **推奨対応**: 多数のコンポーネントを処理する際のオーバーヘッドを抑えるため、静的な `Dictionary<string, Type>` キャッシュを持たせると高速化できる。

```csharp
private static readonly Dictionary<string, Type> TypeCache = new();

public static Type Resolve(string fullTypeName)
{
    if (string.IsNullOrEmpty(fullTypeName)) return null;
    if (TypeCache.TryGetValue(fullTypeName, out var cached)) return cached;

    var direct = Type.GetType(fullTypeName);
    if (direct != null) return TypeCache[fullTypeName] = direct;

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        var type = assembly.GetType(fullTypeName);
        if (type != null) return TypeCache[fullTypeName] = type;
    }

    return TypeCache[fullTypeName] = null;
}
```

### 3.5 [`MaterialSettingsApplier`](file:///home/katu25/avatar-vcs/Packages/dev.avatarvcs.avatar-vcs/Editor/MaterialSettings/MaterialSettingsApplier.cs#L66) のパス操作のフォールバック
* **現状**: `var directory = System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');`
* **軽微な点**: ルート直下に配置されたアセット等で `directory` が空文字になった場合のガードとして、`string.IsNullOrEmpty(directory) ? "Assets" : directory` のフォールバックを入れておくとより安全。

---

## 4. 進捗状況と次期フェーズ (Phase 3) への展望

| フェーズ | 状態 | 実装内容 |
|---|---|---|
| **Phase 1: 中核仮説の検証** | ✅ **完了** | ルート/コンテナ生成、Prefab GUID 取得、破棄→再生成による冪等性、Transform 復元 |
| **Phase 2: 設定の記録・復元** | ✅ **完了** | SerializedObject 経由のコンポーネント設定、BlendShape/Materialスロット管理、lilToon設定の複製適用 |
| **Phase 3: バージョン管理機能** | ⏳ **未着手** | コミット・ブランチの永続化（`ProjectSettings/AvatarVcs/`）、差分計算（Diff）、EditorWindow UI |
| **Phase 4: 堅牢性向上** | ⏳ **未着手** | アセットハッシュ記録・警告、GUID 再マッピング UI、sensitive フィールドのマスキング |

### 次のステップ
Phase 1 & Phase 2 の基盤実装は高い品質で整っているため、次は **Phase 3（コミット・ブランチのストレージ永続化および差分計算機能）** の実装に進むことが推奨されます。
