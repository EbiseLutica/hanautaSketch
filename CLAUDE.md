# hanautaSketch

## 概要

メロディとベースラインを手軽にスケッチし、MIDIで書き出して編曲に持っていくための作曲支援ツール。目玉は「スマートトラック」で、ベースライン（任意でメロディも）からコード進行を判別して和音を鳴らしたり、パラメータからドラムを生成したりする。将来は鼻歌からのメロディ起こしも予定している。

仕様と設計の詳細は Notion の企画ページ（hanautaSketch）にまとめてある。

## 技術情報

- C# / .NET 10。主な対象は Windows。macOS でも動く（ショートカットは Ctrl の代わりに ⌘）
  - macOS ではビルド出力を `.app` バンドルの形（`bin/<構成>/hanautaSketch.app`、`macOS/Info.plist`）にする。バンドルでないと AppKit が英語のアプリとして扱うため
  - メニューは macOS が画面上端の `NativeMenu`（並び・表記は macOS の慣例）、Windows がウィンドウ内の `Menu`。Avalonia が足すアプリ名メニューの標準項目は `App` で日本語に置き換える
  - 取り消す・やり直すは、文字の入力欄にフォーカスがある間はその入力欄に向ける（`ITextEditTarget`）
- UI：Avalonia 12。見た目は Lutica Design System（`Themes/LuticaTheme.axaml`。FluentTheme をパレット・リソース差し替えで土台にし、Button / ToggleButton は独自の ControlTheme）、MVVM は CommunityToolkit.Mvvm。コンパイル済みバインディングが既定なので、XAML には `x:DataType` を付ける
  - Avalonia API は推測で書かず、Avalonia MCP（`lookup_avalonia_api` / `search_avalonia_docs`）で確認する
- ピアノロールは `Control.Render(DrawingContext)` で自前描画する。配色は `LuticaTheme.axaml` のテーマ辞書に `PianoRoll*Brush` として置き（DS と同じ色は `Lu*` を `StaticResource` で参照）、`PianoRollPalette.Load` がテーマごとに読んでペンに組み立てる。コントロールはテーマ・リソースが変わるまで使い回す
  - 色・影・角丸は `LuticaTheme.axaml` の `Lu*` リソース（`LuPanelBrush`、`LuShadowControl` など）を `DynamicResource` で参照し、値を直書きしない。ボタンの修飾は `Classes`（`primary` / `danger` / `ghost` / `small` など）、補助テキストは `TextBlock.dimmed` / `.caption`
  - フォントはシステムフォント（Fluent の既定に任せる）。書体は同梱しない。Lutica の Koruri は使わない
  - 表示文字列は `HanautaSketch/Resources/Strings.resx`（英語・既定）と `Strings.ja.resx` に置き、`Strings.Designer.cs` のプロパティ経由で使う（XAML は `{x:Static res:Strings.X}`、書式付きは `Strings.Format`）。キーを足すときは両方の resx とプロパティを足す。言語は OS に従い、設定（`AppSettings.Language`）で上書きできる（反映は次回起動時）
  - Engine の例外メッセージは英語。UI に出る文言は App 側で訳す（プロジェクト形式の例外は `ErrorDetail` で表示文に置き換える）
- テスト：xUnit
- 配布は NativeAOT（`PublishAot`）。リフレクション前提の処理は書かない。JSON は `JsonSerializerContext` のソース生成で読み書きする（`ProjectSerializer`、`AppSettingsStore`）
- CI：GitHub Actions（`.github/workflows/ci.yml`）。PR と master への push で Windows / macOS / Linux のテストを行う。publish（win-x64 / osx-arm64（`.app`）/ linux-x64 / linux-arm64）は master への push と手動実行のみで、PR では行わず、成果物を Artifacts に上げる（SF2 は含まない）。AOT はビルド環境の glibc にリンクされるので、Linux は ubuntu-22.04 でビルドする（glibc 2.35 以上で動く）
- 再生：MeltySynth（SF2）でシンセを鳴らす。出力は全 OS で Silk.NET.OpenAL（OpenAL Soft を常に使う。Windows では内部で WASAPI）。NAudio は COM interop が NativeAOT で動かないので使わない。MeltySynth とシーケンサーは Engine（`PlaybackEngine`）、出力は App（`IAudioOutput`、`AudioOutput.Create`）に置く
  - 録音（鼻歌の採譜）も OpenAL Soft のキャプチャ（`Silk.NET.OpenAL.Extensions.EXT`）を使い、App の `IAudioInput`（`AudioInput.Start`）に置く。カウントインとクリックは `PlaybackOptions` を渡して `PlaybackEngine` が鳴らす（10ch のウッドブロック）。macOS は `Info.plist` の `NSMicrophoneUsageDescription` が要る
  - 音源（SF2）は同梱しない（GM 互換のものは 100MB を超えて git に置けず、LFS は帯域枠を食うため）。利用者が設定画面で選び、パスを `AppSettings.SoundFontPath` に保存する。変えたらその場で読み込み直す。未設定・見つからないときは起動時に選ぶよう促す
- 保存形式：独自 JSON（`.hsk`、`ProjectSerializer`）。ファイル上の形式は DTO で固定し、`version` で互換性を管理する（`"version": "0.6"` のメジャー.マイナー表記。正式版で 1.0 にする。0.x の間はマイナーまで含めて新しいファイルを弾く。初期の整数表記 1 / 2 は 0.1 / 0.2 として読む。0.2 でスマートコード、0.3 でスマートドラムのトラック種別を追加、0.4 でコードの N.C. override とドラムのベース追従モード（none / fill / full）、0.5 でテンポ・拍子・キーの途中変更（tempos / meters / keys。0.4 までの曲頭の値 bpm / beatsPerBar / key は読み込みのみ）、0.6 でコードの種類に三和音以外（7th・sus・テンションなど）。0.1 の曲全体のスマートコード設定は読み込み時に無視する）。ファイル選択・確認などのダイアログは `IDialogService` 経由で VM から使う。曲に依存しないアプリ設定は `%APPDATA%\hanautaSketch\settings.json`（`AppSettingsStore`）。設定画面（`SettingsWindow`、DataContext はメインの VM）に SoundFont・テーマ・表示言語・コード遷移データをまとめ、変えたらすぐ反映する（表示言語だけ次回起動時）。macOS はアプリ名メニューの「設定…」、Windows はツールメニューから開く
- 採用予定：MIDI 入出力は DryWetMIDI

### ドメインモデルの約束事

- 時間はすべて tick の整数で扱う（PPQ=480）
- スケール構成音は `Key.GetScalePitchClasses()` 経由でのみ取得する（将来のスケール拡張に備えた差し替え口）
- ショートカットの主修飾キーは `PlatformKeys` 経由で扱う（Windows は Ctrl、macOS は ⌘）。XAML の KeyBinding・メニュー表示は Ctrl で書き、macOS では起動時に `PlatformKeys.Adapt` が ⌘ に置き換える
- `HanautaSketch.Engine.Core.Key` は `Avalonia.Input.Key` と名前が衝突するため、UI 側では using エイリアスで解決する
- スマートトラックの出力は派生データとして扱い、保存しない。ユーザーの判断は override として保持する
  - `SmartChordTrack` はソース（ベース・メロディの `NoteTrack` 参照）、判別設定、伴奏設定、override を持ち、`Regenerate(song)` で判別と伴奏生成をやり直す。再生は全トラック共通の `Track.PlaybackNotes` を使う
  - ソース参照は保存時にトラックの並び順（インデックス）で書き出す
- MIDI チャンネルは `MidiChannels.Assign` で決める（ドラムは 10ch、それ以外はトラック順。再生と書き出しで共通）
- ピアノロールで編集するのは `INoteEditable`（ノートトラック、ドラムの fix 区間）。スマートトラックの生成ノーツは読み取り専用
- 鼻歌の採譜は、曲のテンポ（クリック・伴奏）に合わせて歌ってもらう前提で、テンポは推定しない。録音の秒は `Conductor.SecondsToTick` で tick にし、グリッドに吸着する。録音した範囲の既存ノーツは置き換える（`HummingTranscriber.ReplaceRange`）
  - 録音は選択中のノートトラックに、再生開始位置の1小節前からカウントインして行い、録音中は録音先のトラックを鳴らさない。録音の先頭が曲のどの時刻かは、鳴らし始めた時点の取り込み済みサンプル数と出力の遅れ（`IAudioOutput.LatencySeconds`）から求め、残りの遅れは設定のレイテンシ補正で引く
  - 録音の設定（入力デバイス・伴奏・メトロノーム・グリッド・スケール・レイテンシ補正）は `AppSettings.Recording`
- コードの種類（`ChordQuality`）は構成音と表記を `ChordQualities` の表で持つ。自動判別は三和音だけを使い、それ以外は override と候補のランキングで使う
  - override の候補は、実際の曲から集計した「前のコード → 次のコード」の表（`ChordTransitionTable`、`Smart/Chords/Data/chord-transitions.json` を埋め込み）で並べる。判別の遷移スコア（`ChordTransitions`）とは別物
  - 集計処理はエンジン（`Smart/Chords/Corpus`）にある。同梱の表は McGill Billboard（CC0）だけから `tools/ChordStats` で作る
  - Chordonomicon（CC BY-NC 4.0）は同梱しない。利用者が設定画面からライセンスに同意したときだけ、アプリが裏で取得（リビジョン固定）・集計し、表だけを `%APPDATA%\hanautaSketch\chord-transitions-extended.json` に保存する（`ChordDataStore`）。元の CSV は保存しない
  - 出典はヘルプメニューと THIRD_PARTIES.md に書いている（リポジトリ自体は MIT、フィクスチャはパブリックドメイン）。依存パッケージを足したら THIRD_PARTIES.md にも追記する
- 判別アルゴリズムの回帰テストは `HanautaSketch.Tests/Fixtures/*.hsk` と MML 風の期待値（`ChordFixtureTests`）で行う
- テンポ・拍子・キーの変化は `Song.Conductor` が持つ（SMF のコンダクタートラック相当）。テンポとキーは tick、拍子は小節番号で変更点を持ち、どれも曲頭に必ず1つある。拍子は n/4 のみ
  - 小節と tick の変換（`BarAt` / `BarToTick` / `PositionInBar` など）、tick と秒の変換（`TickToSeconds` / `SecondsToTick`）は `Conductor` 経由で行い、小節長を固定値で計算しない
  - コード判別はキーの区間（`KeyRegions`）ごとに、その区間のキーで行う

## ディレクトリ構成

```
HanautaSketch.slnx
HanautaSketch/               Avalonia アプリ（UI）
  Audio/                     オーディオの出力・入力（IAudioOutput / IAudioInput：OpenAL）
  Controls/                  自前描画のカスタムコントロール（PianoRollControl など）
  Services/                  View 依存の処理を VM から使うための窓口（ダイアログ）、アプリ設定
  Themes/                    Lutica Design System のテーマ（LuticaTheme.axaml）
  ViewModels/
  Views/
HanautaSketch.Engine/        UI 非依存のロジック。Tests に InternalsVisibleTo
  Audio/                     MeltySynth による再生エンジン（PlaybackEngine）
  Core/                      ドメインモデル（Song, Track, Note, Key）
  Editing/                   編集の補助（Undo/Redo 履歴）
  IO/                        プロジェクトファイルの読み書き（今後 MIDI 書き出しも）
  Smart/Chords/              スマートコード（トラック、コード判別、遷移スコア、伴奏生成）
  Smart/Drums/               スマートドラム（トラック、セクション、パターン生成、fix）
  Smart/Rhythm/              ベースのリズム解析（コードの区切り判定、ドラムのベース追従）
  Transcription/             鼻歌の採譜（YIN によるピッチ検出、音の切り出し、スケール寄せ、グリッド吸着）
HanautaSketch.Tests/         Engine のテスト
tools/
  ChordStats/                同梱のコード遷移の表（Billboard のみ）を作るツール（ファイルベースの C#）。元データは .cache/ に取得（git 管理外）
```

## コマンド

```bash
dotnet build
dotnet test
dotnet run --project HanautaSketch
dotnet run tools/ChordStats/ChordStats.cs   # 同梱のコード遷移の表を作り直す（Billboard、約340KBを取得）
```
