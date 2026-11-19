using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Audio;

/// <summary>
/// 再生の設定。通常の再生は既定値のまま使い、録音ではカウントインやクリックを足す。
/// </summary>
public sealed record PlaybackOptions
{
    /// <summary>開始位置の前に鳴らすカウントインの拍数（開始位置のテンポで刻む）。0 ならなし</summary>
    public int CountInBeats { get; init; }

    /// <summary>開始位置以降もクリックを鳴らすか</summary>
    public bool Metronome { get; init; }

    /// <summary>曲のノーツを鳴らすか。false ならクリックだけ</summary>
    public bool PlaySong { get; init; } = true;

    /// <summary>鳴らさないトラック（録音先など）</summary>
    public Track? ExcludedTrack { get; init; }

    /// <summary>
    /// 鳴らさないトラック（ミュート・ソロを反映）の音を最初から入れないか。チャンネルを共有するトラック（ドラムの 10ch など）でも
    /// トラック単位でミュートが効くが、再生中のミュート・ソロの切り替えは反映できない（WAV 書き出し用）
    /// </summary>
    public bool AudibleTracksOnly { get; init; }

    /// <summary>曲の終わりで止めず、<see cref="PlaybackEngine.Stop"/> まで鳴らし続けるか</summary>
    public bool Continuous { get; init; }
}
