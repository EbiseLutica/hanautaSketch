using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;

namespace HanautaSketch.Tests.Editing;

public class VelocityEditingTests
{
    private static List<Note> Notes() =>
    [
        new(0, 120, 60, 100),
        new(0, 120, 64, 100),   // 和音
        new(480, 120, 62, 100),
        new(960, 120, 65, 100),
    ];

    [Fact]
    public void Click_SetsChordAtThatPosition()
    {
        var notes = Notes();
        var changed = VelocityEditing.ApplyStroke(notes, null, 0, 40, 0, 40, tolerance: 10);

        Assert.True(changed);
        Assert.Equal([40, 40, 100, 100], notes.Select(n => n.Velocity).ToArray());
    }

    [Fact]
    public void Stroke_InterpolatesAlongTime()
    {
        var notes = Notes();
        VelocityEditing.ApplyStroke(notes, null, 0, 20, 960, 120, tolerance: 0);

        Assert.Equal([20, 20, 70, 120], notes.Select(n => n.Velocity).ToArray());
    }

    [Fact]
    public void Stroke_WorksRightToLeft()
    {
        var notes = Notes();
        VelocityEditing.ApplyStroke(notes, null, 960, 120, 0, 20, tolerance: 0);

        Assert.Equal([20, 20, 70, 120], notes.Select(n => n.Velocity).ToArray());
    }

    [Fact]
    public void Stroke_OnlyAffectsTargets()
    {
        var notes = Notes();
        VelocityEditing.ApplyStroke(notes, new HashSet<int> { 0, 3 }, 0, 50, 960, 50, tolerance: 0);

        Assert.Equal([50, 100, 100, 50], notes.Select(n => n.Velocity).ToArray());
    }

    [Fact]
    public void Velocity_IsClampedToMidiRange()
    {
        var notes = Notes();
        VelocityEditing.ApplyStroke(notes, null, 0, -30, 960, 400, tolerance: 0);

        Assert.Equal(1, notes[0].Velocity);
        Assert.Equal(127, notes[3].Velocity);
    }

    [Fact]
    public void NoChange_ReturnsFalse()
    {
        var notes = Notes();
        Assert.False(VelocityEditing.ApplyStroke(notes, null, 0, 100, 960, 100, tolerance: 0));
    }
}
