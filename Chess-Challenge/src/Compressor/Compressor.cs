using ChessChallenge.API;
using System;
using System.Drawing;
using System.Linq;


namespace Chess_Challenge.src.Compressor
{


    public static class Compressor
    {
        public static ulong[] data = new ulong[] {
            0x9f2f2000000000d6,0xad88018280898ded,0xffffffffffffffff,0xb5d5e50101001008,
            0x183c7afef8b82800,0xeb10080000406001,0xa4fb550200000000,0x3b030106bd761839,
            0x6b0000000004f4f9,0xb7b19101418011b5,0xffffffffffffffff,0x1008008080a7abad,
            0x141d1f7f5e3c18,0x80060200001008d7,0x40aadf25,0x9c186ebd6080c0dc,
            0x915100b0320021b,0x20901180b5f0008,0x60b090d06690206,0x811100f0f7a0510,
            0x80c0a0c0fff0b0a,0x40e0a0403230810,0x71e0a0911230a13,0xd0c050d084c0503,
            0x90b090c09510709,0xb0b0a0a0b860a0a,0x90e0d120ceb050a,0x910080e09230505,
        };



        public static void Test()
        {
            //(ulong[] components, int[] weights) = UncompressData(data);
            //var board = Board.CreateBoardFromFEN("8/Rr6/8/8/6Kk/8/Q/8 w KQkq d6 0 2");
            //var mb = new MyBot();
            //mb._board = board;
            //int e1 = mb.Evaluate();
            //int e2 = Evaluate(board, components, weights);
        }

        public static (ulong[] components, int[] weights) UncompressData(ulong[] data)
        {
            //ulong[] components = data.Take(16).ToArray();                           //12
            //sbyte[] smallWeights = new sbyte[96];                                   // 8
            //Buffer.BlockCopy(data, 128, smallWeights, 0, 96);                       // 9
            //int[] weights = smallWeights.Select(x => (int)(x * 2.2)).ToArray();     // 21
            //return (components, weights);                                           // 4

            byte[] smallWeights = new byte[96];
            Buffer.BlockCopy(data, 128, smallWeights, 0, 96);
            return (data.Take(16).ToArray(), smallWeights.Select(x => (int)(x * 4.11328125 - 40)).ToArray());

        }

        private static int[] GamePhaseIncrement = { 0, 1, 1, 2, 4, 0 };

        public static int Evaluate(Board board, ulong[] components, int[] weights)
        {
            int mg = 0, eg = 0, gamephase = 0;
            for (int side = 0; side < 2; side++)
            {
                for (int p = 0; p < 6; p++)
                {
                    ulong pieceMask = board.GetPieceBitboard((PieceType)p + 1, side == 0);
                    int y = BitboardHelper.GetNumberOfSetBits(pieceMask);
                    gamephase += GamePhaseIncrement[p] * y;
                    for (int c = 0; c < 8; c++)
                    {
                        int x = BitboardHelper.GetNumberOfSetBits(components[c + side * 8] & pieceMask);
                        //mg += x * weights[p * 8 + c];
                        //eg += x * weights[p * 8 + c + 48];

                        int w_mg = weights[p * 8 + c], w_eg = weights[p * 8 + c + 48];
                        mg += x * w_mg;
                        eg += x * w_eg;
                    }
                }
                mg = -mg;
                eg = -eg;
            }
            return (mg * gamephase + eg * (24 - gamephase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
        }

        public static int EvaluateNew(Board board, ulong[] components, int[] weights)
        {
            int mg = 0, eg = 0, gamephase = 0, side = 0, weightIdx;
            for (; side++ < 2;)
            {
                weightIdx = 0;
                for (int p = 0; p++ < 6;)
                {
                    ulong pieceMask = board.GetPieceBitboard((PieceType)p + 1, side == 0);
                    gamephase += GamePhaseIncrement[p] * BitboardHelper.GetNumberOfSetBits(pieceMask); ;
                    for (int c = 0; c++ < 8;)
                    {
                        int x = BitboardHelper.GetNumberOfSetBits(components[c + side * 8] & pieceMask),
                            w_mg = weights[weightIdx], 
                            w_eg = weights[weightIdx++ + 48];
                        mg += x * w_mg;
                        eg += x * w_eg;
                    }
                }
                mg = -mg;
                eg = -eg;
            }
            return (mg * gamephase + eg * (24 - gamephase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
        }



        #region PESTO_TABLES

        private static int[] mg_pawn_table = {
      0,   0,   0,   0,   0,   0,  0,   0,
     98, 134,  61,  95,  68, 126, 34, -11,
     -6,   7,  26,  31,  65,  56, 25, -20,
    -14,  13,   6,  21,  23,  12, 17, -23,
    -27,  -2,  -5,  12,  17,   6, 10, -25,
    -26,  -4,  -4, -10,   3,   3, 33, -12,
    -35,  -1, -20, -23, -15,  24, 38, -22,
      0,   0,   0,   0,   0,   0,  0,   0
    };

        private static int[] eg_pawn_table = {
      0,   0,   0,   0,   0,   0,   0,   0,
    178, 173, 158, 134, 147, 132, 165, 187,
     94, 100,  85,  67,  56,  53,  82,  84,
     32,  24,  13,   5,  -2,   4,  17,  17,
     13,   9,  -3,  -7,  -7,  -8,   3,  -1,
      4,   7,  -6,   1,   0,  -5,  -1,  -8,
     13,   8,   8,  10,  13,   0,   2,  -7,
      0,   0,   0,   0,   0,   0,   0,   0
    };

        private static int[] mg_knight_table = {
    -167, -89, -34, -49,  61, -97, -15, -107,
     -73, -41,  72,  36,  23,  62,   7,  -17,
     -47,  60,  37,  65,  84, 129,  73,   44,
      -9,  17,  19,  53,  37,  69,  18,   22,
     -13,   4,  16,  13,  28,  19,  21,   -8,
     -23,  -9,  12,  10,  19,  17,  25,  -16,
     -29, -53, -12,  -3,  -1,  18, -14,  -19,
    -105, -21, -58, -33, -17, -28, -19,  -23
    };

        private static int[] eg_knight_table = {
    -58, -38, -13, -28, -31, -27, -63, -99,
    -25,  -8, -25,  -2,  -9, -25, -24, -52,
    -24, -20,  10,   9,  -1,  -9, -19, -41,
    -17,   3,  22,  22,  22,  11,   8, -18,
    -18,  -6,  16,  25,  16,  17,   4, -18,
    -23,  -3,  -1,  15,  10,  -3, -20, -22,
    -42, -20, -10,  -5,  -2, -20, -23, -44,
    -29, -51, -23, -15, -22, -18, -50, -64
    };

        private static int[] mg_bishop_table = {
    -29,   4, -82, -37, -25, -42,   7,  -8,
    -26,  16, -18, -13,  30,  59,  18, -47,
    -16,  37,  43,  40,  35,  50,  37,  -2,
     -4,   5,  19,  50,  37,  37,   7,  -2,
     -6,  13,  13,  26,  34,  12,  10,   4,
      0,  15,  15,  15,  14,  27,  18,  10,
      4,  15,  16,   0,   7,  21,  33,   1,
    -33,  -3, -14, -21, -13, -12, -39, -21
    };

        private static int[] eg_bishop_table = {
    -14, -21, -11,  -8, -7,  -9, -17, -24,
     -8,  -4,   7, -12, -3, -13,  -4, -14,
      2,  -8,   0,  -1, -2,   6,   0,   4,
     -3,   9,  12,   9, 14,  10,   3,   2,
     -6,   3,  13,  19,  7,  10,  -3,  -9,
    -12,  -3,   8,  10, 13,   3,  -7, -15,
    -14, -18,  -7,  -1,  4,  -9, -15, -27,
    -23,  -9, -23,  -5, -9, -16,  -5, -17
    };

        private static int[] mg_rook_table = {
     32,  42,  32,  51, 63,  9,  31,  43,
     27,  32,  58,  62, 80, 67,  26,  44,
     -5,  19,  26,  36, 17, 45,  61,  16,
    -24, -11,   7,  26, 24, 35,  -8, -20,
    -36, -26, -12,  -1,  9, -7,   6, -23,
    -45, -25, -16, -17,  3,  0,  -5, -33,
    -44, -16, -20,  -9, -1, 11,  -6, -71,
    -19, -13,   1,  17, 16,  7, -37, -26
    };

        private static int[] eg_rook_table = {
    13, 10, 18, 15, 12,  12,   8,   5,
    11, 13, 13, 11, -3,   3,   8,   3,
     7,  7,  7,  5,  4,  -3,  -5,  -3,
     4,  3, 13,  1,  2,   1,  -1,   2,
     3,  5,  8,  4, -5,  -6,  -8, -11,
    -4,  0, -5, -1, -7, -12,  -8, -16,
    -6, -6,  0,  2, -9,  -9, -11,  -3,
    -9,  2,  3, -1, -5, -13,   4, -20
    };

        private static int[] mg_queen_table = {
    -28,   0,  29,  12,  59,  44,  43,  45,
    -24, -39,  -5,   1, -16,  57,  28,  54,
    -13, -17,   7,   8,  29,  56,  47,  57,
    -27, -27, -16, -16,  -1,  17,  -2,   1,
     -9, -26,  -9, -10,  -2,  -4,   3,  -3,
    -14,   2, -11,  -2,  -5,   2,  14,   5,
    -35,  -8,  11,   2,   8,  15,  -3,   1,
     -1, -18,  -9,  10, -15, -25, -31, -50
    };

        private static int[] eg_queen_table = {
     -9,  22,  22,  27,  27,  19,  10,  20,
    -17,  20,  32,  41,  58,  25,  30,   0,
    -20,   6,   9,  49,  47,  35,  19,   9,
      3,  22,  24,  45,  57,  40,  57,  36,
    -18,  28,  19,  47,  31,  34,  39,  23,
    -16, -27,  15,   6,   9,  17,  10,   5,
    -22, -23, -30, -16, -16, -23, -36, -32,
    -33, -28, -22, -43,  -5, -32, -20, -41
    };

        private static int[] mg_king_table = {
    -65,  23,  16, -15, -56, -34,   2,  13,
     29,  -1, -20,  -7,  -8,  -4, -38, -29,
     -9,  24,   2, -16, -20,   6,  22, -22,
    -17, -20, -12, -27, -30, -25, -14, -36,
    -49,  -1, -27, -39, -46, -44, -33, -51,
    -14, -14, -22, -46, -44, -30, -15, -27,
      1,   7,  -8, -64, -43, -16,   9,   8,
    -15,  36,  12, -54,   8, -28,  24,  14
    };

        private static int[] eg_king_table = {
    -74, -35, -18, -18, -11,  15,   4, -17,
    -12,  17,  14,  17,  17,  38,  23,  11,
     10,  17,  23,  15,  20,  45,  44,  13,
     -8,  22,  24,  27,  26,  33,  26,   3,
    -18,  -4,  21,  24,  27,  23,   9, -11,
    -19,  -3,  11,  21,  23,  16,   7,  -9,
    -27, -11,   4,  13,  14,   4,  -5, -17,
    -53, -34, -21, -11, -28, -14, -24, -43
    };

        static int[][] pestoTables = new int[][]
        {
            mg_pawn_table, eg_pawn_table,
            mg_knight_table, eg_knight_table,
            mg_bishop_table, eg_bishop_table,
            mg_rook_table, eg_rook_table,
            mg_queen_table, eg_queen_table,
            mg_king_table, eg_king_table,
        };

        #endregion
    }


}
