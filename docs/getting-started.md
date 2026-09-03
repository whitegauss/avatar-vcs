# Getting Started — インストールと初回セットアップ

## 動作環境

- Unity **2022.3** 以降（VRChat 推奨環境。例: 2022.3.22f1）
- VRChat Creator Companion (VCC) または Git URL 経由でのインストール

---

## インストール

### A. VCC (VRChat Creator Companion) 経由（推奨）

1. VCC を開き、**Settings → Packages → Add Repository** を選択
2. 以下の URL を追加:
   ```text
   https://raw.githubusercontent.com/whitegauss/avatar-vcs/main/index.json
   ```
3. プロジェクトの **Manage Packages** から **"Avatar VCS (Phase 1 PoC)"** を追加

### B. Unity Package Manager (Git URL 経由)

Unity の **Window → Package Manager → + → Add package from git URL** に以下を入力:

```text
https://github.com/whitegauss/avatar-vcs.git?path=Packages/dev.avatarvcs.avatar-vcs
```

または `Packages/manifest.json` に直接追記:

```json
{
  "dependencies": {
    "dev.avatarvcs.avatar-vcs": "https://github.com/whitegauss/avatar-vcs.git?path=Packages/dev.avatarvcs.avatar-vcs"
  }
}
```

---

## 初回セットアップ

### Step 1: 管理ルートを作成する（Ensure Root）

1. Hierarchy でアバターのルート GameObject を選択
2. メニューから `GameObject → AvatarVCS → Ensure Root` を実行

これにより:
- アバター配下に `[AvatarVCS]` という管理ルート GameObject が作成される
- アバタールート自身と直下の子（Body / Armature など）に `AvatarVcsTrackedReference` コンポーネントが自動付与される（= プロパティ追跡が開始される）

> **Note**: `[AvatarVCS]` の名前を変更しても機能は維持されますが、混乱を避けるため元の名前のままにすることを推奨します。

### Step 2: EditorWindow を開く

`Window → AvatarVCS` からメインウィンドウを開き、Avatar フィールドにアバタールートをアサインします。

Hierarchy でアバターが選択済みの場合は `GameObject → AvatarVCS → Open Window` からも開けます（アバターが自動セットされた状態で開きます）。

### Step 3: 最初のコミットを作成する

EditorWindow 下部の **Commit** バーにメッセージを入力し、**Commit** ボタンを押します。  
現在のアバター構成（BlendShape 値・マテリアル設定・コンポーネントフィールド値）が記録されます。

---

## 衣装やアクセサリの追加（コンテナを使う場合）

「この衣装の Prefab を丸ごと別の Prefab と差し替えて切り替えたい」という場合にのみコンテナを使います。

1. 対象の Prefab インスタンスを `[AvatarVCS]` 直下にドラッグするだけで OK  
   → 次のコミット時に自動的にコンテナで包まれます（`Create Container` は不要）

複数の Prefab をまとめて1ユニットとして切り替えたい場合:
1. `[AvatarVCS]` を選択した状態で `GameObject → AvatarVCS → Create Container` を実行
2. 作成されたコンテナの下に Prefab インスタンスを配置

---

## 次のステップ

- [コアコンセプト (Concepts)](./concepts.md) — プロパティ追跡とコンテナの違いを理解する
- [ユーザーガイド (User Guide)](./user-guide.md) — EditorWindow の全機能リファレンス
- [← 目次に戻る](./index.md)
