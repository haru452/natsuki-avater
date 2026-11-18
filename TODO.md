# TODO

## 翻訳に DeepL の代わりに Gemini も選べるようにする（未着手・2026-11-18 に相談）

DeepL API は登録（キーの取得）が面倒なので、Gemini API でも訳せるようにして、どちらを使うか選べるようにする。
作業は `translator-app` ブランチ（`Tools/translator/pc-app`）。

1. **設定に「翻訳に使うもの」を足す**: `Settings` に `Translator`（`DeepL` / `Gemini`）と `GeminiKey` を足す。
   - 画面のキー操作で切り替えとキーの入力ができるようにする（例: **G** = Gemini のキー、**T** = DeepL ⇄ Gemini）。
   - キーは DeepL と同じく `settings.json` に保存し、画面には出さない。
2. **Gemini で訳す部分を作る**: `ITranslator` を実装した `GeminiTranslator` を足す（今の `DeepL.cs` と同じ形）。
   - 送るのは声ではなく、PC の中で文字にした文だけ。
   - 指示文は古いスマホ版（`Tools/translator/phone/gemini.py`）のものを参考にする（友だち同士の自然な言い方、短めに）。
   - 混んでいるとき（429）の待ち直しと、つながらないときのエラー表示も DeepL と同じようにする。
3. **画面と README**: 今どちらで訳しているかを画面に出す。README に Gemini のキーの取り方（https://aistudio.google.com/apikey）を書く。
4. **（任意）アバターのメニューで切り替える**: 中身のない「ローカルAI」スイッチを、DeepL ⇄ Gemini の切り替えに使うこともできる。

注意
- Gemini の無料枠では、送った文が Google のサービス改善に使われることがある（README に書く）。
- Gemini は DeepL より返事が少し遅いことがある。字幕が出るまでの時間を画面で比べる。

（ペットの TODO は `pet-muryotaisu` / `pet-riko2` ブランチの TODO.md にある）
