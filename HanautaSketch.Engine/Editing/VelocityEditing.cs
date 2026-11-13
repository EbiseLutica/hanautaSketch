using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Editing;

/// <summary>
/// ベロシティのなぞり描き。時間軸上の線分（開始 tick とベロシティ → 終了 tick とベロシティ）に沿って、
/// 範囲内で発音するノートのベロシティを線形補間した値に揃える。
/// 同じ tick のノート（和音）は同じ値になる。
/// </summary>
public static class VelocityEditing
{
    public const int MinVelocity = 1;
    public const int MaxVelocity = 127;

    /// <param name="notes">編集対象のノーツ（その場で書き換える）</param>
    /// <param name="targets">編集してよいノートのインデックス。null なら全ノート</param>
    /// <param name="tolerance">線分の両端をこの tick 数だけ広げる（クリック1点でもノートに当たるように）</param>
    /// <returns>1つでも変化したら true</returns>
    public static bool ApplyStroke(IList<Note> notes, IReadOnlySet<int>? targets,
        double fromTick, double fromVelocity, double toTick, double toVelocity, double tolerance)
    {
        var start = Math.Min(fromTick, toTick) - tolerance;
        var end = Math.Max(fromTick, toTick) + tolerance;
        var span = toTick - fromTick;
        var changed = false;

        for (var i = 0; i < notes.Count; i++)
        {
            if (targets is not null && !targets.Contains(i)) continue;

            var note = notes[i];
            if (note.Tick < start || note.Tick > end) continue;

            var t = Math.Abs(span) < 1e-9 ? 0 : Math.Clamp((note.Tick - fromTick) / span, 0, 1);
            var velocity = ClampVelocity(fromVelocity + (toVelocity - fromVelocity) * t);
            if (note.Velocity == velocity) continue;

            notes[i] = note with { Velocity = velocity };
            changed = true;
        }

        return changed;
    }

    public static int ClampVelocity(double velocity) => (int)Math.Clamp(Math.Round(velocity), MinVelocity, MaxVelocity);
}
