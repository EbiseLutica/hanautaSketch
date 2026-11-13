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
- 再生：MeltySynth（SF2）でシンセを鳴らし、NAudio 2.2.1（WASAPI 共有モード）で出力する。MeltySynth とシーケンサーは Engine（`PlaybackEngine`）、OS 依存の出力は App（`AudioOutput`）に置く
  - 音源は `HanautaSketch/SoundFonts/*.sf2` に置く（GM 互換の SGM-V2.01 を想定）。サイズが大きいので git 管理外（`.gitignore` で `*.sf2` を除外）。ビルド時に出力先へコピーされる
- 保存形式：独自 JSON（`.hsk`、`ProjectSerializer`）。ファイル上の形式は DTO で固定し、`version` で互換性を管理する（`"version": "0.2"` のメジャー.マイナー表記。正式版で 1.0 にする。0.x の間はマイナーまで含めて新しいファイルを弾く。初期の整数表記 1 / 2 は 0.1 / 0.2 として読む。0.2 は 0.1 にトラック種別を足した上位互換で、0.1 の曲全体のスマートコード設定は読み込み時に無視する）。ファイル選択・確認などのダイアログは `IDialogService` 経由で VM から使う。曲に依存しないアプリ設定は `%APPDATA%\hanautaSketch\settings.json`（`AppSettingsStore`）
- 採用予定：MIDI 入出力は DryWetMIDI

### ドメインモデルの約束事

- 時間はすべて tick の整数で扱う（PPQ=480）
- スケール構成音は `Key.GetScalePitchClasses()` 経由でのみ取得する（将来のスケール拡張に備えた差し替え口）
- `HanautaSketch.Engine.Core.Key` は `Avalonia.Input.Key` と名前が衝突するため、UI 側では using エイリアスで解決する
- スマートトラックの出力は派生データとして扱い、保存しない。ユーザーの判断は override として保持する
  - `SmartChordTrack` はソース（ベース・メロディの `NoteTrack` 参照）、判別設定、伴奏設定、override を持ち、`Regenerate(song)` で判別と伴奏生成をやり直す。再生は全トラック共通の `Track.PlaybackNotes` を使う
  - ソース参照は保存時にトラックの並び順（インデックス）で書き出す
- 判別アルゴリズムの回帰テストは `HanautaSketch.Tests/Fixtures/*.hsk` と MML 風の期待値（`ChordFixtureTests`）で行う
- 試作段階のため、テンポ・拍子・キーは `Song` 上で曲全体に1つの固定値。後で Conductor（途中変更対応）に置き換える

## ディレクトリ構成

```
HanautaSketch.slnx
HanautaSketch/               Avalonia アプリ（UI）
  Audio/                     WASAPI 出力（AudioOutput）
  Controls/                  自前描画のカスタムコントロール（PianoRollControl など）
  Services/                  View 依存の処理を VM から使うための窓口（ダイアログ）、アプリ設定
  SoundFonts/                SF2 置き場（git 管理外）
  ViewModels/
  Views/
HanautaSketch.Engine/        UI 非依存のロジック。Tests に InternalsVisibleTo
  Audio/                     MeltySynth による再生エンジン（PlaybackEngine）
  Core/                      ドメインモデル（Song, Track, Note, Key）
  Editing/                   編集の補助（Undo/Redo 履歴）
  IO/                        プロジェクトファイルの読み書き（今後 MIDI 書き出しも）
  Smart/Chords/              スマートコード（トラック、コード判別、遷移スコア、伴奏生成）
  Smart/Rhythm/              ベースのリズム解析（コードの区切り判定、将来はドラムの FollowBass と共用）
HanautaSketch.Tests/         Engine のテスト
```

## コマンド

```bash
dotnet build
dotnet test
dotnet run --project HanautaSketch
```
