# natsuki_ver02

VRChat アバター用の Unity プロジェクト（ギミックと翻訳ペット）。

## ブランチ

この README はどのブランチにも同じものが入っています。

| ブランチ | 中身 | 分かれた元 |
|---|---|---|
| `main` | 共通の土台。Fire・Gun のギミックと翻訳ペット（標準はロボットのペット） | ― |
| `pet-muryotaisu` | ペットが **Natsuki**。人型ペットの動きはまずここで作る | `main` |
| `pet-riko2` | ペットが **Homare（誉）**。動きは Natsuki と同じで、モデル・表情の名前・名札が違う | `pet-muryotaisu` |
| `translator-app` | 翻訳アプリ（PC 版 `translator_pc.exe`）。PC の中で声を文字にして DeepL で訳す | `main` |

### `main`
- Fire・Gun のギミック（`Assets/natsuki_veer02/Editor/Fire`、`Gun`）。
- 翻訳ペットの土台（`Assets/natsuki_veer02/Editor/Translator`）。人型モデルを設定しなければロボットのペットになる（BOOTH 版はこちら）。

### `pet-muryotaisu`（Natsuki）
- モデル: Asset Store の Muryotaisu（`Assets/Mryotaisu`）。**再配布できないので git には入れていない**。
- 人型ペットの動き:
  - ついて歩く・走る。走っても離されにくく、12 m 以上離れたときだけ瞬間移動する。
  - 左右の近いほうで待つ。鏡モード。
  - 見つめると笑う。しゃがんだ人の顔も見る。なでるとハートが出る。
  - 名前を呼ぶと振り向いて笑う。
  - 声の命令（座って・待ってて・おいで・立って・後ろ向いて・前向いて）。
- 走る・座る・しゃがむ動きは、コードで作ったオリジナル（`Assets/natsuki_veer02/PetMotions`）。待機・歩きなどは Muryotaisu の動き。
- 名札「Natsuki2026」。

### `pet-riko2`（Homare）
- `pet-muryotaisu` を取り込んで作っている。ペットの動きを変えるときは、まず `pet-muryotaisu` で作り、それをこのブランチに取り込む。
- モデル: XAvatar で取り込んだ homare（`Assets/homare`。**git には入れていない**）。モデルは `TranslatorConfig.Pet`（設定の中身は `TranslatorPetProfile`）で選ぶ。riko2 の設定も残してある。
- 表情: 笑顔 `Fcl_ALL_Joy`、まばたき `Fcl_EYE_Close`。
- 動きはすべてコードで作ったオリジナル（`PetMotions`）。Asset Store の動きは使っていない。
- 名札は今は出していない（`ShowNameTag = false`）。Homare の写真を撮ってから作り直す予定。

### `translator-app`（翻訳アプリ）
- `Tools/translator/pc-app`: `translator_pc.exe`（C#）。
  - 流れ: PC のマイク → 音声認識（SenseVoice、日本語は ReazonSpeech）→ DeepL → ペットの上の字幕／チャットボックス。
  - ペットの名前（誉・なつき）を聞くと、ペットを振り向かせる。続けて日本語の命令を言うと、そのとおり動かす。
  - 使い方は `Tools/translator/pc-app/README.md`。
- `Tools/translator/phone`（Gemini を使うスマホ版）と `Tools/translator/pc`（Python の中継）は古い版。

## アバターのメニュー（翻訳ペット）
- **ペット**: ペットを出す・しまう。オンの間、PC アプリがマイクで聞く。
- **翻訳**: 翻訳のオン・オフ。オフでも、名前を呼ばれたときの反応と声の命令は動く。
- **ローカルAI**: まだ中身がない（オンにしても DeepL のまま）。
- **鏡モード**: ペットが前ではなく、すぐ横で待つ。

## ペットを作り直すとき（Unity）
1. Tools → Translator Pet → **Build Prefab**。
2. アバターを選んで **Add To Avatar**。モデルを替えたときは、一度消してから置き直す（大きさを合わせ直すため）。
3. アップロードする。

## git に入れていないもの
`Assets/Mryotaisu`（Natsuki のモデル）、`Assets/homare`・`Assets/riko2`（VRoid のモデル）、Asset Store の有料素材、翻訳アプリの音声認識モデル（`Tools/translator/pc-app/models`）。
