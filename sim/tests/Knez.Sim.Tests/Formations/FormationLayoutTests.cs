using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Formations;

[TestFixture]
public class FormationLayoutTests
{
    private static FixVec2 V(int xTenths, int yTenths) => new(Fix.Ratio(xTenths, 10), Fix.Ratio(yTenths, 10));

    [Test]
    public void SlotOffset_LineOfEightInTwoRanks_FourWideCentredOnTheAnchor()
    {
        var offsets = Enumerable.Range(0, 8).Select(s => FormationLayout.SlotOffset(TestDefs.Line, 8, s)).ToArray();
        Assert.That(offsets, Is.EqualTo(new[]
        {
            V(-15, 0), V(-5, 0), V(5, 0), V(15, 0),       // front rank: the anchor is its centre
            V(-15, -15), V(-5, -15), V(5, -15), V(15, -15), // second rank 1.5 m behind
        }));
    }

    [Test]
    public void SlotOffset_IncompleteRank_IsTheRearOneAndCentred()
    {
        // 5 in two ranks: 3 in front, 2 behind them, centred.
        var offsets = Enumerable.Range(0, 5).Select(s => FormationLayout.SlotOffset(TestDefs.Line, 5, s)).ToArray();
        Assert.That(offsets, Is.EqualTo(new[] { V(-10, 0), V(0, 0), V(10, 0), V(-5, -15), V(5, -15) }));
    }

    [Test]
    public void SlotOffset_ColumnFixesTheWidth_RanksFollowTheCount()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormationLayout.Width(TestDefs.Column, 6), Is.EqualTo(2));
            Assert.That(FormationLayout.SlotOffset(TestDefs.Column, 6, 5), Is.EqualTo(V(5, -30)));
            Assert.That(FormationLayout.Width(TestDefs.Column, 1), Is.EqualTo(1));
            Assert.That(FormationLayout.SlotOffset(TestDefs.Column, 1, 0), Is.EqualTo(FixVec2.Zero));
        });
    }

    [Test]
    public void SlotOffset_MirroredFiles_HaveExactlyMirroredOffsets()
    {
        // Spacing with an odd raw value: the /2 must truncate symmetrically.
        var odd = new Knez.Sim.Defs.FormationDef("odd", Fix.FromRaw(65537), Fix.One, ranks: 1, width: 0);
        for (int count = 1; count <= 9; count++)
        {
            for (int slot = 0; slot < count; slot++)
            {
                var left = FormationLayout.SlotOffset(odd, count, slot);
                var right = FormationLayout.SlotOffset(odd, count, count - 1 - slot);
                Assert.That(right.X, Is.EqualTo(-left.X), $"count {count}, slot {slot}");
            }
        }
    }

    [TestCase(-1)]
    [TestCase(8)]
    public void SlotOffset_SlotOutsideTheSquad_Throws(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FormationLayout.SlotOffset(TestDefs.Line, 8, slot));
    }

    [Test]
    public void ToWorld_FacingNorth_RightIsEastAndBackIsSouth()
    {
        var anchor = FixVec2.FromInts(10, 10);
        var north = Angle.FromDegrees(90);
        Assert.Multiple(() =>
        {
            Assert.That(FormationLayout.ToWorld(anchor, north, V(10, 0)), Is.EqualTo(FixVec2.FromInts(11, 10)));
            Assert.That(FormationLayout.ToWorld(anchor, north, V(0, -20)), Is.EqualTo(FixVec2.FromInts(10, 8)));
        });
    }

    [Test]
    public void ToWorld_FacingEast_RightIsSouth()
    {
        var anchor = FixVec2.FromInts(10, 10);
        Assert.That(FormationLayout.ToWorld(anchor, Angle.FromDegrees(0), V(10, 0)), Is.EqualTo(FixVec2.FromInts(10, 9)));
    }

    [Test]
    public void ToWorld_FacingSouth_IsTheNorthLayoutTurnedHalfway()
    {
        var anchor = FixVec2.FromInts(10, 10);
        var offset = V(15, -15);
        var north = FormationLayout.ToWorld(anchor, Angle.FromDegrees(90), offset);
        var south = FormationLayout.ToWorld(anchor, Angle.FromDegrees(270), offset);
        Assert.That(south - anchor, Is.EqualTo(-(north - anchor)));
    }

    [Test]
    public void ToWorld_Diagonal_KeepsDistancesWithinAMillimetre()
    {
        var anchor = FixVec2.FromInts(30, 20);
        var facing = Angle.FromDegrees(225);
        var a = FormationLayout.SlotPosition(TestDefs.Line, 8, 0, anchor, facing);
        var b = FormationLayout.SlotPosition(TestDefs.Line, 8, 3, anchor, facing);
        long error = Math.Abs((a - b).Length.Raw - Fix.FromInt(3).Raw); // slots 0 and 3 are 3 m apart
        Assert.That(error, Is.LessThan(66)); // 1 mm = 65.5 raw
    }
}
