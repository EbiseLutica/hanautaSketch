# hanautaSketch

## 概要

メロディとベースラインを手軽にスケッチし、MIDIで書き出して編曲に持っていくための作曲支援ツール。目玉は「スマートトラック」で、ベースライン（任意でメロディも）からコード進行を判別して和音を鳴らしたり、パラメータからドラムを生成したりする。将来は鼻歌からのメロディ起こしも予定している。

仕様と設計の詳細は Notion の企画ページ（hanautaSketch）にまとめてある。

## 技術情報

- C# / .NET 10。当面の対象は Windows
- UI：Avalonia 12（FluentTheme）、MVVM は CommunityToolkit.Mvvm。コンパイル済みバインディングが既定なので、XAML には `x:DataType` を付ける
  - Avalonia 12 は v11 から API が変わっている箇所がある（例：`ImmutablePen` は `IImmutableBrush` を要求する、`ActualThemeVariantProperty` がない）。API は推測で書かず、Avalonia MCP（`lookup_avalonia_api` / `search_avalonia_docs`）で確認する
- ピアノロールは `Control.Render(DrawingContext)` で自前描画する。配色は `PianoRollPalette` にテーマ（ライト／ダーク）ごとに持たせる
- テスト：xUnit
- 採用予定：再生は MeltySynth（SF2）＋NAudio（WASAPI）、MIDI 入出力は DryWetMIDI

### ドメインモデルの約束事

- 時間はすべて tick の整数で扱う（PPQ=480）
- スケール構成音は `Key.GetScalePitchClasses()` 経由でのみ取得する（将来のスケール拡張に備えた差し替え口）
- `HanautaSketch.Engine.Core.Key` は `Avalonia.Input.Key` と名前が衝突するため、UI 側では using エイリアスで解決する
- スマートトラックの出力は派生データとして扱い、保存しない。ユーザーの判断は override として保持する
- 試作段階のため、テンポ・拍子・キーは `Song` 上で曲全体に1つの固定値。後で Conductor（途中変更対応）に置き換える

## ディレクトリ構成

```
HanautaSketch.slnx
HanautaSketch/               Avalonia アプリ（UI）
  Controls/                  自前描画のカスタムコントロール（PianoRollControl など）
  ViewModels/
  Views/
HanautaSketch.Engine/        UI 非依存のロジック。Tests に InternalsVisibleTo
  Core/                      ドメインモデル（Song, Track, Note, Key）
  （予定）Smart/ Audio/ IO/  スマートトラック生成、再生、MIDI・プロジェクトの入出力
HanautaSketch.Tests/         Engine のテスト
```

## コマンド

```bash
dotnet build
dotnet test
dotnet run --project HanautaSketch
```
