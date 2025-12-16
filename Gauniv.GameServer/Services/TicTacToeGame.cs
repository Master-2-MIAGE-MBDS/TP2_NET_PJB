using Gauniv.GameServer.Models;

namespace Gauniv.GameServer.Services;

/// <summary>
/// Service de gestion d'une partie de morpion
/// </summary>
public class TicTacToeGame
{
    public string GameId { get; }
    public TicTacToeBoard Board { get; }
    
    public string? PlayerXId { get; set; }
    public string? PlayerXName { get; set; }
    public string? PlayerOId { get; set; }
    public string? PlayerOName { get; set; }
    
    public int PlayerXScore { get; private set; }
    public int PlayerOScore { get; private set; }
    
    public TicTacToeGame(string gameId)
    {
        GameId = gameId;
        Board = new TicTacToeBoard();
    }
    
    /// <summary>
    /// Assigne un joueur à X ou O
    /// </summary>
    public bool AssignPlayer(string playerId, string playerName)
    {
        if (PlayerXId == null)
        {
            PlayerXId = playerId;
            PlayerXName = playerName;
            return true;
        }
        else if (PlayerOId == null)
        {
            PlayerOId = playerId;
            PlayerOName = playerName;
            return true;
        }
        return false; // Partie pleine
    }
    
    /// <summary>
    /// Obtient le symbole d'un joueur
    /// </summary>
    public CellState? GetPlayerSymbol(string playerId)
    {
        if (playerId == PlayerXId) return CellState.X;
        if (playerId == PlayerOId) return CellState.O;
        return null;
    }
    
    /// <summary>
    /// Vérifie si c'est le tour du joueur
    /// </summary>
    public bool IsPlayerTurn(string playerId)
    {
        var symbol = GetPlayerSymbol(playerId);
        return symbol.HasValue && symbol.Value == Board.CurrentPlayer;
    }
    
    /// <summary>
    /// Joue un coup
    /// </summary>
    public MoveResult MakeMove(string playerId, int position)
    {
        var result = new MoveResult
        {
            Position = position,
            Success = false
        };
        
        // Vérifier que c'est le tour du joueur
        if (!IsPlayerTurn(playerId))
        {
            result.ErrorMessage = "Ce n'est pas votre tour";
            return result;
        }
        
        var playerSymbol = GetPlayerSymbol(playerId);
        if (!playerSymbol.HasValue)
        {
            result.ErrorMessage = "Vous n'êtes pas dans cette partie";
            return result;
        }
        
        // Tenter de jouer le coup
        if (!Board.MakeMove(position, playerSymbol.Value))
        {
            result.ErrorMessage = "Coup invalide";
            return result;
        }
        
        result.Success = true;
        result.Player = playerSymbol.Value;
        
        // Vérifier s'il y a un gagnant
        if (Board.CheckWinner(out var winner, out var winningLine))
        {
            Board.Status = winner == CellState.X ? GameStatus.XWins : GameStatus.OWins;
            Board.WinnerId = winner == CellState.X ? PlayerXId : PlayerOId;
            Board.WinningLine = winningLine;
            
            // Incrémenter le score
            if (winner == CellState.X)
                PlayerXScore++;
            else
                PlayerOScore++;
        }
        // Vérifier si c'est un match nul
        else if (Board.IsFull())
        {
            Board.Status = GameStatus.Draw;
        }
        // Sinon, changer de joueur
        else
        {
            Board.SwitchPlayer();
        }
        
        return result;
    }
    
    /// <summary>
    /// Démarre une nouvelle manche (rematch)
    /// </summary>
    public void StartNewRound()
    {
        Board.Reset();
        // Le perdant commence (ou X si match nul)
        if (Board.Status == GameStatus.OWins)
        {
            Board.CurrentPlayer = CellState.X;
        }
        else if (Board.Status == GameStatus.XWins)
        {
            Board.CurrentPlayer = CellState.O;
        }
    }
    
    /// <summary>
    /// Obtient l'état actuel de la partie
    /// </summary>
    public TicTacToeGameState GetGameState()
    {
        return new TicTacToeGameState
        {
            GameId = GameId,
            Board = Board.Cells,
            Status = Board.Status,
            CurrentPlayer = Board.CurrentPlayer,
            PlayerXId = PlayerXId,
            PlayerXName = PlayerXName,
            PlayerOId = PlayerOId,
            PlayerOName = PlayerOName,
            WinnerId = Board.WinnerId,
            WinningLine = Board.WinningLine,
            PlayerXScore = PlayerXScore,
            PlayerOScore = PlayerOScore
        };
    }
    
    /// <summary>
    /// Vérifie si la partie peut commencer
    /// </summary>
    public bool CanStart()
    {
        return PlayerXId != null && PlayerOId != null;
    }
    
    /// <summary>
    /// Vérifie si la partie est terminée
    /// </summary>
    public bool IsGameOver()
    {
        return Board.Status == GameStatus.XWins || 
               Board.Status == GameStatus.OWins || 
               Board.Status == GameStatus.Draw;
    }
    
    /// <summary>
    /// Retire un joueur de la partie
    /// </summary>
    public void RemovePlayer(string playerId)
    {
        if (playerId == PlayerXId)
        {
            PlayerXId = null;
            PlayerXName = null;
        }
        else if (playerId == PlayerOId)
        {
            PlayerOId = null;
            PlayerOName = null;
        }
        
        Board.Status = GameStatus.Cancelled;
    }
}
