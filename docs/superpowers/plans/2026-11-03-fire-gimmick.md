# 燃えるギミック Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expression メニューのトグル `Fire` で、アニメ風の炎パーティクルが体7か所にまとわりつくギミックを natsuki_ver02 に追加する。

**Architecture:** Unity エディタースクリプト（1メニュー項目で一括生成）が、炎スプライト PNG、Particle System 群、FX レイヤー、Expression パラメータ、メニュー項目を作る。スクリプトは冪等で、既存アセットは書き換え前にバックアップする。

**Tech Stack:** Unity 2022.3（VRChat SDK 3.10.3）、C# エディタースクリプト、Unity Test Framework（EditMode）、VRC SDK3A（`VRCExpressionParameters`、`VRCExpressionsMenu`）

**Spec:** `docs/superpowers/specs/2026-11-03-fire-gimmick-design.md`

## Global Constraints

- Modular Avatar / VRCFury は使わない。標準 SDK のみ
- アバター本体のマテリアルとメッシュは変更しない
- PC のみ。Quest 対応は不要
- 外部素材は使わない。スプライトも PNG もスクリプトで生成する
- 炎の配置は7か所: Hips、Spine、Chest、LeftHand、RightHand、LeftFoot、RightFoot（`HumanBodyBones`）
- パーティクルは1か所あたり最大50、合計350以下。当たり判定・ライト・サブエミッターは使わない
- 見た目はアニメ風（セル調）: 涙型スプライト、2〜3段のベタ塗り、通常のアルファ合成（Additive は使わない）、フェードではなくサイズ縮小で消す、低フレームレートのコマ切り替え
- パラメータ `Fire`: Bool、同期あり。メニュー名「燃える」。FX レイヤー名 `Fire`。炎オブジェクト名 `Fire_<ボーン名>`
- 各スクリプトは1ファイル600行以内。役割ごとに分割する
- スクリプトは冪等（再実行で重複させない）。パラメータの空き（256bit 上限）が足りなければ何も書かず中断する

## Review Focus

- 2回続けて実行する → オブジェクト・レイヤー・パラメータ・メニュー項目が重複しない（Task 2, 3, 4）
- Humanoid ボーンが一部欠けている → 警告を出してその部位だけ飛ばし、残りは作る（Task 2）
- Expression パラメータが満杯に近い → 何も書き込まず例外で中断する（Task 4）
- 親メニューの項目数が上限（8）に達している → 何も書き込まず例外で中断する（Task 4）
- 既存の FX レイヤーが Write Defaults オフ → `Fire` レイヤーのステートも既存に合わせる。合わせないと他レイヤーの値が壊れる（Task 3）

## ファイル構成

すべて `Assets/natsuki_veer02/Editor/Fire/` 配下。

| ファイル | 役割 |
|---|---|
| `Fire.Editor.asmdef` | エディター専用アセンブリ。VRC SDK3A / SDKBase を参照 |
| `FireConfig.cs` | 色・量・段数・コマ数・名前などの定数 |
| `FireSpriteGenerator.cs` | 炎スプライトシート PNG の生成 |
| `FireObjectBuilder.cs` | 7か所の Particle System の生成 |
| `FireAnimationBuilder.cs` | クリップ生成と FX レイヤーの追加 |
| `FireMenuRegistrar.cs` | パラメータとメニュー項目の登録 |
| `FireSetup.cs` | 全体の実行、バックアップ、メニュー項目（`Tools/Fire Gimmick/Setup`） |
| `Tests/Fire.Editor.Tests.asmdef` | テスト用アセンブリ |
| `Tests/*Tests.cs` | 各ファイルのテスト |

生成物は `Assets/natsuki_veer02/Fire/`（PNG、マテリアル、クリップ）に出力する。

## テスト実行方法（全タスク共通）

Unity を閉じてから、`ProjectSettings/ProjectVersion.txt` のバージョンに合わせて実行する。

```
"C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" -batchmode -projectPath <プロジェクトのパス> -runTests -testPlatform EditMode -testFilter "<テストクラス名>" -testResults "<出力先>\result.xml" -logFile -
```

Unity を開いている場合は、Window > General > Test Runner の EditMode タブで同じテストを実行してもよい。

---

### Task 1: アセンブリ定義、定数、スプライト生成

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Fire/Fire.Editor.asmdef`
- Create: `Assets/natsuki_veer02/Editor/Fire/FireConfig.cs`
- Create: `Assets/natsuki_veer02/Editor/Fire/FireSpriteGenerator.cs`
- Create: `Assets/natsuki_veer02/Editor/Fire/Tests/Fire.Editor.Tests.asmdef`
- Test: `Assets/natsuki_veer02/Editor/Fire/Tests/FireSpriteGeneratorTests.cs`

**Interfaces:**
- Produces: `FireConfig`（static class）の定数
  - `string ParamName = "Fire"`, `string MenuLabel = "燃える"`, `string LayerName = "Fire"`, `string SitePrefix = "Fire_"`
  - `string OutputDir = "Assets/natsuki_veer02/Fire"`
  - `HumanBodyBones[] Bones`（上記7つ）
  - `int MaxParticlesPerSite = 50`, `int SpriteFrames = 4`, `int SpriteFps = 8`, `int SpriteSize = 128`（1コマの一辺）
  - `Color[] Tones`（3段: 外側 赤橙、中間 橙、内側 黄）
  - 部位ごとの量: `static int RateFor(HumanBodyBones bone)`（Hips/Spine/Chest は 12、手足は 6）
- Produces: `FireSpriteGenerator.Generate(string outputDir) -> Texture2D`
  - `outputDir` に `FireSheet.png` を書き出してインポートし、そのテクスチャを返す
  - 横 `SpriteFrames` コマ × 縦1の並び。全体サイズは `SpriteSize * SpriteFrames` × `SpriteSize`

- [ ] **Step 1: asmdef 2つを作る**
  - `Fire.Editor`: `includePlatforms: ["Editor"]`、`references` に `VRC.SDK3A`、`VRC.SDKBase`
  - `Fire.Editor.Tests`: `includePlatforms: ["Editor"]`、`references` に `Fire.Editor`、`UnityEngine.TestRunner`、`UnityEditor.TestRunner`、`VRC.SDK3A`、`VRC.SDKBase`、`optionalUnityReferences: ["TestAssemblies"]`
  - `VRC.SDK3A` が実在する名前かは `Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/` 配下の asmdef で確認する

- [ ] **Step 2: 失敗するテストを書く**（`FireSpriteGeneratorTests`）
  - `Generate_WritesPngWithExpectedSize`: 返ったテクスチャの幅が `SpriteSize * SpriteFrames`、高さが `SpriteSize`
  - `Generate_UsesOnlyHardEdgedAlpha`: 全ピクセルの alpha が 0 か 255 のどちらか（にじみなし）
  - `Generate_UsesExactlyThreeOpaqueColors`: alpha 255 のピクセルの色が `FireConfig.Tones` の3色のみ
  - `Generate_FramesDiffer`: 1コマ目と2コマ目のピクセル列が一致しない
  - テストごとに一時フォルダ（`Assets/natsuki_veer02/Fire/_test`）へ出力し、`TearDown` で削除する

- [ ] **Step 3: テストを実行して失敗を確認する**
  - Expected: コンパイルエラー（`FireSpriteGenerator` 未定義）で FAIL

- [ ] **Step 4: `FireConfig` と `FireSpriteGenerator.Generate` を実装する**
  - 涙型の輪郭は、下が丸く上が尖る形を式で描く（各コマで先端のゆらぎと横幅を少し変える）
  - 色は輪郭の内側を縮小した3段（外側 → 中間 → 内側）で塗り分ける。フィルタ・アンチエイリアスは使わない
  - インポート設定: `filterMode = Point`、`mipmapEnabled = false`、`alphaIsTransparency = true`、`textureCompression = Uncompressed`

- [ ] **Step 5: テストを実行して通ることを確認する**
  - Expected: 4件 PASS

- [ ] **Step 6: Commit**
  ```bash
  git add Assets/natsuki_veer02/Editor/Fire
  git commit -m "feat: add fire sprite generator and config"
  ```

---

### Task 2: 炎オブジェクトの生成

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Fire/FireObjectBuilder.cs`
- Test: `Assets/natsuki_veer02/Editor/Fire/Tests/FireObjectBuilderTests.cs`

**Interfaces:**
- Consumes: `FireConfig`（Task 1）、`FireSpriteGenerator.Generate`（Task 1）
- Produces: `FireObjectBuilder.Build(GameObject avatarRoot, Texture2D sheet, string outputDir) -> int`（生成した数を返す）
  - `avatarRoot` の `Animator`（Humanoid）から `GetBoneTransform` で各ボーンを取り、各ボーンの子に `Fire_<HumanBodyBones名>` という Particle System 付きオブジェクトを作る（ボーンに追従させるため、共通の `Fire` ルートは作らない）
- Produces: `FireObjectBuilder.FindAll(GameObject avatarRoot) -> List<GameObject>`（名前が `Fire_` で始まるものを返す）
- Produces: `FireObjectBuilder.PathOf(GameObject avatarRoot, GameObject fire) -> string`（`avatarRoot` からの相対パス。Task 3 が使う）

- [ ] **Step 1: 失敗するテストを書く**（`FireObjectBuilderTests`）
  - テスト用に、`Animator`（Humanoid）付きの最小アバターを組むヘルパー `TestAvatar.Create()` を `Tests/` に置く。`HumanDescription` を持つ Avatar を `AvatarBuilder.BuildHumanAvatar` で作る。7つ全ボーンを含める
  - `Build_CreatesSevenInactiveSites`: `FindAll` が7件、すべて `activeSelf == false`
  - `Build_ParticleCountsWithinLimits`: 各 `ParticleSystem.main.maxParticles <= 50`、合計 `<= 350`
  - `Build_NoCollisionLightsOrSubEmitters`: `collision.enabled`、`subEmitters.enabled` が false、`Light` コンポーネントなし
  - `Build_UsesCelStyleSettings`: `TextureSheetAnimation` が有効で `numTilesX == 4`、`timeMode == FPS`、`fps == FireConfig.SpriteFps`。マテリアルのシェーダー名が `Additive` を含まない。`sizeOverLifetime` が有効
  - `Build_IsIdempotent`: 2回呼んでも `FindAll` が7件のまま
  - `Build_SkipsMissingBoneWithWarning`: 左手ボーンを持たないアバターで、`LogAssert.Expect(LogType.Warning, ...)` の警告が出て、`FindAll` が6件
  - `Build_BodySitesEmitMoreThanLimbs`: `Hips` の `emission.rateOverTime` が `LeftHand` より大きい

- [ ] **Step 2: テストを実行して失敗を確認する**
  - Expected: コンパイルエラー（`FireObjectBuilder` 未定義）で FAIL

- [ ] **Step 3: `FireObjectBuilder` を実装する**
  - マテリアル `FireMat.mat` を `outputDir` に作る。シェーダーは `Legacy Shaders/Particles/Alpha Blended`、テクスチャは `sheet`
  - 再実行時は、既存の `Fire_*` を先に `DestroyImmediate` してから作り直す（マテリアルは上書き）
  - Particle System の主な設定は次のとおり。`startLifetime` 0.8〜1.2秒、`startSpeed` 0.3前後、`simulationSpace = Local`、`shape` は小さな球で上方向、`startSize` 0.15〜0.25、`sizeOverLifetime` は 1 → 0 の直線、`colorOverLifetime` は使わない（色はスプライト側）
  - `maxParticles = FireConfig.MaxParticlesPerSite`、`emission.rateOverTime = FireConfig.RateFor(bone)`
  - レンダラーは `ParticleSystemRenderMode.Billboard`、影の受け渡しは両方オフ
  - 生成直後は `SetActive(false)`

- [ ] **Step 4: テストを実行して通ることを確認する**
  - Expected: 7件 PASS

- [ ] **Step 5: Commit**
  ```bash
  git add Assets/natsuki_veer02/Editor/Fire
  git commit -m "feat: add anime-style fire object builder"
  ```

---

### Task 3: FX レイヤーとクリップ

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Fire/FireAnimationBuilder.cs`
- Test: `Assets/natsuki_veer02/Editor/Fire/Tests/FireAnimationBuilderTests.cs`

**Interfaces:**
- Consumes: `FireObjectBuilder.FindAll`、`FireObjectBuilder.PathOf`（Task 2）、`FireConfig`
- Produces: `FireAnimationBuilder.Apply(AnimatorController fx, GameObject avatarRoot, string outputDir) -> void`
  - `outputDir` に `Fire_On.anim`、`Fire_Off.anim` を作る。On は各 `Fire_*` の `m_IsActive` を 1、Off は 0 にする（長さ1フレーム、ループなし）
  - `fx` に Bool パラメータ `Fire` を追加し（既存なら再利用）、`Fire` レイヤーを追加する。ステートは `Off`（デフォルト）と `On`。`Off → On` は `Fire == true`、`On → Off` は `Fire == false`。遷移の `hasExitTime = false`、`duration = 0`
  - 既存レイヤーのステートの Write Defaults が多数派の値に、新しいステートも合わせる

- [ ] **Step 1: 失敗するテストを書く**（`FireAnimationBuilderTests`）
  - 各テストは、`TestAvatar.Create()` と `FireObjectBuilder.Build` の結果、一時の `AnimatorController` を使う
  - `Apply_AddsBoolParameter`: パラメータ `Fire` が Bool 型で1件
  - `Apply_AddsFireLayerWithTwoTransitions`: レイヤー `Fire` が1件、ステート `Off`/`On`、遷移条件が `Fire` の true/false
  - `Apply_OnClipTogglesAllSevenPaths`: `Fire_On.anim` の `m_IsActive` カーブが7本で、値が1。`Fire_Off.anim` は値が0
  - `Apply_IsIdempotent`: 2回呼んでもレイヤー1件、パラメータ1件
  - `Apply_MatchesExistingWriteDefaults`: Write Defaults オフのレイヤーだけを持つコントローラーに適用すると、`On`/`Off` ステートも `writeDefaultValues == false`

- [ ] **Step 2: テストを実行して失敗を確認する**
  - Expected: コンパイルエラーで FAIL

- [ ] **Step 3: `FireAnimationBuilder.Apply` を実装する**
  - 再実行時は、`Fire` レイヤーとクリップを削除してから作り直す
  - レイヤーの `defaultWeight = 1`
  - カーブは `EditorCurveBinding.DiscreteCurve` で、型は `typeof(GameObject)`、プロパティは `m_IsActive`

- [ ] **Step 4: テストを実行して通ることを確認する**
  - Expected: 5件 PASS

- [ ] **Step 5: Commit**
  ```bash
  git add Assets/natsuki_veer02/Editor/Fire
  git commit -m "feat: add fire FX layer and clips"
  ```

---

### Task 4: パラメータとメニューの登録

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Fire/FireMenuRegistrar.cs`
- Test: `Assets/natsuki_veer02/Editor/Fire/Tests/FireMenuRegistrarTests.cs`

**Interfaces:**
- Consumes: `FireConfig`
- Produces: `FireMenuRegistrar.Register(VRCExpressionParameters parameters, VRCExpressionsMenu menu) -> void`
  - 登録前に検証し、失敗時は何も書かずに `System.InvalidOperationException` を投げる
    - `parameters.CalcTotalCost() + 1 > VRCExpressionParameters.MAX_PARAMETER_COST` なら、パラメータ満杯として例外
    - `Fire` 項目が未登録で `menu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS` なら、メニュー満杯として例外
  - パラメータ `Fire`: `valueType = Bool`、`saved = false`、`defaultValue = 0`、`networkSynced = true`
  - メニュー項目: 名前「燃える」、`type = Toggle`、`parameter.name = "Fire"`
  - 既に登録済みなら重複させない

- [ ] **Step 1: 失敗するテストを書く**（`FireMenuRegistrarTests`）
  - `ScriptableObject.CreateInstance` で空のパラメータとメニューを作って使う
  - `Register_AddsSyncedBoolParameter`: `Fire` が Bool、`networkSynced == true`
  - `Register_AddsToggleControl`: 名前が「燃える」で、`type == Toggle`、`parameter.name == "Fire"`
  - `Register_IsIdempotent`: 2回呼んでも、パラメータ1件、項目1件
  - `Register_ThrowsAndWritesNothing_WhenParametersFull`: ダミーの Int パラメータ（コスト8）を `MAX_PARAMETER_COST` ちょうどまで詰めてから呼ぶと、例外が出て、パラメータ数もメニュー項目数も変わらない
  - `Register_ThrowsAndWritesNothing_WhenMenuFull`: ダミー項目を `MAX_CONTROLS` 個入れてから呼ぶと、例外が出て、パラメータ数もメニュー項目数も変わらない

- [ ] **Step 2: テストを実行して失敗を確認する**
  - Expected: コンパイルエラーで FAIL

- [ ] **Step 3: `FireMenuRegistrar.Register` を実装する**
  - `parameters.parameters` は配列なので、追加は新しい配列を作って代入する
  - 変更後に `EditorUtility.SetDirty` を両方に呼ぶ

- [ ] **Step 4: テストを実行して通ることを確認する**
  - Expected: 5件 PASS

- [ ] **Step 5: Commit**
  ```bash
  git add Assets/natsuki_veer02/Editor/Fire
  git commit -m "feat: add fire parameter and menu registrar"
  ```

---

### Task 5: 全体の実行、バックアップ、メニュー項目

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Fire/FireSetup.cs`
- Test: `Assets/natsuki_veer02/Editor/Fire/Tests/FireSetupTests.cs`

**Interfaces:**
- Consumes: 上の4つすべて
- Produces: `FireSetup.Run(GameObject avatarRoot, AnimatorController fx, VRCExpressionParameters parameters, VRCExpressionsMenu menu, string outputDir, string backupDir) -> void`
  - 順序: ① パラメータ・メニューの検証と登録（失敗しうるものを先に）② バックアップ（`fx`、`parameters`、`menu` のアセットを `backupDir` にコピー）③ スプライト生成 ④ オブジェクト生成 ⑤ レイヤー生成
  - ただし②は①の前に行う（書き換え前のバックアップ）。①が例外を出した場合は、③〜⑤を実行しない
- Produces: メニュー項目 `Tools/Fire Gimmick/Setup`
  - `Assets/natsuki_veer02/Prefab/natsuki_veer02.prefab` を `PrefabUtility.LoadPrefabContents` で開き、`VRCAvatarDescriptor` の FX レイヤーのコントローラー、`expressionParameters`、`expressionsMenu` を取り出して `Run` を呼ぶ。成功したら `SaveAsPrefabAsset` で保存し、`UnloadPrefabContents` する。例外時も `UnloadPrefabContents` する
  - バックアップ先は `Assets/natsuki_veer02/FireBackup/<yyyyMMdd_HHmmss>/`

- [ ] **Step 1: 失敗するテストを書く**（`FireSetupTests`）
  - `Run_CreatesAllPieces`: 実行後、次の4点がすべて成り立つ
    - `Fire` パラメータが登録されている
    - メニューに「燃える」がある
    - FX レイヤー `Fire` がある
    - `FindAll` が7件
  - `Run_CreatesBackups`: `backupDir` に3アセットのコピーがある
  - `Run_AbortsBeforeTouchingAnything_WhenParametersFull`: パラメータ満杯で例外が出て、`FindAll` が0件で、FX レイヤー `Fire` がない
  - `Run_Twice_ProducesSameResult`: 2回実行しても、パラメータ・項目・レイヤーが1件、`FindAll` が7件

- [ ] **Step 2: テストを実行して失敗を確認する**
  - Expected: コンパイルエラーで FAIL

- [ ] **Step 3: `FireSetup.Run` とメニュー項目を実装する**
  - 全体を `try/finally` にする必要があるのはメニュー項目側のみ。`Run` 自体は例外をそのまま投げる
  - 完了時に `Debug.Log` で、作った数と出力先を表示する

- [ ] **Step 4: テストを実行して通ることを確認する**
  - Expected: 4件 PASS。あわせて、全テスト（`Fire.Editor.Tests` 全体）が PASS

- [ ] **Step 5: 実機で確認する（人が目視）**
  - Unity で `Tools/Fire Gimmick/Setup` を実行する。Console にエラーが出ないこと
  - `natsuki_veer02.prefab` を開き、`Fire_Hips` など7つのオブジェクトがボーンの子にあることを確認する
  - Play モードで Animator のパラメータ `Fire` を ON にして、炎が7か所に出てアニメ風（ベタ塗り、低コマ）に見えることを確認する
  - VRChat の Build & Test で、Expression メニューの「燃える」トグルでオン・オフできることを確認する

- [ ] **Step 6: Commit**
  ```bash
  git add Assets/natsuki_veer02/Editor/Fire Assets/natsuki_veer02/Fire Assets/natsuki_veer02/Prefab Assets/natsuki_veer02/Animations
  git commit -m "feat: add fire gimmick setup entry point"
  ```

---

## Self-Review

- **Spec coverage:** 構成（パラメータ・メニュー・FX レイヤー・炎オブジェクト・スクリプト分割）→ Task 1〜5。見た目（涙型・ベタ塗り・アルファ合成・サイズ縮小・低コマ）→ Task 1, 2。パフォーマンス → Task 2。エラー処理（冪等・ボーン欠け・パラメータ満杯・バックアップ）→ Task 2, 4, 5。テスト項目4点 → Task 5。範囲外の項目は含めていない。
- **仕様書の修正:** 「7か所」と部位リスト（6つ）が合わなかったため、仕様書に Chest を追加して7か所にした。
- **型・名前の一貫性:** `FireObjectBuilder.Build` は `int` を返す。Task 3 と Task 5 は `FindAll`／`PathOf` を使う。名前は `FireConfig` に集約している。
