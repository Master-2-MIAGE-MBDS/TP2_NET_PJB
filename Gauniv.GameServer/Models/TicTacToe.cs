using MessagePack;

namespace Gauniv.GameServer.Models;

/// <summary>
/// Symbole du morpion
/// </summary>
public enum CellState
{
    Empty = 0,
    X = 1,
    O = 2
}

/// <summary>
/// État d'une partie de morpion
/// </summary>
public enum GameStatus
{
    WaitingForPlayers = 0,
    InProgress = 1,
    XWins = 2,
    OWins = 3,
    Draw = 4,
    Cancelled = 5
}

/// <summary>
/// Plateau de jeu du morpion (3x3)
/// </summary>
[MessagePackObject]
public class TicTacToeBoard
{
    [Key(0)]
    public CellState[] Cells { get; set; } = new CellState[9];
    
    [Key(1)]
    public CellState CurrentPlayer { get; set; } = CellState.X;
    
    [Key(2)]
    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayers;
    
    [Key(3)]
    public string? WinnerId { get; set; }
    
    [Key(4)]
    public int[] WinningLine { get; set; } = Array.Empty<int>();
    
    public TicTacToeBoard()
    {
        Reset();
    }
    
    /// <summary>
    /// Réinitialise le plateau
    /// </summary>
    public void Reset()
    {
        Cells = new CellState[9];
        CurrentPlayer = CellState.X;
        Status = GameStatus.InProgress;
        WinnerId = null;
        WinningLine = Array.Empty<int>();
    }
    
    /// <summary>
    /// Joue un coup sur le plateau
    /// </summary>
    public bool MakeMove(int position, CellState player)
    {
        if (position < 0 || position > 8)
            return false;
            
        if (Cells[position] != CellState.Empty)
            return false;
            
        if (Status != GameStatus.InProgress)
            return false;
            
        if (player != CurrentPlayer)
            return false;
            
        Cells[position] = player;
        return true;
    }
    
    /// <summary>
    /// Vérifie s'il y a un gagnant
    /// </summary>
    public bool CheckWinner(out CellState winner, out int[] winningLine)
    {
        winner = CellState.Empty;
        winningLine = Array.Empty<int>();
        
        // Lignes horizontales
        for (int row = 0; row < 3; row++)
        {
            int idx = row * 3;
            if (Cells[idx] != CellState.Empty && 
                Cells[idx] == Cells[idx + 1] && 
                Cells[idx] == Cells[idx + 2])
            {
                winner = Cells[idx];
                winningLine = new[] { idx, idx + 1, idx + 2 };
                return true;
            }
        }
        
        // Lignes verticales
        for (int col = 0; col < 3; col++)
        {
            if (Cells[col] != CellState.Empty && 
                Cells[col] == Cells[col + 3] && 
                Cells[col] == Cells[col + 6])
            {
                winner = Cells[col];
                winningLine = new[] { col, col + 3, col + 6 };
                return true;
            }
        }
        
        // Diagonale principale
        if (Cells[0] != CellState.Empty && 
            Cells[0] == Cells[4] && 
            Cells[0] == Cells[8])
        {
            winner = Cells[0];
            winningLine = new[] { 0, 4, 8 };
            return true;
        }
        
        // Diagonale secondaire
        if (Cells[2] != CellState.Empty && 
            Cells[2] == Cells[4] && 
            Cells[2] == Cells[6])
        {
            winner = Cells[2];
            winningLine = new[] { 2, 4, 6 };
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Vérifie si le plateau est plein (match nul)
    /// </summary>
    public bool IsFull()
    {
        return Cells.All(c => c != CellState.Empty);
    }
    
    /// <summary>
    /// Change le joueur actuel
    /// </summary>
    public void SwitchPlayer()
    {
        CurrentPlayer = CurrentPlayer == CellState.X ? CellState.O : CellState.X;
    }
}

/// <summary>
/// Données d'un coup joué
/// </summary>
[MessagePackObject]
public class TicTacToeMove
{
    [Key(0)]
    public int Position { get; set; }
    
    [Key(1)]
    public string PlayerId { get; set; } = string.Empty;
}

/// <summary>
/// État complet d'une partie de morpion
/// </summary>
[MessagePackObject]
public class TicTacToeGameState
{
    [Key(0)]
    public string GameId { get; set; } = string.Empty;
    
    [Key(1)]
    public CellState[] Board { get; set; } = new CellState[9];
    
    [Key(2)]
    public GameStatus Status { get; set; }
    
    [Key(3)]
    public CellState CurrentPlayer { get; set; }
    
    [Key(4)]
    public string? PlayerXId { get; set; }
    
    [Key(5)]
    public string? PlayerXName { get; set; }
    
    [Key(6)]
    public string? PlayerOId { get; set; }
    
    [Key(7)]
    public string? PlayerOName { get; set; }
    
    [Key(8)]
    public string? WinnerId { get; set; }
    
    [Key(9)]
    public int[] WinningLine { get; set; } = Array.Empty<int>();
    
    [Key(10)]
    public int PlayerXScore { get; set; }
    
    [Key(11)]
    public int PlayerOScore { get; set; }
}

/// <summary>
/// Résultat d'un coup
/// </summary>
[MessagePackObject]
public class MoveResult
{
    [Key(0)]
    public bool Success { get; set; }
    
    [Key(1)]
    public string? ErrorMessage { get; set; }
    
    [Key(2)]
    public int Position { get; set; }
    
    [Key(3)]
    public CellState Player { get; set; }
}
