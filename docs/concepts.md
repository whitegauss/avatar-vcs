# コアコンセプト

avatar-vcs は「アバターの構成を記録して再現する」ために2つの独立した仕組みを持ちます。それぞれの役割と境界を理解することがこのツールを使いこなす上で最重要です。

---

## 1. プロパティ追跡（Track Properties）

### 何をするか

既存の GameObject・コンポーネントが保持する **値（プロパティ）** を記録し、checkout で **その場で上書き** します。

追跡される値の種類:
- **BlendShape weight** — `SkinnedMeshRenderer` のスライダー値
- **マテリアルスロット** — どの `.mat` ファイルがアサインされているか（GUID で管理）
- **lilToon / Poiyomi / MToon シェーダープロパティ** — Color・Float 値
- **コンポーネントフィールド** — `[Serializable]` なフィールド全般（int, float, bool, string, Vector3, Color, など）
- **GameObject 状態** — `activeSelf`（アクティブ/非アクティブ）、tag、layer
- **シーン内参照** — 同一シーン内の GameObject を参照するフィールド（パス解決）

### 何をしないか

- オブジェクトの追加・削除・Prefab の入れ替えは **一切行いません**
- ボーンの Transform (位置・回転) は追跡対象外です
- アセット実体（Prefab ファイル・Material ファイルの中身）は保持しません

### オプトイン/オプトアウト

`Ensure Root` 実行時にアバタールート配下が **デフォルトで追跡対象** になります。

特定のサブツリーを追跡から外すには:
```
GameObject → AvatarVCS → Untrack Properties Here
```
`AvatarVcsUntracked` マーカーが付き、そのサブツリー全体がコミット・checkout の対象外になります。

再び追跡対象に戻すには:
```
GameObject → AvatarVCS → Track Properties Here
```

---

## 2. コンテナ（Container / 構造管理）

### 何をするか

`[AvatarVCS]` 直下にある **コンテナ** に配置された Prefab インスタンスの **追加・削除・差し替え** を記録し、checkout で **破棄して Prefab から再生成** します。

```
[AvatarVCS]
  └─ hair_long_container      ← コンテナ（AvatarVcsContainer コンポーネント付き）
       └─ HairLong_Prefab     ← Prefab インスタンス（checkout で再生成される）
```

### いつ使うか

「髪ロング版と髪ショート版で Prefab そのものを切り替えたい」「ブランチごとに衣装 A / 衣装 B を差し替えたい」といった、**Prefab レベルの構造変更** を版管理したいときだけ使います。

通常の BlendShape 値や ON/OFF 切り替えだけならプロパティ追跡で十分です。

### 重要な制約

> コンテナの中で行った BlendShape やマテリアルの調整は、checkout 時に Prefab の既定値から再生成された後に再適用されます（0.4.0 以降）。ただしコンテナの **構造**（どの Prefab がどこにあるか）のみを管理するという原則は変わりません。

- コンテナは **ネスト不可**（`[AvatarVCS]` 直下にのみ置ける）
- コンテナ内に直接配置する非 Prefab GameObject は checkout で失われます

---

## ブランチとコミットのモデル

Git に似たブランチ・コミットモデルを採用しています。

```
main ─── commit A ─── commit B ─── commit C (HEAD)
                  \
long-hair ─────── commit D ─── commit E (HEAD)
```

| 概念 | 説明 |
|---|---|
| **コミット** | ある時点でのアバター全構成のスナップショット（JSON ファイル） |
| **ブランチ** | コミット列への名前付きポインタ。HEAD = 最新コミット |
| **checkout** | 指定コミットの内容をシーンに適用する操作。コンテナは再生成、プロパティは上書き |
| **diff** | 2つのコミット間（またはコミットと現在のシーン）の差分表示 |

### アバター GUID

各アバターは `[AvatarVCS]` の `AvatarVcsRoot` コンポーネントに格納された 32 文字の GUID で識別されます。GameObject 名やシーン上の位置に依存しないため、アバターをリネームしてもコミット履歴が保持されます。

> **アバター複製時の注意**: アバターを Ctrl+D で複製すると GUID も複製されます。OnValidate が自動的に検知し、後から追加されたアバター（兄弟インデックスが大きい方）の GUID を再発行します。

---

## 設計上の境界

このツールが **できないこと**:

| できないこと | 理由 |
|---|---|
| 過去バージョンの Prefab ファイル自体を復元 | アセット実体を保持しないため |
| ボーン Transform の記録・復元 | JSON から安全に復元する手段がない |
| Armature 配下の構造変更（オブジェクトの追加削除）の管理 | コンテナは `[AvatarVCS]` 直下のみが対象 |
| プレイモード中の変更を記録 | Unity の EditorWindow はプレイモード中は停止 |

---

## 関連ドキュメント

- [ユーザーガイド (User Guide)](./user-guide.md)
- [データモデル (Data Model)](./data-model.md)
- [← 目次に戻る](./index.md)
