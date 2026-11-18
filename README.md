# hanautaSketch

メロディとベースラインをさっとスケッチして、MIDI で書き出せる作曲支援ツール

## 主な機能

### 基本的なMIDI編集

- ピアノロール
- トラック管理：GM準拠の音色選択やミュート、ソロ切り替えなど
- Conductor：テンポ、拍子を曲の途中で変更。キーごとにスケールの行をハイライト

### スマートトラック機能

ベースラインとメロディを元に伴奏を自動生成するトラック。

- スマートコード：コード進行を自動判別してコードMIDIを自動生成
  - 手動で修正することもできる
- スマートドラム：区間ごとにパターンを設定して自動でドラム打ち込み

### その他

- 内蔵サウンドフォントプレイヤー
- SMF形式への書き出し

### 対応予定

- 鼻歌からのメロディ起こし
- MIDI外部入力による打ち込み

## 動かし方

Windows、[.NET 10 SDK](https://dotnet.microsoft.com/)、GM 互換の SF2 サウンドフォントを用意する

1. このリポジトリを clone する
2. SF2 を `HanautaSketch/SoundFonts/` に置く
3. `dotnet run --project HanautaSketch` で起動する

SF2 がなくても起動して編集はできるが、音は鳴らない

## 基本の使い方

1. ツールバーで BPM とキーを決める
2. Melody と Bass のトラックにノートを置く
3. 「＋コード」でスマートコードを追加する。コードレーンのクリックで、判別されたコードを差し替える
4. 「＋ドラム」でスマートドラムを追加する。セクションレーンのドラッグで区間を作り、パターンを選ぶ
5. Space で再生
6. 「ファイル → MIDI 書き出し」で SMF出力

### ピアノロールの主な操作

| 操作 | 機能 |
| --- | --- |
| 空き場所をクリック／ドラッグ | ノートを置く・そのまま動かす |
| 右クリック | ノートの長さを次に置く長さにする |
| ダブルクリック | ノートを消す |
| Ctrl+ドラッグ（macOS は ⌘） | 矩形選択 |
| Shift+クリック | 選択に追加／外す |
| ルーラーをクリック | 再生開始位置（貼り付け先もここ） |
| 中ボタンドラッグ | スクロール |
| Ctrl+C / X / V、Delete | コピー・切り取り・貼り付け・削除 |
| Ctrl+Z / Ctrl+Y | 元に戻す／やり直す |

## 開発

C# / .NET 10、UI は Avalonia 12、再生は MeltySynth＋NAudio（Windows）／Silk.NET.OpenAL（macOS）。

| パス | 中身 |
| --- | --- |
| `HanautaSketch/` | 本体。UIとか |
| `HanautaSketch.Engine/` | ビジネスロジック |
| `HanautaSketch.Tests/` | テスト |
| `tools/ChordStats/` | コード遷移表ジェネレーター |

```bash
# ビルド / テスト
dotnet build
dotnet test

# アプリの起動
cd ./HanautaSketch && dotnet run

# コード遷移表の自動生成
dotnet run tools/ChordStats/ChordStats.cs
```

最後のコマンドは同梱の遷移表（Billboard のみ）を作り直すときだけ使う。初回は元データ（約 340KB）を `tools/ChordStats/.cache/` に取得する

## ライセンス

ソースコードは [MIT License](./LICENSE)

同梱のコード遷移表（`chord-transitions.json`）は McGill Billboard（CC0）だけから作っている。ツールメニューから取得できる高度なコード遷移データは Chordonomicon（CC BY-NC 4.0）を使っていて、取得時にライセンスへの同意を求める（そのデータは非営利の目的でのみ利用できる）

テスト上のフィクスチャ hsk ファイルはパブリックドメイン

データセットとライブラリの出典・ライセンスは [THIRD_PARTIES.md](./THIRD_PARTIES.md) を参照
