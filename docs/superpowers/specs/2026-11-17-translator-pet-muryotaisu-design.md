# 翻訳ペットを無量大雛（Muryotaisu）にする — 設計

## 目的
自分用のアバター（riko2）では、ロボットの代わりに Unity Asset Store の「無量大雛 Muryotaisu」をペットにする。
BOOTH で配る版はロボットのまま（Asset Store の素材は再配布できないため）。

## 決まったこと（利用者）
- ペットはロボットではなく Muryotaisu（`Assets/Mryotaisu/Models/Muryotaisu.fbx`）
- 大きさはアバターの身長の 0.6 倍
- 歩くときは Walk モーション、止まると IdleA
- 1 分動かず・しゃべらないと「寝る」の代わりに別の待ちモーション（IdleB）。動くかしゃべると戻る
- 撫でられたら笑顔（Smile）になり、ロボットと同じくハートを出す

## 作り方
- `TranslatorConfig.PetModelPath` にモデルのパスを書くと、そのモデルを使う。空かファイルが無ければロボット（BOOTH 版）
- モデルは FBX を置いて展開し、Animator は外す（VRChat のアバターの中で別の Animator を動かさない）
- Muryotaisu のモーションは Humanoid。アバターの FX からは別の人型を動かせないので、ビルド時に
  モデルへ当てて 1/30 秒ごとに各ボーンの回転（Hips は位置も）を記録し、普通の Transform アニメーションに焼く
  （FX レイヤーで動かせる）。表情のブレンドシェイプのカーブはそのまま写す（Smile は撫で用なので除く）
- 動きのレイヤーは 1 つ（Body）: Idle(IdleA) ⇄ Walk（速さ）、Idle → Drowsy（静か 60 秒）→ Bored(IdleB ループ)
  → 動く/しゃべる → Wake（Jump を 1 回）→ Idle。ボーンを 1 つのレイヤーでしか書かないので、レイヤー同士で取り合わない
- 撫で（Pat）レイヤー: Hearts でハート ON と Smile=100、Calm で Smile=0
- 寝る（Sleep）レイヤーと Z はロボットのときだけ
- 大きさ: アバターに置くとき（Add To Avatar）、アバターの Head ボーンの高さ × 0.6 ÷ モデルの Head ボーンの高さで
  Pet_Robot を拡大縮小。字幕とハートの高さもそれに合わせる。撫でる判定は Head ボーンの子なので一緒に動く

## 利用者の判断（私の判断として記録）
- 起きたときの「笑顔で戻る」は、撫での Smile と同じ表情を 2 つのレイヤーで書くと取り合うので、Jump を 1 回にした
- 立っているときのうろうろ（前後に動く）はモデルでは止める（足が動かないまま滑って見えるため）
