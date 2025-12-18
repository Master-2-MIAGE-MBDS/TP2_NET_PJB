# Game Messages Documentation

This document describes the JSON structure of each message type used in the Gauniv Game Server.

## Table of Contents
- [MessageType Enum](#messagetype-enum)
- [Base Message](#base-message)
- [Client Messages](#client-messages)
- [Server Messages](#server-messages)
- [Game Data Objects](#game-data-objects)

---

## MessageType Enum

```json
{
  "PlayerConnect": 1,
  "PlayerDisconnect": 2,
  "MakeMove": 10,
  "RequestRematch": 11,
  "CreateGame": 12,
  "ListGames": 13,
  "JoinGame": 14,
  "SyncGameState": 15,
  "ServerWelcome": 100,
  "ServerError": 101,
  "GameState": 102,
  "PlayerJoined": 103,
  "PlayerLeft": 104,
  "GameStarted": 105,
  "GameEnded": 106,
  "GameCreated": 107,
  "GameList": 108,
  "GameJoined": 109,
  "MoveMade": 110,
  "GameWon": 111,
  "GameLoose": 112,
  "RematchOffered": 113,
  "MoveAccepted": 114,
  "MoveRejected": 115,
  "GameStateSynced": 116
}
```

---

## Base Message

### GameMessage
Base message structure for all communications between client and server.

```json
{
  "Type": 1,
  "PlayerId": "player-123",
  "Timestamp": 1702901234567,
  "Data": [/* byte array */]
}
```

**Fields:**
- `Type` (number): MessageType enum value
- `PlayerId` (string, nullable): Unique identifier of the player
- `Timestamp` (number): Unix timestamp in milliseconds
- `Data` (byte array, nullable): Serialized message data

---

## Client Messages

### PlayerConnectData
Sent when a player connects to the server.

```json
{
  "PlayerName": "JohnDoe",
  "UserId": "user-456"
}
```

**Fields:**
- `PlayerName` (string): Display name of the player
- `UserId` (string, nullable): Optional user identifier

---

### CreateGameRequest
Request to create a new game.

```json
{
  "GameName": "My Tic-Tac-Toe Game"
}
```

**Fields:**
- `GameName` (string): Name of the game to create

---

### JoinGameRequest
Request to join an existing game.

```json
{
  "GameId": "game-789"
}
```

**Fields:**
- `GameId` (string): Unique identifier of the game to join

---

### PlayerActionData
Generic player action data.

```json
{
  "ActionType": "move",
  "Parameters": {
    "direction": "up",
    "speed": 10
  }
}
```

**Fields:**
- `ActionType` (string): Type of action performed
- `Parameters` (object, nullable): Additional action parameters

---

### MakeMoveData
Data for making a move in Tic-Tac-Toe.

```json
{
  "Position": 4
}
```

**Fields:**
- `Position` (number): Position on the grid (0-8)

---

## Server Messages

### ErrorData
Error message sent by the server.

```json
{
  "ErrorCode": "GAME_FULL",
  "Message": "The game is already full"
}
```

**Fields:**
- `ErrorCode` (string): Error code identifier
- `Message` (string): Human-readable error message

---

### GameCreatedData
Response when a game is successfully created.

```json
{
  "Game": {
    "GameId": "game-789",
    "Name": "My Tic-Tac-Toe Game",
    "PlayerCount": 1,
    "MaxPlayers": 2,
    "Status": "WAITING",
    "SpectatorCount": 0
  }
}
```

**Fields:**
- `Game` (GameSummary): Summary of the created game

---

### GameJoinedData
Response when a player successfully joins a game.

```json
{
  "Game": {
    "GameId": "game-789",
    "Name": "My Tic-Tac-Toe Game",
    "PlayerCount": 2,
    "MaxPlayers": 2,
    "Status": "IN_PROGRESS",
    "SpectatorCount": 0
  },
  "Role": "PLAYER"
}
```

**Fields:**
- `Game` (GameSummary): Summary of the joined game
- `Role` (string): Role assigned to the player (e.g., "PLAYER", "SPECTATOR")

---

### GameListResponse
List of available games.

```json
{
  "Games": [
    {
      "GameId": "game-789",
      "Name": "Game 1",
      "PlayerCount": 1,
      "MaxPlayers": 2,
      "Status": "WAITING",
      "SpectatorCount": 0
    },
    {
      "GameId": "game-790",
      "Name": "Game 2",
      "PlayerCount": 2,
      "MaxPlayers": 2,
      "Status": "IN_PROGRESS",
      "SpectatorCount": 1
    }
  ]
}
```

**Fields:**
- `Games` (array): List of GameSummary objects

---

### MoveAcceptedData
Confirmation that a move was accepted.

```json
{
  "PlayerId": "player-123",
  "Position": 4
}
```

**Fields:**
- `PlayerId` (string): ID of the player who made the move
- `Position` (number): Position where the move was made

---

### MoveRejectedData
Notification that a move was rejected.

```json
{
  "Reason": "Position already occupied",
  "Position": 4
}
```

**Fields:**
- `Reason` (string): Reason for rejection
- `Position` (number): Position that was rejected

---

### GameWonData
Notification that a player has won the game.

```json
{
  "WinnerId": "player-123",
  "WinnerName": "JohnDoe",
  "WinningPositions": [0, 1, 2]
}
```

**Fields:**
- `WinnerId` (string): ID of the winning player
- `WinnerName` (string): Name of the winning player
- `WinningPositions` (array): Positions that form the winning line

---

### GameStateSyncedData
Complete game state for synchronization.

```json
{
  "PlayerIds": ["player-123", "player-456"],
  "PlayerMoves": {
    "player-123": [0, 3, 6],
    "player-456": [1, 4, null]
  },
  "GameStatus": "IN_PROGRESS",
  "WinnerId": null
}
```

**Fields:**
- `PlayerIds` (array): Ordered list of player IDs
- `PlayerMoves` (object): Dictionary mapping player IDs to their moves (positions or null)
- `GameStatus` (string): Current game status ("IN_PROGRESS", "FINISHED", "WAITING")
- `WinnerId` (string, nullable): ID of the winner, or null if no winner yet

---

## Game Data Objects

### GameSummary
Summary information about a game.

```json
{
  "GameId": "game-789",
  "Name": "My Tic-Tac-Toe Game",
  "PlayerCount": 2,
  "MaxPlayers": 2,
  "Status": "IN_PROGRESS",
  "SpectatorCount": 0
}
```

**Fields:**
- `GameId` (string): Unique game identifier
- `Name` (string): Game name
- `PlayerCount` (number): Current number of players
- `MaxPlayers` (number): Maximum number of players allowed
- `Status` (string): Current game status
- `SpectatorCount` (number): Number of spectators

---

### GameStateData
Detailed game state information.

```json
{
  "GameId": "game-789",
  "Players": [
    {
      "PlayerId": "player-123",
      "PlayerName": "JohnDoe",
      "IsReady": true,
      "IsConnected": true
    },
    {
      "PlayerId": "player-456",
      "PlayerName": "JaneDoe",
      "IsReady": true,
      "IsConnected": true
    }
  ],
  "Status": "IN_PROGRESS",
  "CustomData": {
    "currentTurn": "player-123",
    "moveCount": 5
  }
}
```

**Fields:**
- `GameId` (string): Unique game identifier
- `Players` (array): List of PlayerInfo objects
- `Status` (string): Current game status
- `CustomData` (object, nullable): Additional game-specific data

---

### PlayerInfo
Information about a player in the game.

```json
{
  "PlayerId": "player-123",
  "PlayerName": "JohnDoe",
  "IsReady": true,
  "IsConnected": true
}
```

**Fields:**
- `PlayerId` (string): Unique player identifier
- `PlayerName` (string): Display name of the player
- `IsReady` (boolean): Whether the player is ready to start
- `IsConnected` (boolean): Whether the player is currently connected

---

## Usage Example

### Client Connecting and Creating a Game

1. **Player Connect**
```json
{
  "Type": 1,
  "PlayerId": null,
  "Timestamp": 1702901234567,
  "Data": /* Serialized PlayerConnectData */ {
    "PlayerName": "JohnDoe",
    "UserId": "user-456"
  }
}
```

2. **Server Welcome**
```json
{
  "Type": 100,
  "PlayerId": "player-123",
  "Timestamp": 1702901234568,
  "Data": null
}
```

3. **Create Game Request**
```json
{
  "Type": 12,
  "PlayerId": "player-123",
  "Timestamp": 1702901234570,
  "Data": /* Serialized CreateGameRequest */ {
    "GameName": "My Game"
  }
}
```

4. **Game Created Response**
```json
{
  "Type": 107,
  "PlayerId": "player-123",
  "Timestamp": 1702901234571,
  "Data": /* Serialized GameCreatedData */ {
    "Game": {
      "GameId": "game-789",
      "Name": "My Game",
      "PlayerCount": 1,
      "MaxPlayers": 2,
      "Status": "WAITING",
      "SpectatorCount": 0
    }
  }
}
```
