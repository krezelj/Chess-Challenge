using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot : IChessBot
{
    //class MoveNode
    //{
    //    public Move move;
    //    public float evaluation;
    //    public List<MoveNode> nextMoves;
    //}

    //                      None    Pawn    Knight  Bishop  Rook    Queen   King
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

    float BasicEvaluation(Board board)
    {
        float evaluation = 0;
        PieceList[] pieceLists = board.GetAllPieceLists();
        for (int i = 0; i < 6; i++)
        {
            evaluation += pieceLists[i].Count * pieceValues[i + 1] - pieceLists[i + 6].Count * pieceValues[i + 1];
        }
        return evaluation;
    }

    public Move Think(Board board, Timer timer)
    {
        int depth = 4;
        int movesEvaluated = 0; // #DEBUG
        Stack<Move> moveStack = new Stack<Move>(board.GetLegalMoves());
        Stack<Move> moveHistoryStack = new Stack<Move>();
        Stack<int> sizeStack = new Stack<int>(new int[] { moveStack.Count });
        while (moveStack.Count > 0)
        {
            while (sizeStack.Peek() == 0)
            {
                depth++;
                sizeStack.Pop();
                board.UndoMove(moveHistoryStack.Pop());
            }

            movesEvaluated++; // #DEBUG
            Move currentMove = moveStack.Pop();
            sizeStack.Push(sizeStack.Pop() - 1); // decrement value of the last element
            if (depth == 0)
                //if depth 0, apply move, evaluate, undo move and push the value up
                continue;

            board.MakeMove(currentMove);
            moveHistoryStack.Push(currentMove);

            depth--;
            Move[] newMoves = board.GetLegalMoves();
            sizeStack.Push(newMoves.Length);
            foreach (var move in newMoves) 
                moveStack.Push(move);
        }
        while (moveHistoryStack.Count > 0)
        {
            board.UndoMove(moveHistoryStack.Pop());
        }
        Console.WriteLine(moveHistoryStack.Count.ToString());
        Console.WriteLine($"Moves Evaluated: {movesEvaluated}"); // #DEBUG
        Move[] moves = board.GetLegalMoves();
        return moves[0];

    }
}