using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;

namespace HanautaSketch.Tests.Editing;

/// <summary>
/// スケールロックの音高の寄せ方と、スケール度数での移動を確かめるテスト。
/// 音高は MIDI ノート番号（60 = C4）。
/// </summary>
public class ScaleLockTests
{
    private static readonly Key CMajor = new(0, Mode.Major);
    private static readonly Key AMinor = new(9, Mode.Minor);
    private static readonly Key FSharpMajor = new(6, Mode.Major);

    /// <summary>
    /// スケール内の音はそのまま、スケール外の音は行の中の位置で近い方のスケール音に寄ることを確かめる。
    /// C# の行の中央（等距離）なら低い C、上寄りなら D。
    /// </summary>
    [Theory]
    [InlineData(60.0, 60)]
    [InlineData(61.0, 60)]
    [InlineData(61.3, 62)]
    [InlineData(60.8, 60)]
    [InlineData(65.6, 65)]
    public void Snap_GoesToNearestScalePitch(double pitch, int expected)
    {
        Assert.Equal(expected, ScaleLock.Snap(pitch, CMajor));
    }

    /// <summary>
    /// 音域の端で片側にスケール音がないときは、音域内の側に寄ることを確かめる。
    /// F# メジャーでは 0（C）も 127（G）もスケール外。
    /// </summary>
    [Fact]
    public void Snap_StaysInRangeAtEdges()
    {
        Assert.Equal(1, ScaleLock.Snap(0, FSharpMajor));
        Assert.Equal(126, ScaleLock.Snap(127, FSharpMajor));
        Assert.Equal(1, ScaleLock.Snap(-3.0, FSharpMajor));
        Assert.Equal(127, ScaleLock.Snap(130.0, CMajor));
        Assert.Equal(126, ScaleLock.Snap(130.0, FSharpMajor));
    }

    /// <summary>
    /// 通し番号とスケール音が相互に変換でき、音域内のスケール音の数が合うことを確かめる。
    /// C メジャーは 0〜119 に 10 オクターブ×7音、120〜127 に C D E F G の5音。
    /// </summary>
    [Fact]
    public void Steps_RoundTrip()
    {
        Assert.Equal(75, ScaleLock.StepCount(CMajor));
        Assert.Equal(35, ScaleLock.ToStep(60, CMajor));
        Assert.Equal(60, ScaleLock.FromStep(35, CMajor));
        Assert.Equal(127, ScaleLock.FromStep(74, CMajor));
        Assert.Null(ScaleLock.FromStep(75, CMajor));
        Assert.Null(ScaleLock.FromStep(-1, CMajor));
    }

    /// <summary>
    /// 度数での移動がキーのスケールに沿うことを確かめる。マイナーでも同じ。
    /// </summary>
    [Fact]
    public void MoveBySteps_FollowsScale()
    {
        Assert.Equal(64, ScaleLock.MoveBySteps(60, CMajor, 2));
        Assert.Equal(59, ScaleLock.MoveBySteps(60, CMajor, -1));
        Assert.Equal(71, ScaleLock.MoveBySteps(69, AMinor, 1));
        Assert.Equal(72, ScaleLock.MoveBySteps(69, AMinor, 2));
        Assert.Equal(67, ScaleLock.MoveBySteps(69, AMinor, -1));
        Assert.Equal(66, ScaleLock.MoveBySteps(64, new Key(4, Mode.Minor), 1));
    }

    /// <summary>
    /// スケール外の音は、最寄りのスケール音に寄せてから度数で動かすことを確かめる。
    /// </summary>
    [Fact]
    public void MoveBySteps_SnapsOutOfScalePitchFirst()
    {
        Assert.Equal(60, ScaleLock.MoveBySteps(61, CMajor, 0));
        Assert.Equal(62, ScaleLock.MoveBySteps(61, CMajor, 1));
    }

    /// <summary>
    /// 複数の音をそろって動かすとき、どれか1音でも音域をはみ出す度数は、はみ出さない所まで縮めることを確かめる。
    /// 124（E）の上には F・G の2音しかない。
    /// </summary>
    [Fact]
    public void ClampSteps_KeepsAllNotesInRange()
    {
        Assert.Equal(2, ScaleLock.ClampSteps([(124, CMajor), (60, CMajor)], 5));
        Assert.Equal(-1, ScaleLock.ClampSteps([(2, CMajor), (60, AMinor)], -4));
        Assert.Equal(3, ScaleLock.ClampSteps([(60, CMajor), (69, AMinor)], 3));
    }

    /// <summary>
    /// 音の集合が違うキーが混ざっていても、音ごとに自分のキーで度数を数えて収めることを確かめる。
    /// 126 は F# メジャーの最上音、123 の上には 125・126 の2音だけ、1 は F# メジャーの最低音。
    /// </summary>
    [Fact]
    public void ClampSteps_UsesEachNotesOwnKey()
    {
        Assert.Equal(0, ScaleLock.ClampSteps([(126, FSharpMajor), (60, CMajor)], 3));
        Assert.Equal(0, ScaleLock.ClampSteps([(1, FSharpMajor), (60, CMajor)], -2));
        Assert.Equal(2, ScaleLock.ClampSteps([(123, FSharpMajor), (60, CMajor)], 5));
        Assert.Equal(0, ScaleLock.ClampSteps([(60, CMajor), (126, FSharpMajor)], 3));
    }

    /// <summary>
    /// 半音の境界ちょうど（行の上端）で、両側がスケール音なら低い方になることを確かめる。E(64) と F(65) の間。
    /// </summary>
    [Fact]
    public void Snap_BoundaryBetweenScalePitches_GoesLower()
    {
        Assert.Equal(64, ScaleLock.Snap(64.5, CMajor));
        Assert.Equal(59, ScaleLock.Snap(59.5, CMajor));
        Assert.Equal(65, ScaleLock.Snap(64.51, CMajor));
    }

    /// <summary>
    /// 0 も 127 もスケール外のキー（F# メジャー）でも、通し番号とスケール音の変換が合うことを確かめる。
    /// 0〜119 に 70 音、120〜127 に 121・123・125・126 の4音。
    /// </summary>
    [Fact]
    public void Steps_KeyWithoutEdgePitches()
    {
        Assert.Equal(74, ScaleLock.StepCount(FSharpMajor));
        Assert.Equal(0, ScaleLock.ToStep(1, FSharpMajor));
        Assert.Equal(0, ScaleLock.ToStep(0, FSharpMajor));
        Assert.Equal(1, ScaleLock.FromStep(0, FSharpMajor));
        Assert.Equal(126, ScaleLock.FromStep(73, FSharpMajor));
        Assert.Null(ScaleLock.FromStep(74, FSharpMajor));
    }

    /// <summary>
    /// 度数での移動が音域の端のスケール音で止まることを確かめる。スケール外の端の音は寄せてから止まる。
    /// </summary>
    [Fact]
    public void MoveBySteps_StopsAtRangeEdges()
    {
        Assert.Equal(127, ScaleLock.MoveBySteps(127, CMajor, 1));
        Assert.Equal(0, ScaleLock.MoveBySteps(0, CMajor, -3));
        Assert.Equal(126, ScaleLock.MoveBySteps(126, FSharpMajor, 5));
        Assert.Equal(1, ScaleLock.MoveBySteps(0, FSharpMajor, -1));
    }

    /// <summary>
    /// ドラッグの度数が、カーソル位置を掴んだ音のキーで寄せた音と、掴んだ音（スケール外なら寄せた音）の差になることを確かめる。
    /// 63（D#）は D と E の真ん中なので D に寄り、1度上。他の音が音域の端にあれば、そこで止める。
    /// </summary>
    [Fact]
    public void DragSteps_MeasuresInAnchorKeyAndClamps()
    {
        Assert.Equal(1, ScaleLock.DragSteps(60, CMajor, 63.0, [(60, CMajor)]));
        Assert.Equal(2, ScaleLock.DragSteps(61, CMajor, 64.2, [(61, CMajor)]));
        Assert.Equal(2, ScaleLock.DragSteps(60, CMajor, 64.0, [(60, CMajor), (61, FSharpMajor)]));
        Assert.Equal(0, ScaleLock.DragSteps(60, CMajor, 63.0, [(60, CMajor), (126, FSharpMajor)]));
    }

    /// <summary>
    /// 掴んだ音のキーが C メジャー以外でも、そのキーで度数を数えることを確かめる。
    /// F# メジャーでは 61（C#）→65（E#）は2度（C メジャーで数えると3度）。下向きのドラッグと下端で止まる場合も見る。
    /// </summary>
    [Fact]
    public void DragSteps_UsesAnchorKeyAndWorksDownward()
    {
        Assert.Equal(2, ScaleLock.DragSteps(61, FSharpMajor, 65.4, [(61, FSharpMajor)]));
        Assert.Equal(-2, ScaleLock.DragSteps(64, CMajor, 60.0, [(64, CMajor)]));
        Assert.Equal(0, ScaleLock.DragSteps(64, CMajor, 60.0, [(64, CMajor), (1, FSharpMajor)]));
    }
}
