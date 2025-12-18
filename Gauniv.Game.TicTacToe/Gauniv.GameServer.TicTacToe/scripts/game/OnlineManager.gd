extends Node

signal board_updated(game_state: Dictionary)
signal opponent_joined(character: String)
signal game_started()
signal move_accepted(position: int)
signal move_rejected(reason: String)
signal game_won(winner_data: Dictionary)
signal game_lost(winner_data: Dictionary)

var tcp_manager: Node
var GameMessages
var is_active: bool = false
var opponent_character: String = ""
var has_game_started: bool = false

func _ready():
	# Obtenir la référence au gestionnaire TCP depuis le menu
	# Sera initialisé depuis Board.gd
	GameMessages = preload("res://scripts/network/GameMessages.gd")

func set_tcp_manager(manager: Node):
	tcp_manager = manager
	if tcp_manager:
		tcp_manager.message_received.connect(_on_message_received)

func start_listening():
	is_active = true
	opponent_character = ""
	has_game_started = false
	print("[OnlineManager] Écoute des messages TCP activée - player_id=", GameConfig.my_player_id)
	
	# Demander l'état initial du jeu
	if tcp_manager and GameConfig.my_player_id != "":
		print("[OnlineManager] Demande de sync initiale")
		tcp_manager.send_sync_game_state(GameConfig.my_player_id)

func stop_polling():
	is_active = false
	print("[OnlineManager] Arrêt de l'écoute des messages")

func send_move(position: int):
	if not tcp_manager or not is_active:
		print("[OnlineManager] Impossible d'envoyer le coup: non actif")
		return
	
	if GameConfig.my_player_id == "":
		print("[OnlineManager] Impossible d'envoyer le coup: pas d'ID joueur")
		return
	
	tcp_manager.send_make_move(GameConfig.my_player_id, position)

# Traiter les messages reçus
func _on_message_received(message):
	if not is_active:
		return
	
	var msg_type = GameMessages.get_message_type(message)
	var msg_type_name = GameMessages.get_message_type_name(msg_type)
	print("[OnlineManager] Message reçu, type: ", msg_type_name, " (", msg_type, ")")
	
	match msg_type:
		GameMessages.MessageType.GameStateSynced:
			_handle_game_state_synced(message)
		GameMessages.MessageType.MoveAccepted:
			_handle_move_accepted(message)
		GameMessages.MessageType.MoveMade:
			_handle_move_made(message)
		GameMessages.MessageType.MoveRejected:
			_handle_move_rejected(message)
		GameMessages.MessageType.GameWon:
			_handle_game_won(message)
		GameMessages.MessageType.GameLoose:
			_handle_game_lost(message)
		GameMessages.MessageType.GameStarted:
			_handle_game_started(message)
		GameMessages.MessageType.PlayerJoined:
			_handle_player_joined(message)

func _handle_game_state_synced(message):
	var data = GameMessages.parse_game_state_synced(message)
	
	var player_ids = data.get("PlayerIds", [])
	var player_moves = data.get("PlayerMoves", {})
	var game_status = data.get("GameStatus", "")
	var winner_id = data.get("WinnerId", null)
	var characters = data.get("CharactersName", {})
	var player_names = data.get("PlayerNames", {})
	
	print("[OnlineManager] État synchronisé - joueurs=", player_ids, ", status=", game_status, ", winner=", winner_id)
	print("[OnlineManager] Characters: ", characters)
	
	# Détecter si un adversaire a rejoint
	if player_ids.size() == 2 and opponent_character == "":
		for pid in player_ids:
			if pid != GameConfig.my_player_id and characters.has(pid):
				print("[OnlineManager] Détection adversaire - pid=", pid)
				opponent_character = characters[pid]
				emit_signal("opponent_joined", opponent_character)
				print("[OnlineManager] Signal opponent_joined émis: ", opponent_character)
				break
	elif player_ids.size() < 2:
		if opponent_character != "":
			print("[OnlineManager] Adversaire parti, réinitialisation")
		opponent_character = ""
	
	# Convertir les données au format attendu par Board
	var x_pawns = []
	var o_pawns = []
	
	# Déterminer qui est X et qui est O
	var player_x_id = player_ids[0] if player_ids.size() > 0 else ""
	var player_o_id = player_ids[1] if player_ids.size() > 1 else ""
	var character_x = ""
	var character_o = ""
	var name_x = ""
	var name_o = ""
	var winner_name = ""
	if player_x_id != "" and characters.has(player_x_id):
		character_x = characters[player_x_id]
	if player_o_id != "" and characters.has(player_o_id):
		character_o = characters[player_o_id]
	if player_x_id != "" and player_names.has(player_x_id):
		name_x = player_names[player_x_id]
	if player_o_id != "" and player_names.has(player_o_id):
		name_o = player_names[player_o_id]
	if winner_id != null and player_names.has(winner_id):
		winner_name = player_names[winner_id]
	
	if player_moves.has(player_x_id):
		var moves = player_moves[player_x_id]
		for pos in moves:
			if pos != null:
				x_pawns.append(pos)
	
	if player_moves.has(player_o_id):
		var moves = player_moves[player_o_id]
		for pos in moves:
			if pos != null:
				o_pawns.append(pos)
	
	var boardstate = {
		"X_pawns": x_pawns,
		"O_pawns": o_pawns,
		"winner": winner_id,
		"character_x": character_x,
		"character_o": character_o,
		"player_name_x": name_x,
		"player_name_o": name_o,
		"player_x_id": player_x_id,
		"player_o_id": player_o_id,
		"winner_name": winner_name
	}
	
	emit_signal("board_updated", boardstate)

	# Signaler le démarrage dès que 2 joueurs sont présents (serveur n'envoie pas GameStarted)
	if player_ids.size() >= 2 and not has_game_started:
		has_game_started = true
		emit_signal("game_started")
		print("[OnlineManager] Signal game_started émis (2 joueurs)")
	elif player_ids.size() < 2:
		has_game_started = false

func _handle_move_accepted(message):
	var data = GameMessages.parse_move_accepted(message)
	var position = data.get("Position", -1)
	print("[OnlineManager] Coup accepté: position ", position)
	emit_signal("move_accepted", position)
	_request_sync_state()

func _handle_move_made(message):
	var data = GameMessages.parse_move_accepted(message)
	var position = data.get("Position", -1)
	var player_id = data.get("PlayerId", "")
	print("[OnlineManager] Coup adverse reçu - player=", player_id, ", position=", position)
	_request_sync_state()

func _handle_move_rejected(message):
	var data = GameMessages.parse_move_rejected(message)
	var reason = data.get("Reason", "Inconnu")
	print("[OnlineManager] Coup rejeté: ", reason)
	emit_signal("move_rejected", reason)

func _handle_game_won(message):
	var data = GameMessages.parse_game_won(message)
	print("[OnlineManager] Victoire!")
	emit_signal("game_won", data)

func _handle_game_lost(message):
	var data = GameMessages.parse_game_won(message)  # Même structure
	print("[OnlineManager] Défaite!")
	emit_signal("game_lost", data)

func _handle_game_started(message):
	print("[OnlineManager] Partie démarrée!")
	emit_signal("game_started")

func _handle_player_joined(message):
	print("[OnlineManager] Un joueur a rejoint")
	_request_sync_state()

func _request_sync_state():
	if tcp_manager and GameConfig.my_player_id != "":
		print("[OnlineManager] Demande de synchronisation")
		tcp_manager.send_sync_game_state(GameConfig.my_player_id)
