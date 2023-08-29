using ChessChallenge.API;
using System;
using System.Linq;

public class CosmosV0_15 : IChessBot
{
#if DEBUG
    private ulong _exploredNodes;
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


    private Board _board;
    private Timer _timer;
    private int _timeLimit;

    private Move _bestMove;
    private Move[] _killerMoves = new Move[1024];
    private int[,,] _historyHeuristic;

    private readonly int[][] UnpackedPestoTables;

    public CosmosV0_15()
    {
        // Unpacking thanks to Tyrant
        // https://github.com/Tyrant7/Chess-Challenge/tree/main/Chess-Challenge
        UnpackedPestoTables = new[] {
            63746705523041458768562654720m,     71818693703096985528394040064m, 75532537544690978830456252672m, 
            75536154932036771593352371712m,     76774085526445040292133284352m, 3110608541636285947269332480m, 
            936945638387574698250991104m,       75531285965747665584902616832m, 77047302762000299964198997571m, 
            3730792265775293618620982364m,      3121489077029470166123295018m,  3747712412930601838683035969m, 
            3763381335243474116535455791m,      8067176012614548496052660822m,  4977175895537975520060507415m, 
            2475894077091727551177487608m,      2458978764687427073924784380m,  3718684080556872886692423941m,
            4959037324412353051075877138m,      3135972447545098299460234261m,  4371494653131335197311645996m,
            9624249097030609585804826662m,      9301461106541282841985626641m,  2793818196182115168911564530m,
            77683174186957799541255830262m,     4660418590176711545920359433m,  4971145620211324499469864196m, 
            5608211711321183125202150414m,      5617883191736004891949734160m,  7150801075091790966455611144m, 
            5619082524459738931006868492m,      649197923531967450704711664m,   75809334407291469990832437230m,
            78322691297526401047122740223m,     4348529951871323093202439165m,  4990460191572192980035045640m, 
            5597312470813537077508379404m,      4980755617409140165251173636m,  1890741055734852330174483975m, 
            76772801025035254361275759599m,     75502243563200070682362835182m, 78896921543467230670583692029m, 
            2489164206166677455700101373m,      4338830174078735659125311481m,  4960199192571758553533648130m, 
            3420013420025511569771334658m,      1557077491473974933188251927m,  77376040767919248347203368440m,
            73949978050619586491881614568m,     77043619187199676893167803647m, 1212557245150259869494540530m, 
            3081561358716686153294085872m,      3392217589357453836837847030m,  1219782446916489227407330320m, 
            78580145051212187267589731866m,     75798434925965430405537592305m, 68369566912511282590874449920m, 
            72396532057599326246617936384m,     75186737388538008131054524416m, 77027917484951889231108827392m, 
            73655004947793353634062267392m,     76417372019396591550492896512m, 74568981255592060493492515584m, 
            70529879645288096380279255040m,
        }.Select(packedTable =>
        {
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(bit => BitConverter.GetBytes(bit)
                    .Select(square => (int)((sbyte)square * 1.461) + PieceValues[_timeLimit++ % 12]))
                .ToArray();

        }).ToArray();
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
        for (int searchDepth = 2, alpha=-10_000, beta=10_000; ;)
        {
            int eval = Search(searchDepth, 0, alpha, beta, true);
            if (2 * timer.MillisecondsElapsedThisTurn > _timeLimit) // TODO add early break when checkmate found
                return _bestMove;
            // check if eval outside of the window
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
                canLateMoveReduce = false;
        Move currentBestMove = Move.NullMove;

        if (isNotRoot && _board.IsRepeatedPosition())
            return 0;

        ulong zKey = _board.ZobristKey;
        ref TTEntry TTMatch = ref TTArray[zKey & 0x3FFFFF];
        int TTEvaluation = TTMatch.evaluation,
            TTNodeType = TTMatch.nodeType,
            bestEvaluation = -20_000,
            startAlpha = alpha,
            movesExplored = 0,
            evaluation;

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
        else if (beta - alpha == 1 && !isInCheck)
        {
            // RMF
            evaluation = Evaluate();
            if (depth <= 6 && evaluation - 100 * depth > beta)
                return evaluation;

            // FP
            canFutilityPrune = depth <= 2 && evaluation + 150 * depth < alpha;

            // LMR
            canLateMoveReduce = true;

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
        

        Move[] moves = _board.GetLegalMoves(isQSearch && !isInCheck);
        moves = moves.OrderByDescending(m =>
            TTMove == m ? 1_000_000 :
            m.IsCapture ? 100_000 * (int)m.CapturePieceType - (int)m.MovePieceType :
            m.IsPromotion ? 91_000 :    // questionable elo gain
            _killerMoves[plyFromRoot] == m ? 90_000 :
            _historyHeuristic[plyFromRoot & 1, (int)m.MovePieceType, m.TargetSquare.Index]
        ).ToArray();

        if (!isQSearch && moves.Length == 0) return isInCheck ? -10_000 + plyFromRoot : 0;

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            bool isQuiet = !(move.IsPromotion || move.IsCapture);

            if (canFutilityPrune && movesExplored > 0 && isQuiet)
                continue;

            _board.MakeMove(move);
            // if isQSearch => full search
            // else if movesExplored == 0 => PV Node => full search
            // else perform LMR or Null Window Search
            //      for both use Null Window, for LMR use reduction of 4
            //      if evaluation > alpha => full search
            if (isQSearch || movesExplored++ == 0 ||
                MiniSearch(alpha + 1, (movesExplored >= 5 && depth >= 2 && canLateMoveReduce && isQuiet) ? 4 : 1) > alpha)
                MiniSearch(beta);

            _board.UndoMove(move);

            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                currentBestMove = move;
                if (!isNotRoot)
                    _bestMove = move;

                alpha = Math.Max(alpha, evaluation);
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
    private readonly short[] PieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
                                             94, 281, 297, 512, 936, 0 }; // Endgame

    private int Evaluate()
    {
        int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2;
        for (; --sideToMove >= 0;)
        {
            for (int piece = -1, square; ++piece < 6;)
                for (ulong mask = _board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                {
                    // Gamephase, middlegame -> endgame
                    gamephase += GamePhaseIncrement[piece];

                    // Material and square evaluation
                    square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                    middlegame += UnpackedPestoTables[square][piece];
                    endgame += UnpackedPestoTables[square][piece + 6];
                }

            middlegame = -middlegame;
            endgame = -endgame;
        }
        return (middlegame * gamephase + endgame * (24 - gamephase)) / 24 * (_board.IsWhiteToMove ? 1 : -1);
    }

    #endregion
}