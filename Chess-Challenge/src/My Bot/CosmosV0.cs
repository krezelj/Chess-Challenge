using ChessChallenge.API;
using System;
using System.Linq;
using static System.Formats.Asn1.AsnWriter;

public class CosmosV0 : IChessBot
{
    #region DEBUG_VARIABLES

    private int exploredNodes;//#DEBUG;

    #endregion

    #region CONSTANTS

    private readonly int CHECKMATE = 100_000;

    #endregion

    private Board _board;
    private Move _bestMove;
    private int _startDepth;


    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _startDepth = 3;

        exploredNodes = 0;//#DEBUG
        Search(_startDepth, -CHECKMATE, CHECKMATE);
        // Console.WriteLine($"{exploredNodes / timer.MillisecondsElapsedThisTurn}kN/s");

        return _bestMove;
    }

    int Search(int depth, int alpha, int beta)
    {
        exploredNodes++;//#DEBUG
        bool isQSearch = depth <= 0;
        bool isRoot = depth == _startDepth;
        bool isWhiteTurn = _board.IsWhiteToMove;
        bool isInCheck = _board.IsInCheck();
        int bestEvaluation = 2 * CHECKMATE * (isWhiteTurn ? -1 : 1);

        if (!isRoot && _board.IsRepeatedPosition())
            return 0;

        int evaluation;
        if (isQSearch)
        {
            // stand-pat
            bestEvaluation = Evaluate();
            if (!isWhiteTurn)
            {
                if (bestEvaluation >= beta)
                    return beta;
                alpha = Math.Max(alpha, bestEvaluation);
            }
            else
            {
                if (bestEvaluation <= alpha)
                    return alpha;
                beta = Math.Min(beta, bestEvaluation);
            }
        }

        Move[] moves = _board.GetLegalMoves(isQSearch);
        moves = moves.OrderByDescending(m =>
        {
            return m.IsCapture ? 1000 * (int)m.CapturePieceType - (int)m.MovePieceType : 0;
        }).ToArray();

        foreach (Move move in moves)
        {
            _board.MakeMove(move);
            evaluation = Search(depth - 1, alpha, beta);
            _board.UndoMove(move);

            if (_board.IsWhiteToMove)
            {
                if (evaluation > bestEvaluation)
                {
                    bestEvaluation = evaluation;
                    if (isRoot)
                        _bestMove = move;
                }
                alpha = Math.Max(alpha, evaluation);
            }
            else
            {
                if (evaluation < bestEvaluation)
                {
                    bestEvaluation = evaluation;
                    if (isRoot)
                        _bestMove = move;
                }
                beta = Math.Min(beta, evaluation);
            }
            if (beta <= alpha)
                break;
        }

        if (!isQSearch && moves.Length == 0) return isInCheck ? ((CHECKMATE - depth) * (isWhiteTurn ? -1 : 1)) : 0;

        return bestEvaluation;
    }

    // None    Pawn    Knight  Bishop  Rook    Queen   King
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

    int Evaluate()
    {
        int evaluation = 0;
        for (int i = 1; i < 6; i++)
        {
            var bb_white = _board.GetPieceBitboard((PieceType)i, true);
            var bb_black = _board.GetPieceBitboard((PieceType)i, false);
            evaluation += (BitboardHelper.GetNumberOfSetBits(bb_white) - BitboardHelper.GetNumberOfSetBits(bb_black)) * pieceValues[i];
        }
        return evaluation;
    }
}