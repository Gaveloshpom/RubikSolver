using System;
using System.Collections.Generic;
using Rubik.Core;
using Xunit;

namespace Rubik.Tests
{
    public class CubeTests
    {
        // ------------------------------------------------------------ helpers

        private static Cube Scrambled(int n, int length, int seed)
        {
            var c = new Cube(n);
            c.Apply(Scrambler.Generate(n, length, seed));
            return c;
        }

        /// <summary>Whether the whole strip (row or column) of the face has the expected color.</summary>
        private static bool Line(Cube c, Face face, bool isRow, int index, Face expected)
        {
            for (int i = 0; i < c.Size; i++)
            {
                byte v = isRow ? c[face, index, i] : c[face, i, index];
                if (v != (byte)expected) return false;
            }
            return true;
        }

        private static int CountDifferences(Cube a, Cube b)
        {
            int diff = 0;
            var sa = a.Stickers;
            var sb = b.Stickers;
            for (int i = 0; i < sa.Length; i++)
                if (sa[i] != sb[i]) diff++;
            return diff;
        }

        private static Cube After(int n, string notation)
        {
            var c = new Cube(n);
            c.Apply(notation);
            return c;
        }

        // ------------------------------------------------------------ basic properties

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        [InlineData(6)] [InlineData(7)] [InlineData(10)] [InlineData(25)]
        public void NewCube_IsSolved(int n)
        {
            Assert.True(new Cube(n).IsSolved);
        }

        [Fact]
        public void Constructor_RejectsInvalidSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Cube(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Cube(-3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Cube(Cube.MaxSize + 1));
        }

        [Fact]
        public void Apply_RejectsLayerOutsideCube()
        {
            var c = new Cube(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Apply(new Move(Axis.X, 3, 1)));
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(8)]
        public void StickerColourCounts_AreInvariant(int n)
        {
            var c = Scrambled(n, 100, seed: 7);
            var counts = new int[6];
            foreach (byte b in c.Stickers) counts[b]++;
            for (int i = 0; i < 6; i++) Assert.Equal(n * n, counts[i]);
        }

        // ------------------------------------------------------------ the main test from the plan

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        [InlineData(6)] [InlineData(10)] [InlineData(25)]
        public void Scramble50ThenInverse_RestoresSolvedState(int n)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var scramble = Scrambler.Generate(n, 50, seed);
                var c = new Cube(n);

                c.Apply(scramble);
                c.Apply(MoveSequence.Invert(scramble));

                Assert.True(c.Equals(new Cube(n)));   // exact sticker match, not just IsSolved
            }
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(8)]
        public void FourQuarterTurns_AreIdentity_ForEveryAxisAndLayer(int n)
        {
            var c = Scrambled(n, 40, seed: 3);
            var before = c.Clone();

            for (int axis = 0; axis < 3; axis++)
                for (int layer = 0; layer < n; layer++)
                {
                    for (int t = 0; t < 4; t++) c.Apply(new Move((Axis)axis, layer, 1));
                    Assert.True(c.Equals(before));
                }
        }

        // ------------------------------------------------------------ move directions (checked by hand)

        [Fact]
        public void R_MovesStickersLikeTheRealCube()
        {
            var c = After(3, "R");
            // R clockwise: F -> U -> B -> D -> F
            Assert.True(Line(c, Face.U, false, 2, Face.F));
            Assert.True(Line(c, Face.B, false, 0, Face.U));
            Assert.True(Line(c, Face.D, false, 2, Face.B));
            Assert.True(Line(c, Face.F, false, 2, Face.D));
            Assert.True(Line(c, Face.U, false, 0, Face.U));   // the left columns are untouched
        }

        [Fact]
        public void U_MovesStickersLikeTheRealCube()
        {
            var c = After(3, "U");
            // U: F -> L -> B -> R -> F
            Assert.True(Line(c, Face.L, true, 0, Face.F));
            Assert.True(Line(c, Face.B, true, 0, Face.L));
            Assert.True(Line(c, Face.R, true, 0, Face.B));
            Assert.True(Line(c, Face.F, true, 0, Face.R));
        }

        [Fact]
        public void F_MovesStickersLikeTheRealCube()
        {
            var c = After(3, "F");
            // F: U -> R -> D -> L -> U
            Assert.True(Line(c, Face.R, false, 0, Face.U));
            Assert.True(Line(c, Face.D, true, 0, Face.R));
            Assert.True(Line(c, Face.L, false, 2, Face.D));
            Assert.True(Line(c, Face.U, true, 2, Face.L));
        }

        [Fact]
        public void L_MovesStickersLikeTheRealCube()
        {
            var c = After(3, "L");
            // L: U -> F -> D -> B -> U
            Assert.True(Line(c, Face.F, false, 0, Face.U));
            Assert.True(Line(c, Face.D, false, 0, Face.F));
            Assert.True(Line(c, Face.B, false, 2, Face.D));
            Assert.True(Line(c, Face.U, false, 0, Face.B));
        }

        [Fact]
        public void D_MovesStickersLikeTheRealCube()
        {
            var c = After(3, "D");
            // D: F -> R -> B -> L -> F
            Assert.True(Line(c, Face.R, true, 2, Face.F));
            Assert.True(Line(c, Face.B, true, 2, Face.R));
            Assert.True(Line(c, Face.L, true, 2, Face.B));
            Assert.True(Line(c, Face.F, true, 2, Face.L));
        }

        [Fact]
        public void B_MovesStickersLikeTheRealCube()
        {
            var c = After(3, "B");
            // B: U -> L -> D -> R -> U
            Assert.True(Line(c, Face.L, false, 0, Face.U));
            Assert.True(Line(c, Face.D, true, 2, Face.L));
            Assert.True(Line(c, Face.R, false, 2, Face.D));
            Assert.True(Line(c, Face.U, true, 0, Face.R));
        }

        // ------------------------------------------------------------ algebraic properties

        [Theory]
        [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void SexyMove_HasOrderSix(int n)
        {
            var c = new Cube(n);
            for (int k = 1; k <= 6; k++)
            {
                c.Apply("R U R' U'");
                if (k < 6) Assert.False(c.Equals(new Cube(n)));
            }
            Assert.True(c.Equals(new Cube(n)));
        }

        [Fact]
        public void MovesOnDifferentAxes_DoNotCommute()
        {
            Assert.False(After(3, "R U").Equals(After(3, "U R")));
        }

        [Fact]
        public void LayersOfTheSameAxis_Commute()
        {
            var rng = new Random(11);
            for (int trial = 0; trial < 200; trial++)
            {
                int axis = rng.Next(3);
                var a = new Move((Axis)axis, rng.Next(5), rng.Next(1, 4));
                var b = new Move((Axis)axis, rng.Next(5), rng.Next(1, 4));

                var c1 = Scrambled(5, 30, seed: trial);
                var c2 = c1.Clone();
                c1.Apply(a); c1.Apply(b);
                c2.Apply(b); c2.Apply(a);
                Assert.True(c1.Equals(c2));
            }
        }

        [Theory]
        [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(8)]
        public void SingleLayerMove_TouchesExactlyRingStickers_OnSolvedCube(int n)
        {
            var solved = new Cube(n);

            // Outer face: the face itself is one color, so only the 4N ring changes.
            // Inner slice: also exactly the 4N ring.
            for (int layer = 0; layer < n; layer++)
            {
                var c = new Cube(n);
                c.Apply(new Move(Axis.X, layer, 1));
                Assert.Equal(4 * n, CountDifferences(c, solved));
            }
        }

        // ------------------------------------------------------------ whole-cube rotations

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
        public void WholeCubeRotation_KeepsCubeSolved_ButChangesOrientation(int n)
        {
            foreach (var rot in new[] { "x", "y", "z", "x'", "y2", "z2" })
            {
                var c = After(n, rot);
                Assert.True(c.IsSolved);
                Assert.False(c.Equals(new Cube(n)));
            }
        }

        [Fact]
        public void Rotations_AreEquivalentToOuterPlusMiddleMoves_On3x3()
        {
            Assert.True(After(3, "x").Equals(After(3, "R M' L'")));
            Assert.True(After(3, "y").Equals(After(3, "U E' D'")));
            Assert.True(After(3, "z").Equals(After(3, "F S B'")));
        }

        // ------------------------------------------------------------ net

        [Fact]
        public void NetString_OfSolvedCube_HasExpectedShape()
        {
            string expected = string.Join("\n", new[]
            {
                "   UU",
                "   UU",
                "LL FF RR BB",
                "LL FF RR BB",
                "   DD",
                "   DD",
            });
            Assert.Equal(expected, new Cube(2).ToNetString());
        }
    }
}
