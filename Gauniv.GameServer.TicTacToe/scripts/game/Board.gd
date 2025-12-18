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
@onready var audio_player = $AudioPlayer

var online_manager: Node

var texture_x: Texture2D
var texture_o: Texture2D
var sound_x: AudioStream
var sound_o: AudioStream

var current_player: String
var game_ended: bool
var x_pawns: Array = []
var o_pawns: Array = []
var score_x: int = 0
var score_o: int = 0

# Pour détecter les changements lors du polling
var last_x_pawns_size: int = 0
var last_o_pawns_size: int = 0

func _ready():
	for i in range(buttons.size()):
		buttons[i].connect("pressed", _on_button_click.bind(i, buttons[i]))
	
	button_back.pressed.connect(func(): emit_signal("return_to_menu"))
	button_restart.connect("pressed", _on_restart_pressed)
	
	# Créer le manager en ligne
	online_manager = preload("res://scripts/game/OnlineManager.gd").new()
	add_child(online_manager)
	online_manager.board_updated.connect(_on_board_updated)
	online_manager.opponent_joined.connect(_on_opponent_joined)
	online_manager.game_started.connect(_on_game_started)

func start_local_game(char_p1: String, char_p2: String):
	visible = true
	_load_assets(char_p1, char_p2)
	_reset_board()
	_update_score_labels()

func start_online_game_as_host(character: String):
	visible = true
	texture_x = GameConfig.get_character_texture(character)
	sound_x = GameConfig.get_character_sound(character)
	image_p1.texture = texture_x
	_reset_board()
	_set_buttons_enabled(false)  # Attendre l'adversaire
	online_manager.start_polling()

func start_online_game_as_guest(game_data: Dictionary):
	visible = true
	var char_x = game_data["player_x"]["character"]
	var char_o = game_data["player_o"]["character"]
	_load_assets(char_x, char_o)
	_reset_board()
	
	# Appliquer l'état initial si présent
	if game_data.has("boardstate"):
		_apply_boardstate(game_data["boardstate"])
	
	online_manager.start_polling()

func _load_assets(char_p1: String, char_p2: String):
	texture_x = GameConfig.get_character_texture(char_p1)
	texture_o = GameConfig.get_character_texture(char_p2)
	sound_x = GameConfig.get_character_sound(char_p1)
	sound_o = GameConfig.get_character_sound(char_p2)
	image_p1.texture = texture_x
	image_p2.texture = texture_o

func _reset_board():
	current_player = GameConfig.CELL_X
	game_ended = false
	x_pawns = []
	o_pawns = []
	last_x_pawns_size = 0
	last_o_pawns_size = 0
	
	for button in buttons:
		button.text = ""
		button.icon = null
		button.disabled = false
		button.modulate = Color(1, 1, 1, 1)
	
	button_restart.visible = false
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
	
	_play_move(idx)
	
	# Envoyer au serveur après le coup
	if GameConfig.is_online_mode:
		var winner = null
		if game_ended:
			winner = current_player if _check_winner(current_player) else null
		online_manager.send_boardstate(x_pawns, o_pawns, winner)

func _play_move(idx: int):
	var pawns = x_pawns if current_player == GameConfig.CELL_X else o_pawns
	
	# Retirer le plus ancien pion si nécessaire
	if pawns.size() >= GameConfig.MAX_PIECES:
		var oldest_idx = pawns.pop_front()
		buttons[oldest_idx].icon = null
		buttons[oldest_idx].disabled = false
		buttons[oldest_idx].modulate = Color(1, 1, 1, 1)
	
	# Placer le nouveau pion
	var texture = texture_x if current_player == GameConfig.CELL_X else texture_o
	var sound = sound_x if current_player == GameConfig.CELL_X else sound_o
	
	buttons[idx].icon = texture
	buttons[idx].disabled = true
	pawns.append(idx)
	
	audio_player.stream = sound
	audio_player.play()
	
	_update_piece_styles(pawns)
	
	# Vérifier victoire
	if _check_winner(current_player):
		game_ended = true
		button_restart.visible = true
		if GameConfig.is_online_mode:
			online_manager.stop_polling()
		
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
	texture_o = GameConfig.get_character_texture(character)
	sound_o = GameConfig.get_character_sound(character)
	image_p2.texture = texture_o

func _on_game_started():
	# L'adversaire a rejoint, activer le jeu si c'est notre tour
	var is_my_turn = (current_player == GameConfig.my_player_role)
	_set_buttons_enabled(is_my_turn)

func _on_board_updated(boardstate: Dictionary):
	if boardstate == null:
		return
	
	var new_x_pawns = boardstate.get("X_pawns", [])
	var new_o_pawns = boardstate.get("O_pawns", [])
	var winner = boardstate.get("winner", null)
	
	# Détecter si un nouveau coup a été joué
	var x_changed = new_x_pawns.size() != last_x_pawns_size
	var o_changed = new_o_pawns.size() != last_o_pawns_size
	
	if x_changed or o_changed:
		_apply_boardstate(boardstate)
		last_x_pawns_size = new_x_pawns.size()
		last_o_pawns_size = new_o_pawns.size()

func _apply_boardstate(boardstate: Dictionary):
	var new_x_pawns = boardstate.get("X_pawns", [])
	var new_o_pawns = boardstate.get("O_pawns", [])
	var winner = boardstate.get("winner", null)
	
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
	else:
		# Activer/désactiver selon le tour
		var is_my_turn = (current_player == GameConfig.my_player_role)
		_set_buttons_enabled(is_my_turn and not game_ended)
