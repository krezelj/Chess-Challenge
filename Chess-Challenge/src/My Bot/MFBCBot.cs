#define STATS

using ChessChallenge.API;
using System;
using System.Linq;
public class MFBCBot : IChessBot
{
#if STATS
    private int _exploredNodes;
#endif


    private record struct TTEntry
    (
        ulong zKey,
        Move move,
        int evaluation,
        int depth,
        int nodeType
    );

    private TTEntry[] TTArray = new TTEntry[0x400000];

    // TT as tuple, loses elo but gains some tokens, might look into it later
    // zKey, Move, eval, depth, flag
    //private readonly (ulong, Move, int, int, int)[] TTArray = new (ulong, Move, int, int, int)[0x400000];

    private Board _board;
    private Timer _timer;
    private int _timeLimit;

    private Move _bestMove;
    private Move[] _killerMoves = new Move[1024];
    private int[,,] _historyHeuristic;
    private int[] moveScores = new int[218],
                    GamePhaseIncrement = { 0, 1, 1, 2, 4, 0 },
                    weights = Enumerable.Range(0, 96).Select(x => Buffer.GetByte(new ulong[]
                    {
                        0xb070306031208,0x192f0c0e02050806,0xa4c040403040007,0x36d0a0309000603,
                        0x9f5050007010500,0x400502000a0301,0x18190000010d00,0x537000600000007,
                        0x344010301000002,0x17f020001000200,0x7de050605000305,0x93e060503000105,
                    }, x) * 4).ToArray();

    private readonly ulong[] components = new ulong[] {
        0x44de3cfe7e7f4eac,0x7ef408000000002f,0x63cc664640c2e7eb,0xff4e061808020610,
        0x3838de7fbf3c324f,0x1fff70000000040,0xffffffffffffffff,0xa277ffffffffd10,
        0xac4e7f7efe3cde44,0x2f0000000008f47e,0xebe7c2404666cc63,0x1006020818064eff,
        0x4f323cbf7fde3838,0x4000000000f7ff01,0xffffffffffffffff,0x10fdffffff7f270a,
    };

    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _timer = timer;
        _historyHeuristic = new int[2, 7, 64]; // side to move, piece (0 is null), target square
#if STATS
        _exploredNodes = 0;
        Console.WriteLine($"\nStats for Ply: {board.PlyCount}");
#endif

        _timeLimit = timer.MillisecondsRemaining / 30; // TODO Add incerementTime/30 to the limit

        for (int searchDepth = 1, alpha = -10_000, beta = 10_000; ;)
        {
            int eval = Search(searchDepth, 0, alpha, beta, true);
            // TODO add early break when checkmate found
            if (2 * timer.MillisecondsElapsedThisTurn > _timeLimit)
                return _bestMove;
            if (eval < alpha)
                alpha -= 82;
            else if (eval > beta)
                beta += 82;
            else
            {
                alpha = eval - 41;
                beta = eval + 41;
                searchDepth++;
            }
#if STATS
            string printoutEval = eval.ToString(); ;
            if (Math.Abs(eval) > 5_000)
            {
                printoutEval = $"{(eval < 0 ? "-" : "")}M{Math.Ceiling((10_000 - Math.Abs((double)eval)) / 2)}";
            }
            Console.WriteLine("Stats: Depth: {0,-2} | Evaluation: {1,-4} | Nodes: {2, -8} | Time: {3,-5}ms" +
                "({4, 5}kN/s) | Best Move: {5}{6}",
                searchDepth,
                printoutEval,
                _exploredNodes,
                _timer.MillisecondsElapsedThisTurn,
                _exploredNodes / (_timer.MillisecondsElapsedThisTurn > 0 ? _timer.MillisecondsElapsedThisTurn : 1),
                _bestMove.StartSquare.Name, _bestMove.TargetSquare.Name);
#endif

        }
    }

    int Search(int depth, int plyFromRoot, int alpha, int beta, bool canNMP)
    {
#if STATS
        ++_exploredNodes;
#endif


        bool isNotRoot = plyFromRoot > 0,
                isInCheck = _board.IsInCheck(),
                canFutilityPrune = false,
                canLMR = beta - alpha == 1 && !isInCheck;
        Move currentBestMove = default;

        if (isNotRoot && _board.IsRepeatedPosition())
            return 0;

        ulong zKey = _board.ZobristKey;
        ref TTEntry TTMatch = ref TTArray[zKey & 0x3FFFFF];
        int TTEvaluation = TTMatch.evaluation,
            TTNodeType = TTMatch.nodeType,
            bestEvaluation = -20_000,
            startAlpha = alpha,
            movesExplored = 0,
            evaluation,
            movesScored = 0;

        Move TTMove = TTMatch.move;
        if (TTMatch.zKey == zKey &&
            isNotRoot &&
            TTMatch.depth >= depth &&
            (
                TTNodeType == 1 ||
                (TTNodeType == 0 && TTEvaluation <= alpha) ||
                (TTNodeType == 2 && TTEvaluation >= beta))
            )
            return TTEvaluation;


        if (isInCheck)
            depth++;
        bool isQSearch = depth <= 0;

        int MiniSearch(
            int newAlpha,
            int reduction = 1,
            bool canNullMovePrune = true) =>
            evaluation = -Search(depth - reduction, plyFromRoot + 1, -newAlpha, -alpha, canNullMovePrune);

        if (isQSearch)
        {
            bestEvaluation = Evaluate();
            if (bestEvaluation >= beta)
                return beta;
            alpha = Math.Max(alpha, bestEvaluation);
        }
        else if (canLMR) // Token save, the condition for LMR is !isPv and !isInCheck
        {
            // RMF
            evaluation = Evaluate();
            if (depth <= 6 && evaluation - 100 * depth > beta)
                return evaluation;

            // FP
            canFutilityPrune = depth <= 2 && evaluation + 150 * depth < alpha;

            // Pawn Endgame Detection
            //ulong nonPawnPieces = 0;
            //for (int i = 1; ++i < 6;) 
            //    nonPawnPieces |= _board.GetPieceBitboard((PieceType)i, true) | _board.GetPieceBitboard((PieceType)i, false);

            // NMP
            if (depth >= 2 && canNMP)
            {
                _board.ForceSkipTurn();
                MiniSearch(beta, 2 + depth / 2, false);
                _board.UndoSkipTurn();
                if (evaluation >= beta)
                    return evaluation;
            }
        }

        Span<Move> moves = stackalloc Move[218];
        _board.GetLegalMovesNonAlloc(ref moves, isQSearch && !isInCheck);

        foreach (Move move in moves)
            moveScores[movesScored++] = -(
            move == TTMove ? 10_000_000 :
            move.IsCapture ? 1_000_000 * (int)move.CapturePieceType - (int)move.MovePieceType :
            _killerMoves[plyFromRoot] == move ? 900_000 :
            _historyHeuristic[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index]);

        moveScores.AsSpan(0, moves.Length).Sort(moves);

        if (!isQSearch && moves.Length == 0) return isInCheck ? -10_000 + plyFromRoot : 0;

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            bool isQuiet = !(move.IsCapture || move.IsPromotion);

            //if (canLMR && movesExplored >= depth * depth && isQuiet)
            //    continue;

            if (canFutilityPrune && movesExplored > 0 && isQuiet)
                continue;

            _board.MakeMove(move);
            // if isQSearch => full search
            // else if movesExplored == 0 => PV Node => full search
            // else perform LMR or Null Window Search
            //      for both use Null Window, for LMR use reduction of 4
            //      if evaluation > alpha => full search
            //if (isQSearch || movesExplored++ == 0 ||
            //    MiniSearch(alpha + 1, (movesExplored >= 7 && depth >= 2 && canLMR && isQuiet) ? 4 : 1) > alpha)
            //    MiniSearch(beta);
            if (isQSearch ||
                movesExplored++ == 0 ||
                (movesExplored >= 7 && depth >= 2 && canLMR && isQuiet && MiniSearch(alpha + 1, 4) > alpha) ||
                MiniSearch(alpha + 1) > alpha)
                MiniSearch(beta);

            _board.UndoMove(move);

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                currentBestMove = move;
                if (!isNotRoot)
                    _bestMove = move;

                alpha = Math.Max(alpha, evaluation);

                // SPP
                //if (depth == 1 && isQuiet && evaluation + 80 < bestEvaluation)
                //    break;
                if (alpha >= beta)
                {
                    if (isQuiet)
                    {
                        _killerMoves[plyFromRoot] = move;
                        _historyHeuristic[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                    }
                    break;
                }

            }

            if (_timer.MillisecondsElapsedThisTurn > _timeLimit)
                return 20_000;
        }

        TTMatch = new(
            zKey,
            currentBestMove,
            bestEvaluation,
            depth,
            bestEvaluation >= beta ? 2 : bestEvaluation <= startAlpha ? 0 : 1);

        return bestEvaluation;
    }

    #region EVALUATION

    //public int Evaluate()
    //{
    //    int mg = 0, eg = 0, gamephase = 0, weightIdx;
    //    for (int side = 0; side < 2; side++)
    //    {
    //        weightIdx = 0;
    //        for (int p = 0; p < 6; p++)
    //        {
    //            ulong pieceMask = _board.GetPieceBitboard((PieceType)(p + 1), side == 0);
    //            gamephase += GamePhaseIncrement[p] * BitboardHelper.GetNumberOfSetBits(pieceMask); ;
    //            for (int c = 0; c < 8; c++)
    //            {
    //                int n = BitboardHelper.GetNumberOfSetBits(components[c + side * 8] & pieceMask);
    //                mg += n * weights[weightIdx];
    //                eg += n * weights[weightIdx++ + 48];
    //            }
    //        }
    //        mg = -mg;
    //        eg = -eg;
    //    }
    //    return (mg * gamephase + eg * (24 - gamephase)) / 24 * (_board.IsWhiteToMove ? 1 : -1);
    //}

    public int Evaluate()
    {
        int mg = 0, eg = 0, gamephase = 0, side = -1;
        for (; ++side < 2;)
        {
            for (int weightIdx = 0; weightIdx < 48;)
            {
                ulong pieceMask = _board.GetPieceBitboard((PieceType)(weightIdx / 8 + 1), side == 0);
                int c = weightIdx % 8;
                if (c == 0) // TODO remove this condition and scale the gamephase by C == the number of components e.g. C=8 24-->184
                    gamephase += GamePhaseIncrement[weightIdx / 8] * BitboardHelper.GetNumberOfSetBits(pieceMask);
                int n = BitboardHelper.GetNumberOfSetBits(components[c + side * 8] & pieceMask);
                mg += n * weights[weightIdx];
                eg += n * weights[weightIdx++ + 48];
            }
            mg = -mg;
            eg = -eg;
        }

        return (mg * gamephase + eg * (24 - gamephase)) / 24 * (_board.IsWhiteToMove ? 1 : -1);
    }

    #endregion
}


