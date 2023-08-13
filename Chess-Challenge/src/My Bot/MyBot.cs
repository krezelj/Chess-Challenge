using ChessChallenge.API;
using System;
using System.Linq;
using static System.Formats.Asn1.AsnWriter;

public class MyBot : IChessBot
{
    #region DEBUG_VARIABLES

    private int exploredNodes;//#DEBUG;

    #endregion

    #region CONSTANTS

    private readonly int CHECKMATE = 100_000;

    #endregion

    private Board _board;
    private Timer _timer;

    private Move _bestMove;
    private int _searchDepth;
    private int _timeLimit = 200;

    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _timer = timer;
        _searchDepth = 1;

        exploredNodes = 0;//#DEBUG

        Move lastBestMove;
        int eval = 0; //#DEBUG
        int lastEval;//#DEBUG
        while(true)
        {
            // Console.WriteLine($"Searching at depth: {++_searchDepth}");
            lastBestMove = _bestMove;
            lastEval = eval;
            eval = PVS(++_searchDepth, 0, -CHECKMATE, CHECKMATE);
            if (timer.MillisecondsElapsedThisTurn > _timeLimit)
                break;
        }
        Console.WriteLine("Stats:");
        Console.WriteLine($"\tDepth Reached: {_searchDepth}");
        Console.WriteLine($"\tEval: {(eval == 2 * CHECKMATE ? lastEval : eval)}");
        Console.WriteLine($"\tNodes: {exploredNodes} ({exploredNodes / _timer.MillisecondsElapsedThisTurn}kN/s)");
        return lastBestMove;
    }

    

    int PVS(int depth, int plyCount, int alpha, int beta)
    {
        ++exploredNodes;//#DEBUG
        bool isQSearch = depth <= 0;
        bool isRoot = plyCount == 0;
        bool isInCheck = _board.IsInCheck();
        int bestEvaluation = -2 * CHECKMATE;
        //Move currentBestMove = Move.NullMove;

        if (!isRoot && _board.IsRepeatedPosition())
            return 0;

        int evaluation;
        if (isQSearch)
        {
            bestEvaluation = Evaluate();
            if (bestEvaluation >= beta)
                return beta;
            alpha = Math.Max(alpha, bestEvaluation);
        }

        Move[] moves = _board.GetLegalMoves(isQSearch && !isInCheck);
        moves = moves.OrderByDescending(m =>
        {
            return m.IsCapture ? 1000 * (int)m.CapturePieceType - (int)m.MovePieceType : 0;
        }).ToArray();

        for (int i = 0; i < moves.Length; i++)
        {
            if (_timer.MillisecondsElapsedThisTurn > _timeLimit)
                return 2 * CHECKMATE;
                

            Move move = moves[i];
            _board.MakeMove(move);

            evaluation = -PVS(depth - 1, plyCount + 1, -beta, -alpha);
            //bool fullSearch = isQSearch || i == 0;
            //evaluation = -PVS(depth - 1, fullSearch ? -beta : -alpha - 1, -alpha);
            //if (!fullSearch && evaluation > alpha)
            //    evaluation = -PVS(depth - 1, - beta, -alpha);
            _board.UndoMove(move);

            //if (evaluation >= beta)
            //    return beta;
            //if (evaluation > alpha)
            //    alpha = evaluation;

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                //currentBestMove = move;
                if (isRoot)
                    _bestMove = move;
                    
                    
                alpha = Math.Max(alpha, evaluation);
                if (alpha >= beta)
                    break;
            }
        }

        if (!isQSearch&& moves.Length == 0) return isInCheck ? -CHECKMATE + plyCount : 0;

        return bestEvaluation;
    }

    // None    Pawn    Knight  Bishop  Rook    Queen   King
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

    Random rng = new Random();

    int Evaluate()
    {
        int evaluation = 0;
        for (int i = 1; i < 6; i++)
        {
            var bb_white = _board.GetPieceBitboard((PieceType)i, true);
            var bb_black = _board.GetPieceBitboard((PieceType)i, false);
            evaluation += (BitboardHelper.GetNumberOfSetBits(bb_white) - BitboardHelper.GetNumberOfSetBits(bb_black)) * pieceValues[i];
        }
        evaluation += rng.Next(-10, 10);
        return evaluation * (_board.IsWhiteToMove ? 1 : -1);
    }
}