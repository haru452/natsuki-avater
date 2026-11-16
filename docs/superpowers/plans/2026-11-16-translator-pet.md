# 翻訳ペット（アバター側・第1段階）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** どのアバターにも置ける `TranslatorPet.prefab`（ブロックのペット、KAT の文字表示、頭上の看板、トグル）と、PC から文字を送る試し送信スクリプトを作る。

**Architecture:** エディタースクリプトがプレハブと付属アセットを生成する。メニュー、パラメータ、FX レイヤーは Modular Avatar の部品でビルド時に合成し、アバター本体のアセットは書き換えない。文字表示は KAT の部品をそのまま使う。送信側は Python 標準ライブラリだけの小さなモジュール。

**Tech Stack:** Unity 2022.3.22f1 / VRChat SDK 3.10.3 / Modular Avatar / KAT v1.3.2 / Unity Test Framework（EditMode）/ Python 3.13（unittest）

**Spec:** `docs/superpowers/specs/2026-11-16-translator-pet-design.md`

## Global Constraints

- `reko` と `natsuki` のプレハブ、FX、Expression メニュー、Expression パラメータのアセットを書き換えない
- 生成するアニメーターのステートは、すべて Write Defaults オフ
- コードは1ファイル 600 行以内
- 生成物の出力先は `Assets/natsuki_veer02/Translator/`、生成スクリプトは `Assets/natsuki_veer02/Editor/Translator/`
- パラメータ名は `TranslatorActive`、`TranslatorLocalAI`、KAT の決まりどおりの `KAT_…`。接頭辞は足さない
- Python は標準ライブラリのみ
- lilToon を前提にしない
- 何度実行しても同じ結果になる（重複を作らない）
- コミットは、そのタスクで作ったファイルだけを `git add` する。作業ツリーには銃ギミックの未コミットの変更があるので、`git add -A` は使わない
- Unity のテストはエディターを閉じてから実行する:
  `"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testFilter "<クラス名>" -testResults "<スクラッチ>\result.xml" -logFile -`

## Review Focus

1. KAT の文字セットにない文字（漢字、絵文字）を送る → 止まらず、その文字だけ「?」になる（Task 2）
2. 128 文字を超える文、空の文を送る → 128 文字で切る。空なら全消去だけ送る（Task 2）
3. すでにプレハブが置いてあるアバターに、もう一度「置く」 → 2個目を作らない（Task 6）
4. 同期パラメータの空きが足りないアバターに置く → 置かずに、合計と対処を表示する（Task 6）
5. 頭の骨がない（Humanoid でない）アバターに置く → エラーにせず、看板はアバター直下の頭上の高さに残す（Task 5）

---

### Task 1: 前提の確認と KAT の調査

**Files:**
- Modify: `docs/superpowers/specs/2026-11-16-translator-pet-design.md`（末尾に「KAT 調査結果」を追記）

**Interfaces:**
- Produces: 設計書の「KAT 調査結果」。Task 2〜5 がここの値を使う

- [ ] **Step 1: 前提を確かめる。** `Packages/vpm-manifest.json` に `nadena.dev.modular-avatar` があり、`Assets/KillFrenzy/AvatarText/` があること。どちらかがなければ、利用者に頼んで止まる
- [ ] **Step 2: KAT を調べ、次を設計書に書く**
  - 付属アニメーター、マテリアル、プレハブ、文字テクスチャ（英語・日本語）のパス
  - パラメータの正確な名前と型（`KAT_CharSync` の番号は 0 始まりか）、同期設定ごとの本数とポインタの範囲
  - 文字の番号から Float への変換式（シェーダーとアニメーターから読む。見込み: 番号 0〜127 は `番号/127`、128〜255 は `(番号-256)/127`）
  - 文字と番号の対応（見込み: ASCII は `ord - 32`）。日本語テクスチャに入っている文字の一覧と番号
  - アニメーションが文字オブジェクトを指すパス
  - 同期設定 4 / 8 / 16 のアニメーターが付属しているか。なければ、インストーラーのどの公開メソッドで生成できるか
  - LICENSE の本文（同梱してよいか）
- [ ] **Step 3: Modular Avatar の型名を確かめて設計書に書く。** `ModularAvatarParameters`、`ModularAvatarMenuInstaller`、`ModularAvatarMergeAnimator`、`ModularAvatarBoneProxy` の名前空間、アセンブリ名、使うフィールド名
- [ ] **Step 4: Commit**（設計書のみ）

### Task 2: 試し送信スクリプト（Python）

**Files:**
- Create: `Tools/translator/kat_osc.py`
- Create: `Tools/translator/kat_send_test.py`
- Test: `Tools/translator/test_kat_osc.py`

**Interfaces:**
- Consumes: Task 1 の変換式、文字表、パラメータ名
- Produces（`kat_osc.py`）:
  - `TEXT_LIMIT = 128`、`CLEAR_POINTER = 255`
  - `char_code(ch: str) -> int` … 文字セットにない文字は「?」の番号
  - `code_to_float(code: int) -> float`
  - `osc_message(address: str, value: bool | int | float) -> bytes`
  - `build_messages(text: str, sync: int = 8) -> list[list[bytes]]` … 送る順のグループ。グループの間で待つ
  - `send(text: str, host: str = "127.0.0.1", port: int = 9000, sync: int = 8, step_delay: float = 0.25) -> None`

- [ ] **Step 1: 失敗するテストを書く**（`test_kat_osc.py`）

```python
def test_ascii_codes(self):            # 値は Task 1 の表で確定させる
    self.assertEqual(kat_osc.char_code(" "), 0)
    self.assertEqual(kat_osc.char_code("A"), 33)
def test_unknown_char_becomes_question(self):
    self.assertEqual(kat_osc.char_code("漢"), kat_osc.char_code("?"))
    self.assertEqual(kat_osc.char_code("😀"), kat_osc.char_code("?"))
def test_float_mapping(self):
    self.assertAlmostEqual(kat_osc.code_to_float(0), 0.0)
    self.assertAlmostEqual(kat_osc.code_to_float(127), 1.0)
    self.assertAlmostEqual(kat_osc.code_to_float(255), -1 / 127)
def test_osc_message_layout(self):
    self.assertEqual(kat_osc.osc_message("/a", 1), b"/a\x00\x00,i\x00\x00\x00\x00\x00\x01")
    self.assertEqual(kat_osc.osc_message("/a", True), b"/a\x00\x00,T\x00\x00")
    self.assertEqual(len(kat_osc.osc_message("/a", 0.5)), 12)
def test_groups(self):
    groups = kat_osc.build_messages("Good morning", sync=8)
    # 先頭: Visible=True と Pointer=255。続いて 8 文字ごとに1グループ（Pointer 1, 2）
    self.assertEqual(len(groups), 3)
    self.assertEqual(len(groups[1]), 9)   # Pointer + CharSync 8本
def test_last_group_padded_with_spaces(self):
    groups = kat_osc.build_messages("Hi", sync=8)
    self.assertEqual(len(groups[1]), 9)
def test_truncates_at_limit(self):
    self.assertEqual(len(kat_osc.build_messages("x" * 300, sync=8)), 1 + 16)
def test_empty_text_only_clears(self):
    self.assertEqual(len(kat_osc.build_messages("", sync=8)), 1)
```

- [ ] **Step 2: 失敗を確かめる。** Run: `python -m unittest discover -s Tools/translator -v` → `ModuleNotFoundError`
- [ ] **Step 3: `kat_osc.py` を実装する。** OSC のアドレスは `/avatar/parameters/<名前>`。Bool は型タグ `T` / `F`、Int は big-endian の int32、Float は big-endian の float32
- [ ] **Step 4: `kat_send_test.py` を実装する。** `python kat_send_test.py "Good morning" [--host H] [--port P] [--sync N]`。`kat_osc.send` を呼ぶだけ
- [ ] **Step 5: テストが通ることを確かめる。** 同じコマンドで全件 OK
- [ ] **Step 6: Commit**

### Task 3: 土台とアセット生成（Unity）

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Translator/Translator.Editor.asmdef`（参照: `VRC.SDK3A`、`VRC.SDKBase`、Modular Avatar のアセンブリ、`Fire.Editor`）
- Create: `Assets/natsuki_veer02/Editor/Translator/TranslatorConfig.cs`
- Create: `Assets/natsuki_veer02/Editor/Translator/TranslatorAssetGenerator.cs`
- Test: `Assets/natsuki_veer02/Editor/Translator/Tests/Translator.Editor.Tests.asmdef`、`Tests/TranslatorAssetTests.cs`

**Interfaces:**
- Produces（`TranslatorConfig`、すべて `public const` / `static readonly`）:
  - `OutputDir = "Assets/natsuki_veer02/Translator"`、`PrefabPath = OutputDir + "/TranslatorPet.prefab"`
  - `ActiveParam = "TranslatorActive"`、`LocalAIParam = "TranslatorLocalAI"`、`KatVisibleParam = "KAT_Visible"`、`KatPointerParam = "KAT_Pointer"`、`KatClearPointer = 255`
  - `KatSyncCount = 8`（4 / 8 / 16）
  - `RootName = "TranslatorPet"`、`PetRootName = "Pet_Root"`、`BlockName = "Pet_Block"`、`TextName = "Pet_Text"`、`SignRootName = "Sign_Root"`、`SignBoardName = "Sign_Board"`
  - `BlockSize = 0.25f`、`PetOffset = new Vector3(0.5f, 0f, 0f)`、`TextHeightAboveBlock = 0.15f`、`SignHeightAboveHead = 0.25f`、`SignWidth = 0.6f`
  - `SignText = "Since I can't speak your language, I'm using a translator."`
  - `MenuLabel = "翻訳"`、`ActiveLabel = "翻訳"`、`LocalAILabel = "ローカルAI"`
- Produces（`TranslatorAssetGenerator`）:
  - `static Material BlockMaterial(string outputDir)` … 目を描いた 256×256 の PNG と、Unity 標準シェーダーのマテリアル
  - `static Material SignMaterial(string outputDir)` … `SignText` を描いた PNG（透明な背景、横長）と、`Unlit/Transparent` のマテリアル。すでに `Sign.png` があれば PNG は作り直さない（差し替えた画像を残すため）

- [ ] **Step 1: 失敗するテストを書く**（`TranslatorAssetTests`）
  - `BlockMaterial_CreatesTextureAndStandardMaterial`: PNG とマテリアルが `OutputDir` にでき、シェーダー名が `Standard`
  - `SignMaterial_IsUnlitAndTextureHasVisiblePixels`: シェーダー名が `Unlit/Transparent`。テクスチャに、アルファが 0 より大きい画素と 0 の画素の両方がある
  - `SignMaterial_KeepsReplacedImage`: `Sign.png` を別の画像に置き換えてからもう一度呼ぶと、ファイルの中身が変わらない
  - `Generators_AreIdempotent`: 2回呼んでも、`OutputDir` のファイル数が増えない
  - テストは `OutputDir` ではなく一時フォルダ（`Assets/__TranslatorTest`）に出力し、`TearDown` で消す
- [ ] **Step 2: 失敗を確かめる。** Run: テストコマンド（`-testFilter TranslatorAssetTests`）→ コンパイルエラー
- [ ] **Step 3: 実装する。** 看板の文字は、エディターの `GUIStyle` で一時的な `RenderTexture` に描いて PNG に保存する
- [ ] **Step 4: テストが通ることを確かめる**
- [ ] **Step 5: Commit**（`.meta` も含める）

### Task 4: アニメーター

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Translator/TranslatorAnimationBuilder.cs`
- Test: `Tests/TranslatorAnimationTests.cs`

**Interfaces:**
- Consumes: `TranslatorConfig`、Task 1 の調査結果
- Produces:
  - `static AnimatorController BuildToggleController(string outputDir)` … `Translator_Toggle.controller`
  - `static AnimatorController BuildKatController(string outputDir, int syncCount)` … `Translator_KAT.controller`
  - アニメーションのパスは、プレハブのルートからの相対パス（`Pet_Root`、`Sign_Root`、`Pet_Root/Pet_Text`）

- [ ] **Step 1: 失敗するテストを書く**
  - `Toggle_HasOffAndOnStates_WriteDefaultsOff`: レイヤー1つ、ステート `Off`（既定）と `On`、どちらも `writeDefaultValues == false`
  - `Toggle_ClipsDriveBothRoots`: `On` のクリップは `Pet_Root` と `Sign_Root` の `m_IsActive` を 1 に、`Off` は 0 にする
  - `Toggle_TransitionsUseActiveParam`: 遷移の条件が `TranslatorActive` の If / IfNot、Exit Time なし、遷移時間 0
  - `Toggle_DriversSetKatParams`: `On` の `VRCAvatarParameterDriver` は `KAT_Visible = 1`。`Off` は `KAT_Visible = 0` と `KAT_Pointer = 255`
  - `Kat_AllStatesWriteDefaultsOff_AndTargetPetText`: すべてのステートが Write Defaults オフ。クリップが指すパスはすべて `Pet_Root/Pet_Text`
  - `Kat_HasCharSyncParamsForSyncCount`: `syncCount` が 4 / 8 / 16 のそれぞれで、`KAT_CharSync…` のパラメータがその本数ある
- [ ] **Step 2: 失敗を確かめる**
- [ ] **Step 3: `BuildToggleController` を実装する**
- [ ] **Step 4: `BuildKatController` を実装する。** Task 1 の結果で決める。インストーラーに公開の生成メソッドがあれば、それを呼んで `outputDir` に作る。なければ、付属のアニメーターとクリップを `outputDir` に複製し、クリップのパスを `Pet_Root/Pet_Text` に書き換える。KAT のフォルダの中身は書き換えない
- [ ] **Step 5: テストが通ることを確かめる**
- [ ] **Step 6: Commit**

### Task 5: プレハブの組み立て

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Translator/TranslatorPrefabBuilder.cs`
- Create: `Assets/natsuki_veer02/Editor/Translator/TranslatorBudget.cs`
- Test: `Tests/TranslatorPrefabTests.cs`

**Interfaces:**
- Consumes: Task 3、Task 4 の生成メソッド
- Produces:
  - `static GameObject TranslatorPrefabBuilder.Build(string outputDir)` … 保存したプレハブのアセットを返す
  - `static int TranslatorBudget.SyncedBits(int katSyncCount)` … `2 + (10 + 8 * katSyncCount)`。かっこの中が KAT の分で、公式の値（同期1本で 18、8本で 74）と合う。10 の内訳（`KAT_Pointer` の 8 と Bool 2本。`KAT_Visible` 以外の Bool の名前）は Task 1 で確かめ、MA Parameters にも入れる
  - `static int TranslatorBudget.UsedBits(VRCExpressionParameters p)` … 同期ありのパラメータだけを数える

- [ ] **Step 1: 失敗するテストを書く**
  - `Prefab_HasExpectedHierarchy`: 設計書の階層どおりの名前と親子関係
  - `Prefab_RootsStartInactive`: `Pet_Root` と `Sign_Root` が非アクティブ、ルートはアクティブ
  - `Prefab_BlockSizeAndPlacement`: `Pet_Root` の位置が `(0.5, 0, 0)`。ブロックの一辺が 0.25 で、底が地面の高さ。`Pet_Text` はブロックの上面から 0.15 上
  - `Prefab_BlockHasNoCollider`: `Collider` が1つもない
  - `Prefab_SignHasTwoBoardsBackToBack`: `Sign_Board` の下に板が2枚あり、向きが 180 度ちがう
  - `Prefab_SignUsesHeadBoneProxy`: `Sign_Root` に Bone Proxy があり、対象が Head。頭の骨が見つからないときの位置として、`Sign_Root` 自身の高さが 1.5 以上
  - `Prefab_ParametersMatchSpec`: MA Parameters の中身が設計書の表どおり（名前、型、同期、保存）。`TranslatorActive` は保存なし・既定値オフ
  - `Prefab_MenuHasSubmenuWithTwoToggles`: Menu Installer のメニューが、サブメニュー「翻訳」1つ。その中にトグル「翻訳」（`TranslatorActive`）と「ローカルAI」（`TranslatorLocalAI`）
  - `Prefab_MergesTwoFxControllers`: Merge Animator が2つ、どちらも FX、パスは相対
  - `Build_IsIdempotent`: 2回作っても、プレハブの子の数と `outputDir` のファイル数が同じ
  - `Budget_SyncedBitsMatchesParameters`: 4 / 8 / 16 のそれぞれで、`SyncedBits` が MA Parameters から数えた合計と同じ
- [ ] **Step 2: 失敗を確かめる**
- [ ] **Step 3: 実装する。** ブロックは `GameObject.CreatePrimitive(PrimitiveType.Cube)` から作り、コライダーを外す。看板の板は `Quad`。文字オブジェクトは、KAT 付属のプレハブかメッシュとマテリアルから組む（Task 1 の結果どおり）
- [ ] **Step 4: テストが通ることを確かめる**
- [ ] **Step 5: Commit**

### Task 6: メニュー項目とアバターへの配置

**Files:**
- Create: `Assets/natsuki_veer02/Editor/Translator/TranslatorSetup.cs`
- Test: `Tests/TranslatorSetupTests.cs`

**Interfaces:**
- Consumes: `TranslatorPrefabBuilder.Build`、`TranslatorBudget`、`FireSetup.ResolvePrefabPath`（アバターの決め方を炎・銃と同じにする）
- Produces:
  - `[MenuItem("Tools/Translator Pet/Build Prefab")] static void BuildPrefab()`
  - `[MenuItem("Tools/Translator Pet/Add To Avatar")] static void AddToAvatar()`
  - `static GameObject Place(GameObject avatarRoot)` … 置いたインスタンス（すでにあればそれ）を返す
  - `static void Preflight()` … Modular Avatar か KAT がなければ、入れるものを書いた例外を投げる

- [ ] **Step 1: 失敗するテストを書く**（`reko.prefab` を一時シーンに出して使う）
  - `Place_AddsInstanceUnderAvatarRoot`: アバター直下に `TranslatorPet` が1つでき、プレハブのインスタンスになっている
  - `Place_Twice_DoesNotDuplicate`: 2回呼んでも1つ。同じオブジェクトが返る
  - `Place_DoesNotTouchAvatarAssets`: 呼ぶ前後で、`reko` の FX、メニュー、パラメータのファイルの中身（バイト列）が同じ
  - `Place_Throws_WhenBudgetExceeded`: 同期パラメータを 200 ビット使っているアバターでは例外。メッセージに、合計のビット数と「同期設定を下げる」が入っている。インスタンスは作られない
  - `Place_Throws_WhenNoAvatarDescriptor`: `VRCAvatarDescriptor` のないオブジェクトでは例外
- [ ] **Step 2: 失敗を確かめる**
- [ ] **Step 3: 実装する**
- [ ] **Step 4: Translator のテストを全部流して、通ることを確かめる。** あわせて炎と銃のテスト（`FireSetupTests`、`GunSetupTests` など既存の全件）も流し、通ることを確かめる
- [ ] **Step 5: Commit**

### Task 7: 実機での確認と記録

**Files:**
- Modify: `docs/superpowers/specs/2026-11-16-translator-pet-design.md`（確認結果を追記）
- Modify: `Assets/natski2.unity`（`reko` にプレハブを置いた状態。利用者に確認してから保存する）

- [ ] **Step 1:** Unity で「Build Prefab」→ `reko` を選んで「Add To Avatar」。コンソールにエラーがないこと
- [ ] **Step 2:** Play モード（Modular Avatar が適用された状態）で、FX に2つのレイヤーが足され、メニューに「翻訳」があることを確かめる。画像を撮って、ブロック、文字、看板の位置を確かめる
- [ ] **Step 3: 利用者に頼む。** VRChat の Build & Test でトグルをオンにし、`python Tools/translator/kat_send_test.py "Good morning"` を実行して、設計書の受け入れ基準を上から確かめてもらう
- [ ] **Step 4:** `python Tools/translator/kat_send_test.py "おはよう オハヨウ 漢字"` を送り、どこまで出るかを設計書に書く
- [ ] **Step 5:** `git diff --stat` で、`reko` の FX、メニュー、パラメータに今回の変更が入っていないことを確かめる
- [ ] **Step 6: Commit**
