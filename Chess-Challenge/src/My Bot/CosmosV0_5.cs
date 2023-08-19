using ChessChallenge.API;
using System;
using System.Linq;

public class CosmosV0_5 : IChessBot
{
    #region DEBUG_VARIABLES

    private int exploredNodes;//#DEBUG;
    private int ttHits;

    #endregion

    #region CONSTANTS

    private readonly int CHECKMATE = 10_000;

    #endregion

    private record struct TTEntry
    (
        ulong zKey,
        Move move,
        short evaluation,
        sbyte depth,
        byte nodeType   // 0 - pv-node, 1 - all-node, 2 - cut-node
    );

    private ulong TTMask = 0x3FFFFF;
    private TTEntry[] TTArray;


    private Board _board;
    private Timer _timer;

    private Move _bestMove;
    private int _searchDepth;
    private int _timeLimit = 100;

    public CosmosV0_5()
    {
        TTArray = new TTEntry[TTMask + 1];
    }

    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _timer = timer;
        _searchDepth = 1;

        exploredNodes = 0;//#DEBUG
        ttHits = 0;//#DEBUG

        Move lastBestMove = _bestMove;
        int eval = 0; //#DEBUG
        int lastEval = 0;//#DEBUG
        while (true)
        {
            eval = Search(++_searchDepth, 0, -CHECKMATE, CHECKMATE);
            if (timer.MillisecondsElapsedThisTurn > _timeLimit)
                break;
            // TODO Reorder, this is assuming that we are able to do at least to searches i.e. we do not break after first search
            lastBestMove = _bestMove;
            lastEval = eval;
            if (eval > CHECKMATE / 2 && eval < 2 * CHECKMATE)
                break;
        }
        //Console.WriteLine("Stats:\n\tDepth Reached: {0}\n\tBest Move: {1}\n\tEvaluation: {2}\n\tNodes: {3}({4}kN/s)",
        //    _searchDepth - 1,
        //    lastBestMove,
        //    lastEval,
        //    exploredNodes, exploredNodes / (_timer.MillisecondsElapsedThisTurn > 0 ? _timer.MillisecondsElapsedThisTurn : 1));//#DEBUG
        return lastBestMove;
    }

    int Search(int depth, int plyCount, int alpha, int beta)
    {
        ++exploredNodes;//#DEBUG

        bool isQSearch = depth <= 0;
        bool isRoot = plyCount == 0;
        bool isInCheck = _board.IsInCheck();
        int bestEvaluation = -2 * CHECKMATE;
        Move currentBestMove = Move.NullMove;

        if (!isRoot && _board.IsRepeatedPosition())
            return 0;

        ulong zKey = _board.ZobristKey;
        TTEntry TTMatch = TTArray[zKey & TTMask];

        if (TTMatch.zKey == zKey &&
            !isRoot &&
            TTMatch.depth >= depth &&
            (
                TTMatch.nodeType == 1 ||
                (TTMatch.nodeType == 0 && TTMatch.evaluation <= alpha) ||
                (TTMatch.nodeType == 2 && TTMatch.evaluation >= beta))
            )
            return TTMatch.evaluation;


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
            TTMatch.move == m ? 100_000 :
            m.IsCapture ? 1000 * (int)m.CapturePieceType - (int)m.MovePieceType : 0
        ).ToArray();

        int startAlpha = alpha;
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            _board.MakeMove(move);

            // evaluation = -PVS(depth - 1, plyCount + 1, -beta, -alpha);
            bool fullSearch = isQSearch || i == 0;
            evaluation = -Search(depth - 1, plyCount + 1, fullSearch ? -beta : -alpha - 1, -alpha);
            if (!fullSearch && evaluation > alpha)
                evaluation = -Search(depth - 1, plyCount + 1, -beta, -alpha);
            _board.UndoMove(move);

            //if (evaluation >= beta)
            //    return beta;
            //if (evaluation > alpha)
            //    alpha = evaluation;

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                currentBestMove = move;
                if (isRoot)
                    _bestMove = move;


                alpha = Math.Max(alpha, evaluation);
                if (alpha >= beta)
                    break;
            }

            if (_timer.MillisecondsElapsedThisTurn > _timeLimit)
                return 2 * CHECKMATE;
        }

        if (!isQSearch && moves.Length == 0) return isInCheck ? -CHECKMATE + plyCount : 0;

        TTArray[zKey & TTMask] = new TTEntry(zKey, currentBestMove, (short)bestEvaluation, (sbyte)depth,
            (byte)(bestEvaluation >= beta ? 2 : bestEvaluation <= startAlpha ? 0 : 1));

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