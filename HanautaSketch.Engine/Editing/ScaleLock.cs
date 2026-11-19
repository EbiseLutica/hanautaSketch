using HanautaSketch.Engine.Core;
using static HanautaSketch.Engine.Editing.TranspositionEditing;

namespace HanautaSketch.Engine.Editing;

/// <summary>
/// スケールロック（スケール外の音を置けないモード）での音高の扱い。
/// 音高を最寄りのスケール音に寄せる処理と、スケール度数（音域内のスケール音の通し番号）での移動を持つ。
/// マウスを動かすたびに選択中のノーツ分呼ばれるので、どれも割り当てなしの定数時間で済ませる。
/// </summary>
public static class ScaleLock
{
    /// <summary>
    /// キーのスケール構成音を、ピッチクラスごとの有無と、昇順に並べたピッチクラスで表したもの。
    /// </summary>
    private readonly record struct Scale(bool[] Contains, int[] PitchClasses)
    {
        public static Scale Of(Key key)
        {
            var pitchClasses = key.GetScalePitchClasses().Order().ToArray();
            var contains = new bool[12];
            foreach (var pc in pitchClasses) contains[pc] = true;
            return new Scale(contains, pitchClasses);
        }

        public bool Has(int pitch) => Contains[((pitch % 12) + 12) % 12];

        /// <summary>0 以上 pitch 未満にあるスケール音の数</summary>
        public int CountBelow(int pitch)
        {
            var count = pitch / 12 * PitchClasses.Length;
            foreach (var pc in PitchClasses)
            {
                if (pc < pitch % 12) count++;
            }
            return count;
        }
    }

    private static readonly Dictionary<Key, Scale> Cache = [];

    private static Scale ScaleOf(Key key)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var scale))
                Cache[key] = scale = Scale.Of(key);
            return scale;
        }
    }

    /// <summary>
    /// 小数の音高を、音域内でいちばん近いスケールの音にする。等距離なら低い方。
    /// </summary>
    /// <param name="pitch">MIDI ノート番号（小数可）。行の中央が整数になる</param>
    /// <param name="key">その位置のキー</param>
    public static int Snap(double pitch, Key key)
    {
        var scale = ScaleOf(key);
        // 半音の境界ちょうど（x.5）は低い方の音にする
        var nearest = Math.Clamp((int)Math.Ceiling(pitch - 0.5), MinPitch, MaxPitch);
        if (scale.Has(nearest)) return nearest;

        var below = nearest;
        while (below >= MinPitch && !scale.Has(below)) below--;
        var above = nearest;
        while (above <= MaxPitch && !scale.Has(above)) above++;

        if (below < MinPitch) return above;
        if (above > MaxPitch) return below;
        return pitch - below <= above - pitch ? below : above;
    }

    /// <summary>
    /// スケールの音の、音域内での通し番号（最低のスケール音が 0）。スケール外の音は最寄りに寄せてから数える。
    /// </summary>
    public static int ToStep(int pitch, Key key) => ScaleOf(key).CountBelow(Snap(pitch, key));

    /// <summary>
    /// 通し番号からスケールの音を返す。音域外なら null。
    /// </summary>
    public static int? FromStep(int step, Key key)
    {
        if (step < 0) return null;
        var scale = ScaleOf(key);
        var length = scale.PitchClasses.Length;
        var pitch = step / length * 12 + scale.PitchClasses[step % length];
        return pitch <= MaxPitch ? pitch : null;
    }

    /// <summary>音域内のスケール音の数</summary>
    public static int StepCount(Key key) => ScaleOf(key).CountBelow(MaxPitch + 1);

    /// <summary>
    /// 音高をスケール上で指定の度数だけ動かす。スケール外の音は最寄りに寄せてから動かす。
    /// 音域をはみ出す場合は端のスケール音で止める。
    /// </summary>
    public static int MoveBySteps(int pitch, Key key, int steps)
    {
        var step = Math.Clamp(ToStep(pitch, key) + steps, 0, StepCount(key) - 1);
        return FromStep(step, key)!.Value;
    }

    /// <summary>
    /// 複数の音をそろって動かすときに、どの音も音域をはみ出さないよう度数を収める。
    /// </summary>
    /// <param name="notes">動かす音の音高と、それぞれのキー</param>
    /// <param name="steps">動かしたい度数</param>
    public static int ClampSteps(IEnumerable<(int Pitch, Key Key)> notes, int steps)
    {
        foreach (var (pitch, key) in notes)
        {
            var step = ToStep(pitch, key);
            steps = Math.Clamp(steps, -step, StepCount(key) - 1 - step);
        }
        return steps;
    }

    /// <summary>
    /// 掴んだ音を目標の音高（最寄りのスケール音に寄せる）まで動かすときの度数を求める。
    /// 度数は掴んだ音のキーで数え、どの音も音域をはみ出さないよう収める。
    /// </summary>
    /// <param name="anchorPitch">掴んだ音の元の音高</param>
    /// <param name="anchorKey">掴んだ音の元の位置のキー</param>
    /// <param name="targetPitch">カーソル位置の音高（小数可）</param>
    /// <param name="notes">一緒に動かす音（掴んだ音を含む）の元の音高と、それぞれの元の位置のキー</param>
    public static int DragSteps(int anchorPitch, Key anchorKey, double targetPitch, IEnumerable<(int Pitch, Key Key)> notes)
    {
        var target = Snap(targetPitch, anchorKey);
        var steps = ToStep(target, anchorKey) - ToStep(anchorPitch, anchorKey);
        return ClampSteps(notes, steps);
    }
}
