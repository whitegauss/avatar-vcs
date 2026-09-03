# FAQ — よくある質問・トラブルシューティング

## 一般

### Q: VRChat SDK は必要ですか？

いいえ。avatar-vcs は VRChat SDK への依存を持ちません。VRChat アバター以外の Unity プロジェクトにも利用できます。

---

### Q: アバターを複製（Ctrl+D）したら、履歴が共有されてしまいました

複製直後は元のアバターと同じ `avatarGuid` を持ちます。Unity が `OnValidate` を呼ぶタイミング（Inspector が開かれたとき・値変更時など）に自動検知して、兄弟インデックスが大きい方（後から追加された方）の GUID が自動的に再発行されます。

手動で確認したい場合は、複製したアバターの `[AvatarVCS]` コンポーネントを Inspector で確認してください。再発行済みなら元とは異なる GUID が表示されているはずです。

---

### Q: `[AvatarVCS]` をリネームしてしまいました

機能は維持されます。`AvatarVcsRoot` マーカーコンポーネントが付いていれば、名前ではなくコンポーネントで検索するフォールバックにより正しく認識されます。ただし混乱を避けるため元の名前（`[AvatarVCS]`）に戻すことを推奨します。

---

### Q: コミット履歴はどこに保存されますか？

`ProjectSettings/AvatarVcs/avatars/{avatarGuid}/` 配下に保存されます。詳細は [storage-layout.md](./storage-layout.md) を参照してください。

---

## コミット・Checkout

### Q: コミットが遅い / コンソールに大量のログが出る

コミット対象のコンポーネントが多い場合、キャプチャに時間がかかることがあります。追跡が不要なサブツリーには `Untrack Properties Here` を使って除外してください。

---

### Q: Checkout 後に「Asset version has changed」という警告が出ました

コミット時と現在でアセットファイルの内容が変わっています（同一 GUID のまま上書き更新された場合）。警告は情報提供のみで、checkout 自体は完了しています。最新アセットのコンテンツでよければそのまま使用できます。

---

### Q: Checkout したら Prefab が missing になりました

Prefab を別のファイルに差し替えた際に GUID が変わった場合に発生します。

**解決手順:**
1. EditorWindow 上部の **Remap** セクションで missing GUID を確認
2. 各 missing GUID の横のフィールドに新しい Prefab をアサイン
3. **Retry** ボタンを押す

詳細は [user-guide.md](./user-guide.md#guid-再マッピング) を参照。

---

### Q: Checkout で「A newer version of AvatarVCS wrote this commit」とエラーが出ます

コミットの `schemaVersion` が現在のビルドが認識できる最大バージョンを超えています。より新しいバージョンの avatar-vcs でコミットされた履歴を古いバージョンで読み込もうとしている場合に発生します。パッケージを最新版に更新してください。

---

### Q: Checkout を Ctrl+Z で取り消せますか？

ブランチ切り替えの checkout は Ctrl+Z で取り消せます。

一般的な checkout（History パネルからの checkout）は、checkout 前に自動的に安全コミットが作成されます。その安全コミットに checkout し直すことで元の状態に戻せます。

---

## コンテナ

### Q: `Create Container` を2回使うとエラーになりました

0.3.0 以前のバグです。現在のバージョンでは `new_container`, `new_container_1`, ... と自動で連番になります。

---

### Q: コンテナ内で調整した BlendShape 値が checkout 後にリセットされます

0.4.0 以前はこの挙動でした。0.4.0 以降では、コンテナ内の Prefab インスタンスに加えた BlendShape / マテリアル / active 等の調整値もコミット時に記録され、checkout 時に Prefab 再生成後に再適用されます。

パッケージを **0.4.0-poc 以上** に更新してください。

---

### Q: コンテナの中にコンテナを作れますか？

作れません（ネスト禁止）。コンテナは `[AvatarVCS]` の直接の子としてのみ配置できます。ネストされたコンテナは checkout 時に正しく再生成されません。

---

### Q: Prefab インスタンスでない GameObject をコンテナに入れたら?

コミット時に `CaptureContainer` が警告を出します。コンテナの checkout は Prefab から再生成するため、Prefab インスタンスでない GameObject はコンテナ管理の対象外です。通常の子として `[AvatarVCS]` の外に置くか、Track Properties の対象にしてください。

---

## プロパティ追跡

### Q: 同名の兄弟 GameObject があると警告が出ます

`Transform.Find` はパスで検索するため、同じ名前の兄弟 GameObject があると追跡対象を特定できません。兄弟間でユニークな名前を付けてください。

---

### Q: `Untrack Properties Here` を実行したのに追跡が止まりません

アバタールート自身（`[AvatarVCS]` の親）に `Untrack Properties Here` を実行しても、アバター全体のプロパティキャプチャが無効になるため、コマンドが拒否されます（警告ログが出ます）。特定の子（例: 特定の衣装 GameObject）に対して実行してください。

---

### Q: Armature 配下のアクセサリの Transform が記録されません

`AvatarVcsTrackedReference` が付いている場合、**Armature に直接配置したアクセサリ等の Prefab インスタンスの Transform（位置）は記録されます**。

ただし Armature 直下への配置ではなく、Armature の骨（ボーン）の Transform は記録対象外です（ボーンは Prefab 実体を持たず、JSON から安全に復元する手段がないため）。

---

## 開発・テスト

### Q: テストを実行するには？

`TestProject` を Unity 2022.3 で開き、**Test Runner** (`Window → General → Test Runner`) の EditMode から実行します。

### Q: 新しいテストアセンブリに純粋ロジックのテストを追加したい

`AvatarVcs.Tests.Core` アセンブリ（`Tests/Core/`）は Unity シーンや AssetDatabase が不要な純粋なテスト用アセンブリです。`AvatarVcs.Core` への参照のみで書けるテストはここに追加してください。

Editor API を必要とするテストは `AvatarVcs.Tests.Editor`（`Tests/Editor/`）に追加します。

---

## 関連ドキュメント

- [ユーザーガイド (User Guide)](./user-guide.md)
- [アーキテクチャ (Architecture)](./architecture.md)
- [← 目次に戻る](./index.md)
