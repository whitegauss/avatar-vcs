# 失敗ログ

出荷後にユーザーが踏んだ不具合と、**なぜテストで止められなかったか**を記録する。
再発防止のためのものなので、「何を直したか」より「なぜ気づけなかったか」を主に書く。

CHANGELOG は利用者向けに「何が直ったか」を書く場所なので、そちらとは役割が違う。

---

## 適用ルール（ここまでで得られたもの）

1. **サードパーティの識別子を許可リストにするときは、実物を列挙してから書く。**
   「正式名称はこれだろう」で1つ書くと、実際に使われている名前を取りこぼす。
   実際にインストールしたパッケージから `grep` で全部拾ってから決める。
2. **テストのフィクスチャに理想化した値を使わない。**
   実物が `Hidden/lilToonOutline` なら、テストも `Hidden/lilToonOutline` を使う。
   きれいな値でテストを書くと、テストだけが通る。
3. **「対象外なら無警告でスキップ」は、必ず「気づけない」とセットで検討する。**
   ベストエフォートな機能でも、丸ごと何も記録されていない状態は異常なので、
   それが分かる手段（診断ログ・UI 表示・テスト）を必ず1つ用意する。
4. **CI が緑でも「そのコードが実行されたか」を確認する。**
   `result="Passed"` と「そのパスを通った」は別。分岐の入口を assert する。

---

## 2026-09-04 — lilToon のシェーダー設定が丸ごと記録されていなかった

**影響**: 0.4.0-poc / 0.4.1-poc / 0.4.2-poc の3リリース。実アバターでは
`materialSettings` が**常に空**で、checkout しても色が戻らなかった。
看板機能が実質まったく動いていなかった。

**発見経路**: 実機テストでのユーザー報告。CI・CodeRabbit ともに検知ゼロ。

### 原因

`ShaderPropertyMap.SupportedShaders` がシェーダー名の**完全一致**で、lilToon 系は
文字列 `"lilToon"` の1件だけだった。

lilToon は描画モードごとに別シェーダーを登録していて、実測で **64 個**の名前がある。
実アバターのマテリアルはほぼ必ずバリアント側に付く。

```
lilToon                              ← 許可リストが拾えた唯一の名前
Hidden/lilToonOutline                ← 実際に使われていた
Hidden/lilToonTransparent            ← 実際に使われていた
Hidden/lilToonCutout
_lil/[Optional] lilToonOverlay
... 他 59 個
```

つまり **63/64 が弾かれていた**。Poiyomi も同じ形
（`.poiyomi/Poiyomi Toon`、ロック時は `Hidden/Locked/Poiyomi Toon/<hash>`）なので、
同じ理由で動いていなかったはず。

### なぜ気づけなかったか（本題）

**1. 対象外シェーダーを無警告でスキップする設計だった。**
「materialSettings はマテリアル参照追跡の上に乗るベストエフォート」という判断から、
意図的に警告を出していなかった。結果、**Console には何も出ず**、
ユーザーからは「機能が無言で効かない」ようにしか見えなかった。

ベストエフォートであることと、丸ごとゼロ件なのを黙っていることは別の話だった。

**2. テストが全部 Standard シェーダーだった。**
`ShaderPropertyMap.IsSupported` が false を返すため、
`capture → commit → checkout → 再適用` の本番シーケンスが**一度も実行されていなかった**。
`ContainerInnerPropertyTests` に至っては「Standard では materialSettings が空になる」ことを
**期待値として assert** しており、壊れている状態を正常として固定していた。

**3. 再現テストのフィクスチャも理想化していた。**
バグ再現のために TestProject へテスト用シェーダーを追加したとき、素直に `lilToon` と
名付けた。当然そのテストは緑になり、「再現しない」と一度報告してしまった。
実物のシェーダー名を確認していれば、この往復は不要だった。

**4. 単体テストが許可リストを写経していた。**
`ShaderPropertyMapTests` は許可リストと同じ4文字列を `TestCase` に並べていただけで、
**実装の写しでしかなく、実物と突き合わせていなかった**。この形のテストは
「リストの中身が正しいか」を一切検証しない。

### 特定に効いたこと

コードを睨むより、**ユーザーのプロジェクトの実データを読む**のが早かった。

- `ProjectSettings/AvatarVcs/avatars/<guid>/index.json` → コミット一覧（`entries[]`）
- 同 `commits/<commitId>.json` → `materialSettings` が全件 0 と判明 = 記録側の問題と確定
- `.mat` の `m_Shader` の guid → `.meta` 逆引き → `.shader` の `Shader "..."` 行

WSL からは Windows 側を `/mnt/d/...` で直接読める。

### 対処

- 許可リストを**ファミリー判定**に変更（`/` で分割し、`[Optional] ` を剥がして、
  いずれかのセグメントが `lilToon` / `Poiyomi` / `MToon` で始まれば対象）
- 内部パスシェーダー（`Hidden/ltspass_*`、`Hidden/ltsother_*`）は除外を維持し、テストで固定
- `ShaderPropertyMapTests` を実物の名前 37 ケースに差し替え
- TestProject に `Hidden/lilToonOutline` のスタンドインを追加し、e2e で本番経路を実行

### 未対処（要検討）

- 対象外シェーダーの**サマリ診断**。commit 時に「N スロットが対象外シェーダー（内訳）」を
  1行出せば、今回は初回コミットの時点で気づけた。ノイズとのバランスは要検討

---

## 2026-09-03 — `git mv` 後に CI が「存在しないソースファイル」で落ちた

**症状**: `error CS2001: Source file '.../SnapshotDifferTests.cs' could not be found`

**原因**: `.github/workflows/tests.yml` の `Library` キャッシュ。`restore-keys` で
前回の Library が復元されるが、その中の Bee ビルドグラフが `git mv` **前**のパスを
参照したままだった。

**ポイント**: ファイルの**追加**では起きず、**移動・リネーム**でのみ起きる。
それまで追加は何度もしていたので、キャッシュを疑うのが遅れた。

**対処**: キャッシュキーの epoch を上げて（`Library-TestProject-v2-`）一度クリーンビルドさせた。

**次回**: ファイルを移動・リネームした PR で CI が「ファイルが無い」と言い出したら、
まずキャッシュ epoch を疑う。
