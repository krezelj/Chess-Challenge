using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Intrinsics.X86;
using ChessChallenge.API;

public class Stockfish1000 : IChessBot {
    private Process stockfishProcess;
    private StreamWriter Ins() => stockfishProcess.StandardInput;
    private StreamReader Outs() => stockfishProcess.StandardOutput;

    private const int ELO_LEVEL = 1000;
    public Stockfish1000() {
        var stockfishExe = "C:\\MyRoot\\Chess\\stockfish\\stockfish-windows-x86-64-avx2.exe";
        if (stockfishExe == null) {
            throw new Exception("Missing environment variable: 'STOCKFISH_EXE'");
        }

        stockfishProcess = new();
        stockfishProcess.StartInfo.RedirectStandardOutput = true;
        stockfishProcess.StartInfo.RedirectStandardInput = true;
        stockfishProcess.StartInfo.FileName = stockfishExe;
        stockfishProcess.Start();

        Ins().WriteLine("uci");
        string? line;
        var isOk = false;

        while ((line = Outs().ReadLine()) != null) {
            if (line == "uciok") {
                isOk = true;
                break;
            }
        }

        if (!isOk) {
            throw new Exception("Failed to communicate with stockfish");
        }

        Ins().WriteLine("setoption name UCI_LimitStrength value true");
        Ins().WriteLine($"setoption name UCI_Elo value {ELO_LEVEL}");
    }

    public Move Think(Board board, Timer timer) {
        Ins().WriteLine("ucinewgame");
        Ins().WriteLine($"position fen {board.GetFenString()}");
        var timeString = board.IsWhiteToMove ? "wtime" : "btime";
        Ins().WriteLine($"go {timeString} {timer.MillisecondsRemaining}");

        string? line;
        Move? move = null;

        while ((line = Outs().ReadLine()) != null) {
            if (line.StartsWith("bestmove")) {
                var moveStr = line.Split()[1];
                move = new Move(moveStr, board);
                
                break;
            }
        }

        if (move == null) {
            throw new Exception("Engine crashed");
        }

        return (Move)move;
    }
}