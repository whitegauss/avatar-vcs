# ユーザーガイド — EditorWindow 操作リファレンス

## ウィンドウを開く

| 方法 | 説明 |
|---|---|
| `Window → AvatarVCS` | 空の状態でウィンドウを開く |
| `GameObject → AvatarVCS → Open Window` | 選択中のアバターをセットした状態で開く |

---

## アバター選択

ウィンドウ上部の **Avatar** フィールドにアバタールートの GameObject をアサインします。

- **Use Selected** ボタン: Hierarchy で選択中の GameObject を即座にアサイン
- コンテナ内のオブジェクトや `[AvatarVCS]` 自身を選択した場合は、自動的に親アバタールートへ解決されます
- AvatarVCS の管理下にない GameObject をアサインしようとすると確認ダイアログが表示されます

---

## GameObject メニューコマンド

`GameObject → AvatarVCS` 配下の各コマンド（Hierarchy でアバターを選択した状態で使用）:

| コマンド | 説明 |
|---|---|
| **Ensure Root** | `[AvatarVCS]` 管理ルートを作成。アバタールート配下を自動で追跡対象に設定 |
| **Create Container** | `[AvatarVCS]` 直下に新しいコンテナを作成。名前は自動採番（`new_container`, `new_container_1`, ...） |
| **Open Window** | EditorWindow を開く（アバター自動セット） |
| **Track Properties Here** | 選択 GameObject をプロパティ追跡の対象に追加（`AvatarVcsTrackedReference` を付与） |
| **Untrack Properties Here** | 選択サブツリーをプロパティ追跡から除外（`AvatarVcsUntracked` を付与） |
| **Export BlendShapes...** | 選択 `SkinnedMeshRenderer` の BlendShape 値を JSON ファイルへエクスポート |
| **Import BlendShapes...** | JSON ファイルから BlendShape 値をインポート・適用 |

---

## EditorWindow の各パネル

### ブランチバー

現在のブランチ名と `HEAD` コミットを表示します。

- **Branch ドロップダウン**: ブランチを切り替える（未コミット変更がある場合は確認ダイアログ）
- **New Branch フィールド + Create**: 新しいブランチを現在の HEAD から作成
- ブランチ切り替えは Ctrl+Z で取り消せます

### コミット履歴パネル

コミット一覧を新しい順に表示します。

- **コミットをクリック**: そのコミットを選択し、右側の diff パネルに差分を表示
- **チェックボックス**: 複数選択して一括削除（ブランチ HEAD は削除不可）
- **Delete Selected**: チェックしたコミットを削除

### diff パネル

選択コミットと **現在のシーン状態** または **別のコミット** との差分を表示します。

| 表示項目 | 意味 |
|---|---|
| `+` (緑) | 追加されたコンテナ / 変更された値（新しい値） |
| `-` (赤) | 削除されたコンテナ / 変更された値（古い値） |
| コンテナ名展開 | 折りたたみ/展開でプロパティ変更を確認 |

- **Diff vs Scene** (デフォルト): 選択コミットと現在のシーンの差分
- **Diff A vs B**: 任意の2コミット間の差分（コミット一覧で2件選択）

> diff は階層変更（scene の編集）が検知されると自動更新されます。

### コミットバー

現在のシーン状態をコミットします。

1. メッセージテキストフィールドに変更内容を記入
2. **Commit** ボタンを押す

### Checkout バー

選択コミットの内容をシーンに適用します。

- **Checkout**: 選択コミットを checkout（現在の変更は安全コミットで自動保存）
- checkout 前に未コミット変更がある場合は自動的に安全コミットが作成されます

---

## Compare モード（ブランチ比較）

2つのコミットの状態をシーン上で交互に確認できるモードです。

### 使い方

1. **Enter Compare** ボタンを押す（または Compare タブを開く）
2. **Commit A** と **Commit B** をそれぞれ選択
3. **Show A** / **Show B** ボタンで交互に切り替えて確認
   - 各切り替えは自動コミットなしに行われます
4. 終了時:
   - **Keep Current & Commit**: 今表示中の状態を採用してコミット
   - **Exit Compare (Restore)**: 比較モード開始前の状態に戻して終了

> compare モード中にウィンドウを閉じたり recompile が起きても、再度開くと compare モードが維持されています。

---

## GUID 再マッピング（アセット再インポート後の追従）

Prefab を再インポートしたり別ファイルに差し替えた場合、旧 GUID での参照が missing になります。

### 解決手順

1. EditorWindow 上部の **Remap** セクションが表示される（missing GUID がある場合）
2. 各 missing GUID の横にあるオブジェクトフィールドに新しい Prefab をアサイン
3. **Retry** ボタンを押す

旧 GUID → 新 GUID のマッピングは `ProjectSettings/AvatarVcs/guid-remapping.json` に永続化されます。

---

## BlendShape プリセット（スタンドアロン機能）

コミット履歴とは独立した、BlendShape 値の共有・転用ツールです。

### エクスポート

1. Hierarchy で `SkinnedMeshRenderer` を持つ GameObject を選択
2. `GameObject → AvatarVCS → Export BlendShapes...`
3. 保存先を指定して JSON ファイルとして書き出す

### インポート

1. 対象の `SkinnedMeshRenderer` を持つ GameObject を選択
2. `GameObject → AvatarVCS → Import BlendShapes...`
3. 事前にエクスポートした JSON ファイルを選択
4. BlendShape 名が一致するものだけ適用（名前が一致しないものはスキップ・ログに出力）

---

## マテリアル設定プリセット（スタンドアロン機能）

コミット履歴とは独立した、マテリアルのシェーダー設定の共有・転用ツールです。
「この色設定を友達に教えたい」ときに、`.mat` そのものを渡す代わりに使います。

### エクスポート

1. Project ウィンドウでマテリアルを選択（右クリック）
2. `Assets → AvatarVCS → Export Material Settings...`
3. 保存先を指定して JSON ファイルとして書き出す（既定名は `<マテリアル名>_settings.json`）

書き出されるのは、**シェーダーのデフォルト値と異なるプロパティだけ**です。
コミットは正確な復元のために全プロパティ（lilToon で約490個）を記録しますが、
プリセットは人が読んで適用を判断するものなので、実際に設定された数個〜数十個だけを残します。
書かれていないプロパティは「触っていない＝相手のマテリアルのままで正しい」という意味になります。

ファイルにはパスもスロットも元マテリアルへの参照も含まれません。これが、別のマテリアル・
別のアバター・別のプロジェクトに読み込める理由です。

### インポート

1. Project ウィンドウで適用先のマテリアルを選択（右クリック）
2. `Assets → AvatarVCS → Import Material Settings...`
3. 事前にエクスポートした JSON ファイルを選択
4. 適用件数とスキップされたプロパティ名が Console に出力される

インポートは**選択したマテリアルそのものを書き換えます**（複製は作りません）。
checkout がソースアセットを触らず複製を作るのは自動実行だからで、インポートは
ユーザーが対象を明示的に選んだ操作なので、そのマテリアルを編集するのが期待どおりの動作です。
`Undo`（Ctrl+Z）で元に戻せます。

適用先のシェーダーが持たないプロパティは、エラーにはならず**スキップして報告**されます。
似ているが同一ではないシェーダーへ持ち込むことが想定用途だからです。

### テクスチャの扱い

テクスチャは **GUID のみ**を記録し、画像データは埋め込みません。したがって:

- 受け取った側が持っていないテクスチャは復元されません（報告されてスキップされます）
- プリセットは値だけのファイルなので、持っていないアセットの代わりにはなりません。
  `.mat` を直接渡すより配布物として安全です
- Tiling / Offset は記録されません（記録されるのは「どのテクスチャを指すか」まで）

---

## 関連ドキュメント

- [コアコンセプト (Concepts)](./concepts.md)
- [よくある質問 (FAQ)](./faq.md)
- [← 目次に戻る](./index.md)
