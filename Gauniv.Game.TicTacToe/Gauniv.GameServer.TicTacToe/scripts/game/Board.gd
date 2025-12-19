extends Node2D

signal game_over(winner: String)
signal return_to_menu

@onready var buttons = $GridContainer.get_children()
@onready var button_restart = $ButtonRestart
@onready var button_back = $ButtonBack
@onready var score_p1 = $ScoreP1
@onready var score_p2 = $ScoreP2
@onready var label_score_p1 = $ScoreP1/LabelScoreP1
@onready var label_score_p2 = $ScoreP2/LabelScoreP2
@onready var image_p1 = $ScoreP1/ImageP1
@onready var image_p2 = $ScoreP2/ImageP2
@onready var label_name_p1 = $ScoreP1/LabelPlayerName
@onready var label_name_p2 = $ScoreP2/LabelPlayerName
@onready var label_result = $LabelResult
@onready var audio_player = $AudioPlayer
@onready var label_waiting = $LabelWaiting
@onready var waiting_timer = $WaitingTimer

var online_manager: Node
var tcp_manager: Node  # Référence au gestionnaire TCP

var texture_x: Texture2D
var texture_o: Texture2D
var sound_x: AudioStream
var sound_o: AudioStream
var character_x_name: String = ""
var character_o_name: String = ""
var player_name_x: String = ""
var player_name_o: String = ""
var player_x_id: String = ""
var player_o_id: String = ""

var current_player: String
var game_ended: bool
var x_pawns: Array = []
var o_pawns: Array = []
var score_x: int = 0
var score_o: int = 0

# Pour détecter les changements lors du polling
var last_x_pawns_size: int = 0
var last_o_pawns_size: int = 0

# Animation des points d'attente
var waiting_dots_count: int = 0

func _ready():
	for i in range(buttons.size()):
		buttons[i].connect("pressed", _on_button_click.bind(i, buttons[i]))
	
	button_back.pressed.connect(func(): emit_signal("return_to_menu"))
	button_restart.connect("pressed", _on_restart_pressed)
	
	# Connecter le timer d'attente
	waiting_timer.connect("timeout", _on_waiting_timer_timeout)
	
	# Créer le manager en ligne
	online_manager = preload("res://scripts/game/OnlineManager.gd").new()
	add_child(online_manager)
	online_manager.board_updated.connect(_on_board_updated)
	online_manager.opponent_joined.connect(_on_opponent_joined)
	online_manager.game_started.connect(_on_game_started)
	online_manager.game_won.connect(_on_game_won)
	online_manager.game_lost.connect(_on_game_lost)

func start_local_game(char_p1: String, char_p2: String):
	visible = true
	_load_assets(char_p1, char_p2)
	_apply_player_names("Joueur X", "Joueur O")
	_reset_board()
	_update_score_labels()
	_hide_waiting_label()

func start_online_game_as_host(character: String):
	visible = true
	_apply_character_assets(character, "")
	var my_name = GameConfig.player_name if GameConfig.player_name != "" else "Moi"
	_apply_player_names(my_name, "En attente...")
	_reset_board()
	_set_buttons_enabled(false)  # Attendre l'adversaire
	print("[Board] Host game démarrée - character=", character, ", player_id=", GameConfig.my_player_id)
	
	# Afficher le message d'attente
	_show_waiting_label()
	
	# Configurer le TCP manager
	_setup_tcp_connection()
	online_manager.start_listening()

func start_online_game_as_guest(game_data: Dictionary):
	visible = true
	var char_x = game_data.get("player_x", {}).get("character", "")
	var char_o = game_data.get("player_o", {}).get("character", "")
	_load_assets(char_x, char_o)
	var my_name = GameConfig.player_name if GameConfig.player_name != "" else "Moi"
	_apply_player_names("Hôte", my_name)
	_reset_board()
	
	# Appliquer l'état initial si présent
	if game_data.has("boardstate"):
		_apply_boardstate(game_data["boardstate"])
	
	print("[Board] Guest game démarrée - data=", game_data)

	# Configurer le TCP manager
	_setup_tcp_connection()
	online_manager.start_listening()

func _setup_tcp_connection():
	# Récupérer le TCP manager depuis le parent (game.gd)
	var game_node = get_parent()
	if game_node.has_node("OnlineMenu"):
		var online_menu = game_node.get_node("OnlineMenu")
		if "tcp_manager" in online_menu and online_menu.tcp_manager:
			tcp_manager = online_menu.tcp_manager
			online_manager.set_tcp_manager(tcp_manager)
			print("[Board] TCP manager configuré avec succès")
		else:
			print("[Board] ERREUR: tcp_manager non trouvé dans OnlineMenu")
	else:
		print("[Board] ERREUR: OnlineMenu non trouvé")

func _load_assets(char_p1: String, char_p2: String):
	var resolved_x = _resolve_character(char_p1, GameConfig.CELL_X)
	var resolved_o = _resolve_character(char_p2, GameConfig.CELL_O)
	character_x_name = resolved_x
	character_o_name = resolved_o
	texture_x = GameConfig.get_character_texture(resolved_x)
	texture_o = GameConfig.get_character_texture(resolved_o)
	sound_x = GameConfig.get_character_sound(resolved_x)
	sound_o = GameConfig.get_character_sound(resolved_o)
	image_p1.texture = texture_x
	image_p2.texture = texture_o
	print("[Board] Assets chargés - X:", resolved_x, " / O:", resolved_o)

func _resolve_character(character: String, slot: String) -> String:
	if character != "":
		return character
	var fallback = ""
	if slot == GameConfig.CELL_X and GameConfig.selected_character_p1 != "":
		fallback = GameConfig.selected_character_p1
	elif slot == GameConfig.CELL_O and GameConfig.selected_character_p2 != "":
		fallback = GameConfig.selected_character_p2
	else:
		fallback = GameConfig.CHARACTERS[0]
	print("[Board] Character manquant pour slot ", slot, ", utilisation du fallback: ", fallback)
	return fallback

func _apply_character_assets(char_x: String, char_o: String):
	if char_x != "" and (char_x != character_x_name or texture_x == null):
		var resolved_x = _resolve_character(char_x, GameConfig.CELL_X)
		character_x_name = resolved_x
		texture_x = GameConfig.get_character_texture(resolved_x)
		sound_x = GameConfig.get_character_sound(resolved_x)
		image_p1.texture = texture_x
	if char_o != "" and (char_o != character_o_name or texture_o == null):
		var resolved_o = _resolve_character(char_o, GameConfig.CELL_O)
		character_o_name = resolved_o
		texture_o = GameConfig.get_character_texture(resolved_o)
		sound_o = GameConfig.get_character_sound(resolved_o)
		image_p2.texture = texture_o

func _default_player_name(slot: String) -> String:
	return "Joueur " + (slot if slot != "" else "?")

func _apply_player_names(name_x: String, name_o: String):
	if name_x != "" and name_x != player_name_x:
		player_name_x = name_x
	elif player_name_x == "":
		player_name_x = GameConfig.player_name if GameConfig.player_name != "" and GameConfig.my_player_role == GameConfig.CELL_X else _default_player_name(GameConfig.CELL_X)

	if name_o != "" and name_o != player_name_o:
		player_name_o = name_o
	elif player_name_o == "":
		player_name_o = GameConfig.player_name if GameConfig.player_name != "" and GameConfig.my_player_role == GameConfig.CELL_O else _default_player_name(GameConfig.CELL_O)

	label_name_p1.text = player_name_x
	label_name_p2.text = player_name_o

func _get_display_name_for_cell(cell: String) -> String:
	return player_name_x if cell == GameConfig.CELL_X else player_name_o

func _show_result_label_for_name(name: String):
	var display = name if name != "" else "Un joueur"
	label_result.text = display + " gagne !"
	label_result.visible = true

func _hide_result_label():
	label_result.visible = false

func _reset_board():
	current_player = GameConfig.CELL_X
	game_ended = false
	x_pawns = []
	o_pawns = []
	last_x_pawns_size = 0
	last_o_pawns_size = 0
	button_restart.visible = false
	_hide_result_label()
	
	for button in buttons:
		button.text = ""
		button.icon = null
	score_p1.visible = true
	score_p2.visible = true

func _on_button_click(idx: int, button: Button):
	if game_ended:
		return
	
	# Vérifier si la case est libre
	if not GameConfig.is_position_free(x_pawns, o_pawns, idx):
		return
	
	# En mode en ligne, vérifier le tour
	if GameConfig.is_online_mode:
		if current_player != GameConfig.my_player_role:
			return
		# Laisser le serveur valider et renvoyer l'état
		_set_buttons_enabled(false)
		online_manager.send_move(idx)
		return

	_play_move(idx)

func _play_move(idx: int):
	var pawns = x_pawns if current_player == GameConfig.CELL_X else o_pawns
	
	# Retirer le plus ancien pion si nécessaire
	if pawns.size() >= GameConfig.MAX_PIECES:
		var oldest_idx = pawns.pop_front()
		buttons[oldest_idx].icon = null
		buttons[oldest_idx].disabled = false
		buttons[oldest_idx].mlistening()
		
		if current_player == GameConfig.CELL_X:
			score_x += 1
		else:
			score_o += 1
		_update_score_labels()
		emit_signal("game_over", current_player)
	else:
		current_player = GameConfig.CELL_O if current_player == GameConfig.CELL_X else GameConfig.CELL_X
		
		if GameConfig.is_online_mode:
			var is_my_turn = (current_player == GameConfig.my_player_role)
			_set_buttons_enabled(is_my_turn and not game_ended)
	
	# Vérifier victoire
	if _check_winner(current_player):
		game_ended = true
		button_restart.visible = true
		if GameConfig.is_online_mode:
			online_manager.stop_polling()
		_show_result_label_for_name(_get_display_name_for_cell(current_player))
		
		if current_player == GameConfig.CELL_X:
			score_x += 1
		else:
			score_o += 1
		_update_score_labels()
		emit_signal("game_over", current_player)
	else:
		current_player = GameConfig.CELL_O if current_player == GameConfig.CELL_X else GameConfig.CELL_X
		
		if GameConfig.is_online_mode:
			var is_my_turn = (current_player == GameConfig.my_player_role)
			_set_buttons_enabled(is_my_turn)

func _check_winner(player: String) -> bool:
	var pawns = x_pawns if player == GameConfig.CELL_X else o_pawns
	return GameConfig.check_winner_from_pawns(pawns)

func _update_piece_styles(pawns: Array):
	# Le plus ancien pion devient semi-transparent s'il y en a 3
	if pawns.size() == GameConfig.MAX_PIECES:
		buttons[pawns[0]].modulate = Color(1, 1, 1, 0.4)
	
	# Les autres sont opaques
	for i in range(1, pawns.size()):
		buttons[pawns[i]].modulate = Color(1, 1, 1, 1)

func _set_buttons_enabled(enabled: bool):
	for i in range(9):
		var is_free = GameConfig.is_position_free(x_pawns, o_pawns, i)
		buttons[i].disabled = not (enabled and is_free)

func _update_score_labels():
	label_score_p1.text = str(score_x)
	label_score_p2.text = str(score_o)

func _on_restart_pressed():
	if GameConfig.is_online_mode:
		online_manager.stop_polling()
		emit_signal("return_to_menu")
	else:
		_reset_board()

func _on_opponent_joined(character: String):
	print("[Board] opponent_joined reçu - character=", character)
	var is_local_x = GameConfig.my_player_role == GameConfig.CELL_X or GameConfig.my_player_role == ""
	var char_x = character if not is_local_x else ""
	var char_o = character if is_local_x else ""
	_apply_character_assets(char_x, char_o)
	
	# Cacher le message d'attente
	_hide_waiting_label()

func _on_game_started():
	# L'adversaire a rejoint, activer le jeu si c'est notre tour
	var is_my_turn = (current_player == GameConfig.my_player_role)
	print("[Board] game_started reçu - current_player=", current_player, ", my_role=", GameConfig.my_player_role, ", enable=", is_my_turn)
	_set_buttons_enabled(is_my_turn)
	
	# Cacher le message d'attente
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
	
	# Détecter si un nouveau coup a été joué
	var x_changed = new_x_pawns.size() != last_x_pawns_size
	var o_changed = new_o_pawns.size() != last_o_pawns_size
	var characters_changed = false
	if char_x != "" and char_x != character_x_name:
		characters_changed = true
	if char_o != "" and char_o != character_o_name:
		characters_changed = true
	var names_changed = false
	if name_x != "" and name_x != player_name_x:
		names_changed = true
	if name_o != "" and name_o != player_name_o:
		names_changed = true
	
	if x_changed or o_changed or characters_changed or names_changed:
		_apply_boardstate(boardstate)
		last_x_pawns_size = new_x_pawns.size()
		last_o_pawns_size = new_o_pawns.size()
	elif winner != null and name_winner != "":
		_show_result_label_for_name(name_winner)

func _apply_boardstate(boardstate: Dictionary):
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
	
	# Réinitialiser l'affichage
	for button in buttons:
		button.icon = null
		button.disabled = false
		button.modulate = Color(1, 1, 1, 1)
	
	# Appliquer les pions X
	for i in range(new_x_pawns.size()):
		var pos = new_x_pawns[i]
		buttons[pos].icon = texture_x
		buttons[pos].disabled = true
	
	# Appliquer les pions O
	for i in range(new_o_pawns.size()):
		var pos = new_o_pawns[i]
		buttons[pos].icon = texture_o
		buttons[pos].disabled = true
	
	# Mettre à jour les variables locales
	x_pawns = new_x_pawns.duplicate()
	o_pawns = new_o_pawns.duplicate()
	
	# Mettre à jour les styles (pion semi-transparent)
	_update_piece_styles(x_pawns)
	_update_piece_styles(o_pawns)
	
	# Déterminer le joueur actuel
	current_player = GameConfig.get_current_player_from_pawns(x_pawns, o_pawns)
	
	# Jouer le son du dernier coup
	if new_x_pawns.size() > last_x_pawns_size:
		audio_player.stream = sound_x
		audio_player.play()
	elif new_o_pawns.size() > last_o_pawns_size:
		audio_player.stream = sound_o
		audio_player.play()
	
	# Vérifier victoire
	if winner != null:
		game_ended = true
		button_restart.visible = true
		online_manager.stop_polling()
		var display_name = winner_name
		if display_name == "":
			if winner == player_x_id:
				display_name = player_name_x
			elif winner == player_o_id:
				display_name = player_name_o
		_show_result_label_for_name(display_name)
	else:
		# Activer/désactiver selon le tour
		var is_my_turn = (current_player == GameConfig.my_player_role)
		_set_buttons_enabled(is_my_turn and not game_ended)
		_hide_result_label()

# Fonctions pour gérer le label "En attente..."
func _show_waiting_label():
	label_waiting.visible = true
	waiting_dots_count = 0
	label_waiting.text = "En attente."
	print("[Board] Affiche label attente")
	waiting_timer.start()

func _hide_waiting_label():
	label_waiting.visible = false
	print("[Board] Cache label attente")
	waiting_timer.stop()

func _on_waiting_timer_timeout():
	waiting_dots_count = (waiting_dots_count + 1) % 4
	match waiting_dots_count:
		0:
			label_waiting.text = "En attente."
		1:
			label_waiting.text = "En attente.."
		2:
			label_waiting.text = "En attente..."
		3:
			label_waiting.text = "En attente"

func _handle_game_finished(data: Dictionary):
	game_ended = true
	button_restart.visible = true
	_set_buttons_enabled(false)
	online_manager.stop_polling()
	var winner_id = data.get("WinnerId", "")
	var winner_name = data.get("WinnerName", "")
	var display = winner_name
	if display == "":
		if winner_id == player_x_id:
			display = player_name_x
		elif winner_id == player_o_id:
			display = player_name_o
	_show_result_label_for_name(display)

func _on_game_won(data: Dictionary):
	_handle_game_finished(data)

func _on_game_lost(data: Dictionary):
	_handle_game_finished(data)
