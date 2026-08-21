# avatar-vcs

VRChat アバターの「構成」（衣装・髪型などのプレハブ構成、BlendShape、マテリアル設定）をバージョン管理する Unity エディタ拡張ツールです。

Git のブランチ／コミットに似たモデルで、「髪ロング版」と「髪ショート版」のような複数構成を並行管理し、いつでも切り替え・比較・復元できるようにします。詳細な設計思想は [DesignDoc_avatar-vcs.md](DesignDoc_avatar-vcs.md) を参照してください。

## 現在のステータス

設計書の Phase 1〜4（[7章](DesignDoc_avatar-vcs.md#7-poc-実装計画v2) 参照）を実装済みです。

- **Phase 1**: 管理下コンテナ方式によるプレハブ構成の記録・破棄→再生成による冪等な復元
- **Phase 2**: コンポーネント設定（Transform 以外のフィールド、アセット参照、シーン内参照）の記録・復元、アバター本体の BlendShape / マテリアル参照のホワイトリスト管理、マテリアル設定（lilToon）の複製・再適用
- **Phase 3**: コミット履歴の永続化、ブランチの作成・切り替え、コミット間の構造化 diff、EditorWindow UI
- **Phase 4**: アセット更新時のバージョン警告（内容ハッシュの変更検知）、GUID 再マッピング UI（アセット再インポート後の追従）

追加機能:
- **ブランチ比較モード**（設計書 5.2）: 2つのコミットを自動コミットなしで交互に checkout して見比べるモード
- **コミット間 diff**: 選択コミット vs 現在のシーンだけでなく、任意のコミット同士の差分表示

CI (`.github/workflows/tests.yml`) で [game-ci/unity-test-runner](https://github.com/game-ci/unity-test-runner) を使い、全 EditMode テストを自動実行しています。

## できないこと（設計上の境界）

このツールは**アセット実体（Prefab/Material の中身）を保持しません**。「過去のバージョンの Prefab に戻す」ことは原理的にできず、あくまで「どの構成がどのアセットを参照していたか」を記録・復元するのみです（設計書 [6.1](DesignDoc_avatar-vcs.md#61-構造的な限界先に明示する) 参照）。アセット自体のバックアップは別途ユーザー側で行ってください。

またアバター本体（Body / Armature 等の骨格構造）はスコープ外で、変更しても管理対象になりません。管理対象は `[AvatarVCS]` ルート配下のコンテナと、ホワイトリストされた BlendShape / マテリアル参照のみです。

## インストール

Unity 2022.3 以降(VRChat 推奨環境、例: Unity 2022.3.22f1)のプロジェクトで、Package Manager から git URL を指定して追加します(このリポジトリは private のため、追加する側の Git 認証情報でアクセスできる必要があります)。

```
https://github.com/whitegauss/avatar-vcs.git?path=Packages/dev.avatarvcs.avatar-vcs
```

もしくは `Packages/manifest.json` に直接追記:

```json
{
  "dependencies": {
    "dev.avatarvcs.avatar-vcs": "https://github.com/whitegauss/avatar-vcs.git?path=Packages/dev.avatarvcs.avatar-vcs"
  }
}
```

## 使い方

### 1. コンテナを作る

Hierarchy でアバターのルート GameObject を選択し、`GameObject > AvatarVCS > Ensure Root` で管理ルート `[AvatarVCS]` を作成します。続けて `Create Container` でコンテナ（例: `hair_long`）を作り、その下に衣装や髪型の Prefab インスタンスを配置してください。

### 2. コミットする

`Window > AvatarVCS` から開く EditorWindow の Commit バーで、現在のコンテナ構成をコミットとして記録します。コミット・checkout・ブランチ操作はすべてこの EditorWindow から行います(選択中のオブジェクトをそのまま「アバター」として扱ってしまう誤操作を防ぐため、GameObject メニューには置いていません)。

### 3. ブランチ・履歴を操作する

EditorWindow (`Window > AvatarVCS`、またはアバターの GameObject を選択した状態で `GameObject > AvatarVCS > Open Window` を使うとアバターが自動セットされた状態で開けます) で以下が行えます。

- ブランチの作成・切り替え（コミットしていない変更がある場合は確認ダイアログ。取り消しは Ctrl+Z)
- コミット履歴の閲覧、任意のコミットへの checkout
- 選択コミットと現在のシーン、または任意の2コミット間の diff 表示
- **Compare**: 2つのコミットを選んで比較モードに入り、「Show A / Show B」で自動コミットなしに交互 checkout。終了時に「今表示中の状態を採用してコミット」か「比較前の状態に戻す」を選択
- アセットの再インポートで GUID が変わってしまった場合の再マッピング（missing 状態のプレハブに新しいアセットを割り当てて retry）

## 開発

### プロジェクト構成

```
Packages/dev.avatarvcs.avatar-vcs/   # VPM パッケージ本体（Editor/Runtime/Tests）
TestProject/                          # ローカル/CI 用の検証用 Unity プロジェクト
.github/workflows/tests.yml           # CI (game-ci/unity-test-runner)
```

### テストの実行

`TestProject` を Unity 2022.3 系で開き、Test Runner (EditMode) から `Tests/Editor/` 配下のテストを実行してください。CI でも同じテストが push / PR ごとに走ります。

## ドキュメント

- [USAGE.md](USAGE.md) — 使い方ガイド(元の状態に戻す方法、コミット削除、比較モードなど)
- [DesignDoc_avatar-vcs.md](DesignDoc_avatar-vcs.md) — 詳細設計書（v2、現行）
- [PRD_avatar-vcs.md](PRD_avatar-vcs.md) — プロダクト要求仕様
- [DesignDoc_v1_superseded.md](DesignDoc_v1_superseded.md) — 旧設計（参考、廃止済み）
