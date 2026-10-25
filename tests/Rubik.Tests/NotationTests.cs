using System;
using System.Collections.Generic;
using Rubik.Core;
using Xunit;

namespace Rubik.Tests
{
    public class NotationTests
    {
        private static Cube After(int n, string notation)
        {
            var c = new Cube(n);
            c.Apply(notation);
            return c;
        }

        [Fact]
        public void Parse_BasicTokens()
        {
            var moves = Notation.Parse("R U R' U' F2 B2'", 3);
            Assert.Equal(6, moves.Count);
            Assert.Equal(new Move(Axis.X, 0, 0, 1), moves[0]);
            Assert.Equal(new Move(Axis.X, 0, 0, 3), moves[2]);
            Assert.Equal(new Move(Axis.Z, 0, 0, 2), moves[4]);
            Assert.Equal(new Move(Axis.Z, 2, 2, 2), moves[5]);   // B2' == B2
        }

        [Fact]
        public void Parse_EmptyOrWhitespace_GivesNoMoves()
        {
            Assert.Equal(0, Notation.Parse("", 3).Count);
            Assert.Equal(0, Notation.Parse("   \t ", 3).Count);
        }

        [Fact]
        public void Parse_NegativeFaces_AreMirroredOntoTheAxis()
        {
            // L, D, B: the outer layer "on the other side of the axis", clockwise as seen from their side
            Assert.Equal(new Move(Axis.X, 4, 4, 3), Notation.Parse("L", 5)[0]);
            Assert.Equal(new Move(Axis.Y, 4, 4, 1), Notation.Parse("D'", 5)[0]);
            Assert.Equal(new Move(Axis.Y, 3, 3, 3), Notation.Parse("2D", 5)[0]);
            Assert.Equal(new Move(Axis.X, 2, 4, 3), Notation.Parse("3Lw", 5)[0]);
        }

        [Fact]
        public void Parse_WideAndNumberedMoves()
        {
            Assert.Equal(new Move(Axis.X, 0, 1, 1), Notation.Parse("Rw", 5)[0]);
            Assert.Equal(new Move(Axis.X, 0, 1, 1), Notation.Parse("r", 5)[0]);
            Assert.Equal(new Move(Axis.X, 0, 2, 1), Notation.Parse("3Rw", 5)[0]);
            Assert.Equal(new Move(Axis.X, 2, 2, 1), Notation.Parse("3R", 5)[0]);
        }

        [Fact]
        public void WideMove_EqualsItsSingleLayers()
        {
            Assert.True(After(4, "Rw").Equals(After(4, "R 2R")));
            Assert.True(After(5, "3Rw'").Equals(After(5, "R' 2R' 3R'")));
            Assert.True(After(5, "2Lw2").Equals(After(5, "L2 2L2")));
        }

        [Theory]
        [InlineData("Q", 3)]
        [InlineData("R3", 3)]
        [InlineData("R''", 3)]
        [InlineData("Rw2w", 3)]
        [InlineData("0R", 3)]
        [InlineData("4R", 3)]       // there is no layer 4 in a 3x3
        [InlineData("4Rw", 3)]
        [InlineData("M", 4)]        // M/E/S only for odd N
        [InlineData("2x", 3)]
        [InlineData("xw", 3)]
        [InlineData("99999999999R", 3)]
        public void Parse_InvalidInput_ThrowsFormatException(string text, int n)
        {
            Assert.Throws<FormatException>(() => Notation.Parse(text, n));
        }

        [Theory]
        [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(7)]
        public void FormatThenParse_GivesTheSameCubeState(int n)
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var moves = Scrambler.Generate(n, 60, seed);
                // add wide moves and rotations to cover every branch of the formatter
                moves.Add(new Move(Axis.X, 0, n - 1, 1));
                if (n >= 3)
                {
                    moves.Add(new Move(Axis.Y, 1, n - 1, 3));   // range up to the opposite face
                    moves.Add(new Move(Axis.Z, 0, n - 2, 2));   // range from the positive face
                }
                if (n >= 5) moves.Add(new Move(Axis.X, 1, n - 2, 1));  // inner range

                string text = Notation.Format(moves, n);

                var direct = new Cube(n);
                direct.Apply(moves);
                var viaText = new Cube(n);
                viaText.Apply(text);

                Assert.True(direct.Equals(viaText));
            }
        }

        [Fact]
        public void Format_ProducesReadableNotation()
        {
            var moves = Notation.Parse("R U' F2 2R 3Rw x", 5);
            Assert.Equal("R U' F2 2R 3Rw x", Notation.Format(moves, 5));
        }

        // ------------------------------------------------------------ MoveSequence

        [Fact]
        public void Simplify_MergesAndCancelsNeighbours()
        {
            Assert.Equal("R2", Notation.Format(MoveSequence.Simplify(Notation.Parse("R R", 3)), 3));
            Assert.Equal("", Notation.Format(MoveSequence.Simplify(Notation.Parse("R R'", 3)), 3));
            Assert.Equal("", Notation.Format(MoveSequence.Simplify(Notation.Parse("R U U' R'", 3)), 3));
            Assert.Equal("R'", Notation.Format(MoveSequence.Simplify(Notation.Parse("R2 R", 3)), 3));
            Assert.Equal("R U", Notation.Format(MoveSequence.Simplify(Notation.Parse("R U", 3)), 3));
        }

        [Fact]
        public void Simplify_KeepsTheSameCubeState()
        {
            for (int n = 2; n <= 6; n++)
                for (int seed = 0; seed < 10; seed++)
                {
                    var moves = Scrambler.Generate(n, 40, seed);
                    // insert repeats and cancellations on purpose so Simplify has work to do
                    var noisy = new List<Move>();
                    foreach (var m in moves) { noisy.Add(m); noisy.Add(m); noisy.Add(m.Inverse()); }

                    var a = new Cube(n); a.Apply(noisy);
                    var b = new Cube(n); b.Apply(MoveSequence.Simplify(noisy));
                    Assert.True(a.Equals(b));
                }
        }

        [Fact]
        public void Invert_UndoesTheSequence()
        {
            var moves = Notation.Parse("R U2 F' 2L Bw", 4);
            var c = new Cube(4);
            c.Apply(moves);
            c.Apply(MoveSequence.Invert(moves));
            Assert.True(c.Equals(new Cube(4)));
        }
    }
}
