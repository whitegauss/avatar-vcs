# avatar-vcs

VRChat アバターの「構成」（衣装・髪型などのプレハブ構成、BlendShape、マテリアル設定）をバージョン管理する Unity エディタ拡張ツールです。

Git のブランチ／コミットに似たモデルで、「髪ロング版」と「髪ショート版」のような複数構成を並行管理し、いつでも切り替え・比較・復元できるようにします。

## 現在のステータス

設計書の Phase 1〜4 を実装済みです。

- **Phase 1**: 管理下コンテナ方式によるプレハブ構成の記録・破棄→再生成による冪等な復元
- **Phase 2**: コンポーネント設定（Transform 以外のフィールド、アセット参照、シーン内参照）の記録・復元、アバター本体の BlendShape / マテリアル参照のホワイトリスト管理、マテリアル設定（lilToon）の複製・再適用
- **Phase 3**: コミット履歴の永続化、ブランチの作成・切り替え、コミット間の構造化 diff、EditorWindow UI
- **Phase 4**: アセット更新時のバージョン警告（内容ハッシュの変更検知）、GUID 再マッピング UI（アセット再インポート後の追従）

追加機能:
- **ブランチ比較モード**（設計書 5.2）: 2つのコミットを自動コミットなしで交互に checkout して見比べるモード
- **コミット間 diff**: 選択コミット vs 現在のシーンだけでなく、任意のコミット同士の差分表示

CI (`.github/workflows/tests.yml`) で [game-ci/unity-test-runner](https://github.com/game-ci/unity-test-runner) を使い、全 EditMode テストを自動実行しています。

## できないこと（設計上の境界）

このツールは**アセット実体（Prefab/Material の中身）を保持しません**。「過去のバージョンの Prefab に戻す」ことは原理的にできず、あくまで「どの構成がどのアセットを参照していたか」を記録・復元するのみです。アセット自体のバックアップは別途ユーザー側で行ってください。

またボーン自体の pose（Transform）はスコープ外で、変更しても管理対象になりません（ボーンは実体を持たず、コミットの JSON から安全に復元する手段がないためです）。それ以外——Body / Armature / アバタールート自身や、その配下の既存コンポーネントが持つ BlendShape・マテリアル参照・各種フィールド値——はデフォルトで追跡対象です。特定のサブツリー（例：この衣装だけ）を追跡から外したい場合は、対象を選択して `GameObject > AvatarVCS > Untrack Properties Here` を実行すると除外マーカー（`AvatarVcsUntracked`）が付き、そのサブツリー全体がコミットに含まれなくなります（`Track Properties Here` で解除）。Armature に直接配置したアクセサリ等の Prefab インスタンスであれば、その位置（Transform）も記録されます。ただし Prefab の追加・削除・入れ替えという「構造」の変更自体を管理できるのは `[AvatarVCS]` ルート配下のコンテナのみです。

## インストール

### VCC (VRChat Creator Companion) 経由

VCC の Settings > Packages > Add Repository で以下の URL を追加してください:

```
https://raw.githubusercontent.com/whitegauss/avatar-vcs/main/index.json
```

追加後、プロジェクトの Manage Packages から "Avatar VCS (Phase 1 PoC)" を追加できます。

### Git URL 経由

Unity 2022.3 以降(VRChat 推奨環境、例: Unity 2022.3.22f1)のプロジェクトで、Package Manager から git URL を指定して追加します。

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

### 1. 準備する（Ensure Root）

Hierarchy でアバターのルート GameObject を選択し、`GameObject > AvatarVCS > Ensure Root` で管理ルート `[AvatarVCS]` を作成します。この時点でアバタールートと直下の子（Body / Armature など）は自動で追跡対象になります（空のコンテナは作られません）。

AvatarVCS には**役割の違う2つの仕組み**があります。

- **プロパティの版管理（Track Properties）**: 既存オブジェクトの BlendShape weight・マテリアル参照・lilToon 設定・tag・active・各種フィールド値を記録し、checkout で**その場で上書き**します。構造（オブジェクト／コンポーネントの増減）は一切触りません。既定でアバタールート配下が対象なので、通常はここに何もしなくて OK。特定サブツリーを外したいときだけ `Untrack Properties Here`。
- **構造の版管理（コンテナ）**: Prefab の**追加・削除・入れ替え**を記録します。checkout で中身を**破棄して Prefab から再生成**します。「髪ロング版」と「髪ショート版」で Prefab そのものを切り替えたい、というときに使います。

### 2. コンテナ（Prefab 差し替えをしたい場合のみ）

1つの Prefab を差し替え単位にしたいだけなら、その Prefab インスタンスを `[AvatarVCS]` 直下に配置するだけで OK です（コミット時に自動でコンテナに包まれます。`Create Container` を手動で呼ぶ必要はありません）。複数の Prefab をまとめて1つの切り替え単位にしたい場合（例: `hair_long` としてまとめて2つの Prefab を切り替えたい）は、`Create Container` でコンテナを作り、その下に配置してください。

> コンテナは checkout で Prefab から再生成されるため、**コンテナの中で行った BlendShape やマテリアルの調整は現状バージョン管理されません**（Prefab の既定値に戻ります）。そうした調整値も版管理したい場合は、その部分をコンテナに入れず通常の子として置き、Track Properties の対象にしてください。

### 3. コミットする

`Window > AvatarVCS` から開く EditorWindow の Commit バーで、現在の構成をコミットとして記録します。コミット・checkout・ブランチ操作はすべてこの EditorWindow から行います(選択中のオブジェクトをそのまま「アバター」として扱ってしまう誤操作を防ぐため、GameObject メニューには置いていません)。

### 4. ブランチ・履歴を操作する

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

### 設計ドキュメントの所在

設計に関する公開ドキュメントは `README.md` と `CHANGELOG.md` のみです。

これらを持たないクローンから作業する場合、設計上の根拠が必要になったらメンテナに設計書を依頼してください。Jira の各タスクは設計書なしでも作業できるよう文面を自己完結させています。
