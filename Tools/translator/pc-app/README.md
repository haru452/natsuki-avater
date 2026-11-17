# VRC Translator PC（translator_pc.exe）

スマホの翻訳アプリと VRChat をつなぐ、PC 側のプログラムです。

- VRChat ⇔ スマホの OSC を中継します（VRChat 9000/9001、スマホから 9002、スマホへ 9001）
- アバターの「翻訳」がオンのあいだだけ、PC のマイクの音をスマホへ送ります（UDP 9003）
- スマホで「相手の声」をオンにしたとき、VRChat の音だけ（YouTube や Discord は入りません）をスマホへ送ります

## 使い方

1. デスクトップの「VRC Translator PC」をダブルクリック
2. 画面に出る「この PC の IP」をスマホのアプリに入れる（初回だけ）
3. スマホのアプリで「自分の声のマイク: PC」にして開始
4. VRChat でアバターの「翻訳」をオン

終わるときは Ctrl+C か、ウィンドウを閉じます。自動では起動しません。

初回に Windows のファイアウォールの確認が出たら「プライベート ネットワーク」を許可してください。

## 作り方（開発者向け）

```
powershell -ExecutionPolicy Bypass -File publish.ps1
```

`publish/translator_pc.exe`（.NET を入れなくても動く 1 ファイル）ができ、デスクトップにショートカットを作ります。
テストは `dotnet test`。

`Tools/translator/pc/`（Python の中継）は古い版です。ポートがぶつかるので同時に動かさないでください。

使っているもの: .NET 8（MIT）、NAudio 2.2.1（MIT）。
