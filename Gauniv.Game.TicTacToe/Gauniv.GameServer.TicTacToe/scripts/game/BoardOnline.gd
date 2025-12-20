extends "res://scripts/game/BoardLocal.gd"
class_name BoardOnline

var online_manager: Node
var tcp_manager: Node
var last_x_pawns_size: int = 0
var last_o_pawns_size: int = 0
var pending_winner_id: String = ""
var pending_winner_name: String = ""
var last_winner_detected: String = ""

func _ready():
	super._ready()
	online_manager = preload("res://scripts/game/OnlineManager.gd").new()
	add_child(online_manager)
	online_manager.board_updated.connect(_on_board_updated)
	online_manager.opponent_joined.connect(_on_opponent_joined)
	online_manager.game_started.connect(_on_game_started)
	online_manager.game_won.connect(_on_game_won)
	online_manager.game_lost.connect(_on_game_lost)

func start_online_game_as_host(character: String):
	GameConfig.is_online_mode = true
	visible = true
	_apply_character_assets(character, "")
	var my_name = GameConfig.player_name if GameConfig.player_name != "" else "Moi"
	_apply_player_names(my_name, "En attente...")
	_reset_board()
	_set_buttons_enabled(false)
	_show_waiting_label()
	button_back.visible = false
	_setup_tcp_connection()
	online_manager.start_listening()

func start_online_game_as_guest(game_data: Dictionary):
	GameConfig.is_online_mode = true
	visible = true
	var char_x = game_data.get("player_x", {}).get("character", "")
	var char_o = game_data.get("player_o", {}).get("character", "")
	_load_assets(char_x, char_o)
	var my_name = GameConfig.player_name if GameConfig.player_name != "" else "Moi"
	_apply_player_names("Hote", my_name)
	_reset_board()
	if game_data.has("boardstate"):
		_apply_boardstate(game_data["boardstate"])
	button_back.visible = false
	_setup_tcp_connection()
	online_manager.start_listening()

func _setup_tcp_connection():
	var node = self as Node
	while node and not node.has_node("OnlineMenu"):
		node = node.get_parent()
		if node == null:
			break
	if node and node.has_node("OnlineMenu"):
		var online_menu = node.get_node("OnlineMenu")
		if "tcp_manager" in online_menu and online_menu.tcp_manager:
			tcp_manager = online_menu.tcp_manager
			online_manager.set_tcp_manager(tcp_manager)
		else:
			push_warning("[BoardOnline] tcp_manager introuvable dans OnlineMenu")
	else:
		push_warning("[BoardOnline] OnlineMenu introuvable")

func _on_button_click(idx: int, button: Button):
	if game_ended:
		return
	# Les spectateurs ne peuvent pas jouer
	if GameConfig.my_player_role == GameConfig.SPECTATOR:
		return
	if not GameConfig.is_position_free(x_pawns, o_pawns, idx):
		return
	if current_player != GameConfig.my_player_role:
		return
	_set_buttons_enabled(false)
	online_manager.send_move(idx)

func _on_restart_pressed():
	online_manager.stop_polling()
	emit_signal("return_to_menu")

func _on_opponent_joined(character: String):
	var is_local_x = GameConfig.my_player_role == GameConfig.CELL_X or GameConfig.my_player_role == ""
	var char_x = character if not is_local_x else ""
	var char_o = character if is_local_x else ""
	_apply_character_assets(char_x, char_o)
	_hide_waiting_label()

func _on_game_started():
	var is_my_turn = (current_player == GameConfig.my_player_role)
	_set_buttons_enabled(is_my_turn)
	_hide_waiting_label()

func _on_board_updated(boardstate: Dictionary):
	if boardstate == null:
		return
	var new_x_pawns = boardstate.get("X_pawns", [])
	var new_o_pawns = boardstate.get("O_pawns", [])
	var winner = boardstate.get("winner", null)
	var char_x = boardstate.get("character_x", "")
	var char_o = boardstate.get("character_o", "")
	var name_x = boardstate.get("player_name_x", "")
	var name_o = boardstate.get("player_name_o", "")
	var name_winner = boardstate.get("winner_name", "")
	_apply_boardstate(boardstate)
	last_x_pawns_size = new_x_pawns.size()
	last_o_pawns_size = new_o_pawns.size()
	if winner != null and name_winner != "":
		_show_result_label_for_name(name_winner)

func _apply_boardstate(boardstate: Dictionary):
	var previous_x_pawns = x_pawns.duplicate()
	var previous_o_pawns = o_pawns.duplicate()
	var new_x_pawns = boardstate.get("X_pawns", [])
	var new_o_pawns = boardstate.get("O_pawns", [])
	var winner = boardstate.get("winner", null)
	var char_x = boardstate.get("character_x", "")
	var char_o = boardstate.get("character_o", "")
	var name_x = boardstate.get("player_name_x", "")
	var name_o = boardstate.get("player_name_o", "")
	var player_x = boardstate.get("player_x_id", player_x_id)
	var player_o = boardstate.get("player_o_id", player_o_id)
	var winner_name = boardstate.get("winner_name", "")
	_apply_character_assets(char_x, char_o)
	_apply_player_names(name_x, name_o)
	player_x_id = player_x
	player_o_id = player_o
	for button in buttons:
		button.icon = null
		button.disabled = false
		button.modulate = Color(1, 1, 1, 1)
	for pos in new_x_pawns:
		buttons[pos].icon = texture_x
		buttons[pos].disabled = true
	for pos in new_o_pawns:
		buttons[pos].icon = texture_o
		buttons[pos].disabled = true
	x_pawns = new_x_pawns.duplicate()
	o_pawns = new_o_pawns.duplicate()
	_update_piece_styles(x_pawns)
	_update_piece_styles(o_pawns)
	var x_changed = _pawns_changed(previous_x_pawns, x_pawns)
	var o_changed = _pawns_changed(previous_o_pawns, o_pawns)
	
	# Détecter le joueur qui a fait le dernier coup
	var last_player_who_moved = ""
	if x_changed and not o_changed:
		current_player = GameConfig.CELL_O
		last_player_who_moved = GameConfig.CELL_X
	elif o_changed and not x_changed:
		current_player = GameConfig.CELL_X
		last_player_who_moved = GameConfig.CELL_O
	else:
		current_player = GameConfig.get_current_player_from_pawns(x_pawns, o_pawns)
	
	# Jouer le son approprié
	if winner != null and winner != last_winner_detected:
		# Nouveau winner détecté, jouer le son gagnant
		var winning_player = GameConfig.CELL_X if winner == player_x_id else GameConfig.CELL_O
		_play_move_sound(winning_player, true)
		last_winner_detected = winner
	elif last_player_who_moved != "":
		# Coup normal
		_play_move_sound(last_player_who_moved, false)
	
	if winner != null:
		# Vérifier si c'est une victoire par forfait ou une victoire normale
		var winner_pawns = x_pawns if winner == player_x_id else o_pawns
		var has_winning_line = GameConfig.check_winner_from_pawns(winner_pawns)
		var is_forfeit = not has_winning_line
		_handle_remote_victory(winner, winner_name, is_forfeit)
	else:
		var is_my_turn = (current_player == GameConfig.my_player_role)
		_set_buttons_enabled(is_my_turn and not game_ended)
		_hide_result_label()

func _handle_remote_victory(winner: String, winner_name: String, is_forfeit: bool = false):
	game_ended = true
	button_restart.visible = true
	button_back.visible = true
	online_manager.stop_polling()
	var display_name = winner_name
	if display_name == "" and pending_winner_name != "":
		display_name = pending_winner_name
	if winner == "" and pending_winner_id != "":
		winner = pending_winner_id
	if display_name == "":
		if winner == player_x_id:
			display_name = player_name_x
		elif winner == player_o_id:
			display_name = player_name_o
	_show_result_label_for_name(display_name, is_forfeit)
	pending_winner_id = ""
	pending_winner_name = ""

func _handle_game_finished(data: Dictionary):
	game_ended = true
	button_restart.visible = true
	button_back.visible = true
	_set_buttons_enabled(false)
	pending_winner_id = data.get("WinnerId", "")
	pending_winner_name = data.get("WinnerName", "")
	var winning_positions = data.get("WinningPositions", [])
	var is_forfeit = (winning_positions == null or (winning_positions is Array and winning_positions.size() == 0))
	
	# Si c'est un forfait, on peut afficher tout de suite
	if is_forfeit and pending_winner_id != "":
		_handle_remote_victory(pending_winner_id, pending_winner_name, true)
	else:
		# Victoire normale : vérifier si on a déjà la ligne gagnante sur le board local
		# (cas de l'hôte qui vient de jouer son coup gagnant)
		var winner_pawns = x_pawns if pending_winner_id == player_x_id else o_pawns
		if GameConfig.check_winner_from_pawns(winner_pawns):
			# On a déjà le board à jour, afficher la victoire maintenant
			_handle_remote_victory(pending_winner_id, pending_winner_name, false)
		# Sinon, on attend le sync du board pour afficher le dernier coup et la victoire

func _on_game_won(data: Dictionary):
	_handle_game_finished(data)

func _on_game_lost(data: Dictionary):
	_handle_game_finished(data)

func _reset_board():
	super._reset_board()
	last_x_pawns_size = 0
	last_o_pawns_size = 0
	_set_buttons_enabled(false)
	pending_winner_id = ""
	pending_winner_name = ""
	last_winner_detected = ""

func _pawns_changed(old_pawns: Array, new_pawns: Array) -> bool:
	if old_pawns.size() != new_pawns.size():
		return true
	for i in range(new_pawns.size()):
		if i >= old_pawns.size():
			return true
		if old_pawns[i] != new_pawns[i]:
			return true
	return false

func _play_move_sound(cell: String, is_winning_move: bool = false):
	var sound: AudioStream
	if is_winning_move:
		sound = sound_x_win if cell == GameConfig.CELL_X else sound_o_win
	else:
		sound = sound_x if cell == GameConfig.CELL_X else sound_o
	
	if sound:
		audio_player.stream = sound
		audio_player.play()
