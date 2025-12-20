extends Node

# Énumération des types de messages (correspond à MessageType en C#)
enum MessageType {
	# Messages client -> serveur
	PlayerConnect = 1,
	PlayerDisconnect = 2,
	PlayerForfeit = 3,
	CreateGame = 12,
	ListGames = 13,
	JoinGame = 14,
	MakeMove = 10,
	RequestRematch = 11,
	SyncGameState = 15,
	
	# Messages serveur -> client
	ServerWelcome = 100,
	ServerError = 101,
	GameState = 102,
	PlayerJoined = 103,
	PlayerLeft = 104,
	GameStarted = 105,
	GameEnded = 106,
	GameCreated = 107,
	GameList = 108,
	GameJoined = 109,
	MoveMade = 110,
	GameWon = 111,
	GameLoose = 112,
	RematchOffered = 113,
	MoveAccepted = 114,
	MoveRejected = 115,
	GameStateSynced = 116,
}

# Crée un message de base
static func create_message(type: int, player_id: String = "", data = null):
	# Format MessagePack avec [Key(N)] : utiliser un array au lieu d'un dict
	# [0] Type, [1] PlayerId, [2] Timestamp, [3] Data
	var timestamp = Time.get_unix_time_from_system() * 1000  # en millisecondes
	
	# Encoder Data en MessagePack selon son type
	var data_bytes = null
	if data != null:
		var MessagePackEncoder = preload("res://scripts/network/MessagePackEncoder.gd")
		if data is Dictionary:
			data_bytes = MessagePackEncoder.encode_dict(data)
		elif data is Array:
			data_bytes = MessagePackEncoder.encode_value(data)
		elif data is int or data is float or data is String or data is bool:
			data_bytes = MessagePackEncoder.encode_value(data)
		else:
			print("[GameMessages] Type de data non supporté: ", typeof(data))
	
	return [
		type,                    # [0] Type (int)
		player_id if player_id != "" else null,  # [1] PlayerId (string ou null)
		int(timestamp),          # [2] Timestamp (long/int64)
		data_bytes               # [3] Data (byte[] ou null)
	]

# Extrait les données d'un message (gère Array et Dictionary)
static func extract_data(message):
	# Si c'est un Array [Type, PlayerId, Timestamp, Data]
	if message is Array and message.size() > 3:
		var data_slot = message[3]
		if data_slot == null:
			return null
		if data_slot is PackedByteArray:
			# Décoder les bytes MessagePack
			var MessagePackEncoder = preload("res://scripts/network/MessagePackEncoder.gd")
			return MessagePackEncoder.decode_message(data_slot)
		if data_slot is Array or data_slot is Dictionary:
			return data_slot
		return data_slot
		return null
	
	# Ancien format Dictionary (rétrocompatibilité)
	if message is Dictionary:
		if message.has("Data") and message["Data"] != null:
			return message["Data"]
	
	return null

# === Messages Client -> Serveur ===

# Connexion d'un joueur
static func create_player_connect(player_name: String, user_id: String = "") -> Array:
	# Encoder en array pour correspondre à [Key(0)] PlayerName, [Key(1)] UserId
	var data = [
		player_name,
		user_id if user_id != "" else null
	]
	print("[GameMessages] Création PlayerConnect - Name: ", player_name, ", UserId: ", user_id)
	var msg = create_message(MessageType.PlayerConnect, "", data)
	print("[GameMessages] Message PlayerConnect créé: ", msg)
	return msg

# Création de partie
static func create_create_game(game_name: String, character: String) -> Array:
	# Encoder en array pour correspondre à [Key(0)] GameName, [Key(1)] Character
	var data = [
		game_name,
		character
	]
	return create_message(MessageType.CreateGame, "", data)

# Rejoindre une partie
static func create_join_game(game_id: String, character: String) -> Array:
	# Encoder en array pour correspondre à [Key(0)] GameId, [Key(1)] Character
	var data = [
		game_id,
		character
	]
	return create_message(MessageType.JoinGame, "", data)

# Lister les parties
static func create_list_games() -> Array:
	return create_message(MessageType.ListGames)

# Jouer un coup
static func create_make_move(player_id: String, position: int) -> Array:
	# Encodé comme un array [position] pour correspondre au [Key(0)] côté C#
	var data = [position]
	return create_message(MessageType.MakeMove, player_id, data)

# Synchroniser l'état du jeu
static func create_sync_game_state(player_id: String) -> Array:
	return create_message(MessageType.SyncGameState, player_id, {})

# Déconnexion
static func create_player_disconnect(player_id: String) -> Array:
	return create_message(MessageType.PlayerDisconnect, player_id, {})

# === Parsers pour messages Serveur -> Client ===

# Parse ServerWelcome
static func parse_server_welcome(message) -> Dictionary:
	var data = extract_data(message)
	var result = {"PlayerId": "", "Message": ""}
	if message is Array and message.size() > 1 and message[1] != null:
		result["PlayerId"] = message[1]
	if data is Dictionary:
		result["PlayerId"] = data.get("PlayerId", result["PlayerId"])
		result["Message"] = data.get("Message", "")
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			result["PlayerId"] = data[0]
		if data.size() > 1 and data[1] != null:
			result["Message"] = data[1]
	return result

# Parse GameCreated
static func parse_game_created(message) -> Dictionary:
	var data = extract_data(message)
	var summary = _parse_game_summary(null)
	if data is Dictionary:
		if data.has("Game"):
			summary = _parse_game_summary(data["Game"])
		else:
			summary = _parse_game_summary(data)
	elif data is Array:
		if data.size() > 0:
			summary = _parse_game_summary(data[0])
	return {
		"Game": summary,
		"GameId": summary.get("GameId", ""),
		"GameName": summary.get("Name", "")
	}

# Parse GameState
static func parse_game_state(message) -> Dictionary:
	var data = extract_data(message)
	if data == null or not (data is Dictionary):
		return {}
	if data.has("Game"):
		return data["Game"]
	return {}

# Parse GameJoined
static func parse_game_joined(message) -> Dictionary:
	var data = extract_data(message)
	var summary = _parse_game_summary(null)
	var role = ""
	if data is Dictionary:
		if data.has("Game"):
			summary = _parse_game_summary(data["Game"])
		else:
			summary = _parse_game_summary(data)
		role = data.get("Role", "")
	elif data is Array:
		if data.size() > 0:
			summary = _parse_game_summary(data[0])
		if data.size() > 1 and data[1] != null:
			role = data[1]
	return {
		"Game": summary,
		"Role": role,
		"GameId": summary.get("GameId", "")
	}

# Parse GameList
static func parse_game_list(message) -> Array:
	var data = extract_data(message)
	print("[GameMessages] parse_game_list - data type: ", typeof(data), ", data: ", data)
	
	if data == null:
		return []
	
	# Si data est un Dictionary avec clé "Games"
	if data is Dictionary and data.has("Games"):
		return data["Games"]
	
	# Si data est un Array (format [Games] avec [Key(0)])
	if data is Array:
		# Si c'est un array avec 1 élément qui est lui-même un array de games
		if data.size() > 0 and data[0] is Array:
			return data[0]  # data[0] = Games
		# Sinon c'est directement l'array de games
		return data
	
	return []

# Parse GameStateSynced
static func parse_game_state_synced(message) -> Dictionary:
	var data = extract_data(message)
	var result = {
		"PlayerIds": [],
		"PlayerMoves": {},
		"GameStatus": "",
		"WinnerId": null,
		"CharactersName": {},
		"PlayerNames": {}
	}
	if data is Dictionary:
		result["PlayerIds"] = data.get("PlayerIds", [])
		result["PlayerMoves"] = data.get("PlayerMoves", {})
		result["GameStatus"] = data.get("GameStatus", "")
		result["WinnerId"] = data.get("WinnerId", null)
		result["CharactersName"] = data.get("CharactersName", {})
		result["PlayerNames"] = data.get("PlayerNames", {})
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			result["PlayerIds"] = data[0]
		if data.size() > 1 and data[1] != null:
			result["PlayerMoves"] = data[1]
		if data.size() > 2 and data[2] != null:
			result["GameStatus"] = data[2]
		if data.size() > 3:
			result["WinnerId"] = data[3]
		if data.size() > 4 and data[4] != null:
			result["CharactersName"] = data[4]
		if data.size() > 5 and data[5] != null:
			result["PlayerNames"] = data[5]
	return result

# Parse MoveAccepted
static func parse_move_accepted(message) -> Dictionary:
	var data = extract_data(message)
	var result = {"PlayerId": "", "Position": -1}
	if data is Dictionary:
		result["PlayerId"] = data.get("PlayerId", "")
		result["Position"] = data.get("Position", -1)
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			result["PlayerId"] = data[0]
		if data.size() > 1 and data[1] != null:
			result["Position"] = data[1]
	return result

# Parse MoveRejected
static func parse_move_rejected(message) -> Dictionary:
	var data = extract_data(message)
	var result = {"Reason": "", "Position": -1}
	if data is Dictionary:
		result["Reason"] = data.get("Reason", "")
		result["Position"] = data.get("Position", -1)
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			result["Reason"] = data[0]
		if data.size() > 1 and data[1] != null:
			result["Position"] = data[1]
	return result

# Parse GameWon
static func parse_game_won(message) -> Dictionary:
	var data = extract_data(message)
	var result = {"WinnerId": "", "WinnerName": "", "WinningPositions": []}
	if data is Dictionary:
		result["WinnerId"] = data.get("WinnerId", "")
		result["WinnerName"] = data.get("WinnerName", "")
		result["WinningPositions"] = data.get("WinningPositions", [])
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			result["WinnerId"] = data[0]
		if data.size() > 1 and data[1] != null:
			result["WinnerName"] = data[1]
		if data.size() > 2 and data[2] != null:
			result["WinningPositions"] = data[2]
	return result

# Parse ErrorData
static func parse_error(message) -> Dictionary:
	var data = extract_data(message)
	var result = {"ErrorCode": "", "Message": ""}
	if data is Dictionary:
		result["ErrorCode"] = data.get("ErrorCode", "")
		result["Message"] = data.get("Message", "")
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			result["ErrorCode"] = data[0]
		if data.size() > 1 and data[1] != null:
			result["Message"] = data[1]
	return result

static func _parse_game_summary(data) -> Dictionary:
	var summary = {
		"GameId": "",
		"Name": "",
		"PlayerCount": 0,
		"MaxPlayers": 0,
		"Status": "",
		"SpectatorCount": 0
	}
	if data is Dictionary:
		for key in summary.keys():
			if data.has(key):
				summary[key] = data[key]
	elif data is Array:
		if data.size() > 0 and data[0] != null:
			summary["GameId"] = data[0]
		if data.size() > 1 and data[1] != null:
			summary["Name"] = data[1]
		if data.size() > 2 and data[2] != null:
			summary["PlayerCount"] = data[2]
		if data.size() > 3 and data[3] != null:
			summary["MaxPlayers"] = data[3]
		if data.size() > 4 and data[4] != null:
			summary["Status"] = data[4]
		if data.size() > 5 and data[5] != null:
			summary["SpectatorCount"] = data[5]
	return summary

# Extraire le type d'un message (Array format)
static func get_message_type(message) -> int:
	if message is Array and message.size() > 0:
		return message[0]
	if message is Dictionary:
		return message.get("Type", -1)
	return -1

# Obtenir le nom du type de message (pour debug)
static func get_message_type_name(type: int) -> String:
	match type:
		MessageType.PlayerConnect: return "PlayerConnect"
		MessageType.PlayerDisconnect: return "PlayerDisconnect"
		MessageType.CreateGame: return "CreateGame"
		MessageType.ListGames: return "ListGames"
		MessageType.JoinGame: return "JoinGame"
		MessageType.MakeMove: return "MakeMove"
		MessageType.RequestRematch: return "RequestRematch"
		MessageType.SyncGameState: return "SyncGameState"
		MessageType.ServerWelcome: return "ServerWelcome"
		MessageType.ServerError: return "ServerError"
		MessageType.GameState: return "GameState"
		MessageType.PlayerJoined: return "PlayerJoined"
		MessageType.PlayerLeft: return "PlayerLeft"
		MessageType.GameStarted: return "GameStarted"
		MessageType.GameEnded: return "GameEnded"
		MessageType.GameCreated: return "GameCreated"
		MessageType.GameList: return "GameList"
		MessageType.GameJoined: return "GameJoined"
		MessageType.MoveMade: return "MoveMade"
		MessageType.GameWon: return "GameWon"
		MessageType.GameLoose: return "GameLoose"
		MessageType.RematchOffered: return "RematchOffered"
		MessageType.MoveAccepted: return "MoveAccepted"
		MessageType.MoveRejected: return "MoveRejected"
		MessageType.GameStateSynced: return "GameStateSynced"
		_: return "Unknown"
