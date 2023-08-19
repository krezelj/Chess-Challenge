using ChessChallenge.API;
using System;
using System.Linq;

public class CosmosV0_6 : IChessBot
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
        int evaluation,
        int depth,
        int nodeType   // 0 - pv-node, 1 - all-node, 2 - cut-node
    );

    private ulong TTMask = 0x3FFFFF;
    private TTEntry[] TTArray;


    private Board _board;
    private Timer _timer;

    private Move _bestMove;
    private int _searchDepth;
    private int _timeLimit = 100;

    public CosmosV0_6()
    {
        TTArray = new TTEntry[TTMask + 1];
        UnpackedPestoTables = PackedPestoTables.Select(packedTable =>
        {
            int pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(bit => BitConverter.GetBytes(bit)
                    .Select(square => (int)((sbyte)square * 1.461) + PieceValues[pieceType++]))
                .ToArray();

        }).ToArray();
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
            if (eval > CHECKMATE / 2)
                break;
        }
        Console.WriteLine("Stats:\n\tDepth Reached: {0}\n\tBest Move: {1}\n\tEvaluation: {2}\n\tNodes: {3}({4}kN/s)",
            _searchDepth - 1,
            lastBestMove,
            lastEval,
            exploredNodes, exploredNodes / (_timer.MillisecondsElapsedThisTurn > 0 ? _timer.MillisecondsElapsedThisTurn : 1));//#DEBUG
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

        TTArray[zKey & TTMask] = new TTEntry(zKey, currentBestMove, bestEvaluation, depth,
            bestEvaluation >= beta ? 2 : bestEvaluation <= startAlpha ? 0 : 1);

        return bestEvaluation;
    }

    //// None    Pawn    Knight  Bishop  Rook    Queen   King
    //int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

    //// Random rng = new Random();

    //int Evaluate()
    //{
    //    int evaluation = 0;
    //    for (int i = 1; i < 6; i++)
    //    {
    //        var bb_white = _board.GetPieceBitboard((PieceType)i, true);
    //        var bb_black = _board.GetPieceBitboard((PieceType)i, false);
    //        evaluation += (BitboardHelper.GetNumberOfSetBits(bb_white) - BitboardHelper.GetNumberOfSetBits(bb_black)) * pieceValues[i];
    //    }
    //    evaluation += (int)(_board.ZobristKey & 0b111) - 3;
    //    // evaluation += rng.Next(-10, 10);
    //    return evaluation * (_board.IsWhiteToMove ? 1 : -1);
    //}

    #region EVALUATION
    private readonly int[] GamePhaseIncrement = { 0, 1, 1, 2, 4, 0 };

    // None, Pawn, Knight, Bishop, Rook, Queen, King 
    private readonly short[] PieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
                                             94, 281, 297, 512, 936, 0 }; // Endgame

    private readonly decimal[] PackedPestoTables = {
        63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
        77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
        2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
        77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
        75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
        75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
        73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
        68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
    };

    private readonly int[][] UnpackedPestoTables;

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