using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
#if DEBUG
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
    private int[] moveScores = new int[218];

    // private readonly int[][] UnpackedPestoTables;
    private readonly ulong[] components;
    private readonly int[] weights;

    public MyBot()
    {
        var data = new ulong[] {
            0x9f2f2000000000d6,0xad88018280898ded,0xffffffffffffffff,0xb5d5e50101001008,
            0x183c7afef8b82800,0xeb10080000406001,0xa4fb550200000000,0x3b030106bd761839,
            0x6b0000000004f4f9,0xb7b19101418011b5,0xffffffffffffffff,0x1008008080a7abad,
            0x141d1f7f5e3c18,0x80060200001008d7,0x40aadf25,0x9c186ebd6080c0dc,
            0x915100b0320021b,0x20901180b5f0008,0x60b090d06690206,0x811100f0f7a0510,
            0x80c0a0c0fff0b0a,0x40e0a0403230810,0x71e0a0911230a13,0xd0c050d084c0503,
            0x90b090c09510709,0xb0b0a0a0b860a0a,0x90e0d120ceb050a,0x910080e09230505
        };
        var smallWeights = new byte[96];
        Buffer.BlockCopy(data, 128, smallWeights, 0, 96);
        components = data.Take(16).ToArray();
        weights = smallWeights.Select(x => (int)(x * 4.11328125 - 40)).ToArray();
    }

    public Move Think(Board board, Timer timer)
    {        
        _board = board;
        _timer = timer;
        _historyHeuristic = new int[2, 7, 64]; // side to move, piece (0 is null), target square
#if DEBUG
        _exploredNodes = 0;
        Console.WriteLine($"\nStats for Ply: {board.PlyCount}");
#endif

        _timeLimit = timer.MillisecondsRemaining / 30; // TODO Add incerementTime/30 to the limit

        for (int searchDepth = 1, alpha=-10_000, beta=10_000; ;)
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
#if DEBUG
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
#if DEBUG
        ++_exploredNodes;
#endif


        bool    isNotRoot = plyFromRoot > 0, 
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
            if (isQSearch || movesExplored++ == 0 ||
                MiniSearch(alpha + 1, (movesExplored >= 7 && depth >= 2 && canLMR && isQuiet) ? 4 : 1) > alpha)
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
                //if (depth == 1 && isQuiet && evaluation + 130 < bestEvaluation)
                //    break;
                if (alpha >= beta)
                {
                    if (isQuiet)
                    {
                        _killerMoves[plyFromRoot] = move;
                        _historyHeuristic[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index] +=  depth * depth;
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
    private readonly int[] GamePhaseIncrement = { 0, 1, 1, 2, 4, 0 };

    // None, Pawn, Knight, Bishop, Rook, Queen, King 
    //private readonly short[] PieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
    //                                         94, 281, 297, 512, 936, 0 }; // Endgame

    public int Evaluate()
    {
        int mg = 0, eg = 0, gamephase = 0, side = 0, weightIdx;
        for (; side++ < 2;)
        {
            weightIdx = 0;
            for (int p = 0; p++ < 6;)
            {
                ulong pieceMask = _board.GetPieceBitboard((PieceType)p + 1, side == 0);
                gamephase += GamePhaseIncrement[p] * BitboardHelper.GetNumberOfSetBits(pieceMask); ;
                for (int c = 0; c++ < 8;)
                {
                    int n = BitboardHelper.GetNumberOfSetBits(components[c + side * 8] & pieceMask),
                        w_mg = weights[weightIdx],
                        w_eg = weights[weightIdx++ + 48];
                    mg += n * w_mg;
                    eg += n * w_eg;
                }
            }
            mg = -mg;
            eg = -eg;
        }
        return (mg * gamephase + eg * (24 - gamephase)) / 24 * (_board.IsWhiteToMove ? 1 : -1);
    }

    #endregion
}