extends Node2D
class_name BoardLocal

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

var texture_x: Texture2D
var texture_o: Texture2D
var sound_x: AudioStream
var sound_o: AudioStream
var sound_x_win: AudioStream
var sound_o_win: AudioStream
var character_x_name: String = ""
var character_o_name: String = ""
var player_name_x: String = ""
var player_name_o: String = ""
var player_x_id: String = ""
var player_o_id: String = ""

var current_player: String = GameConfig.CELL_X
var game_ended: bool = false
var x_pawns: Array = []
var o_pawns: Array = []
var score_x: int = 0
var score_o: int = 0

func _ready():
	for i in range(buttons.size()):
		buttons[i].connect("pressed", _on_button_click.bind(i, buttons[i]))
	button_back.pressed.connect(func(): emit_signal("return_to_menu"))
	button_restart.connect("pressed", _on_restart_pressed)
	waiting_timer.connect("timeout", _on_waiting_timer_timeout)

func start_local_game(char_p1: String, char_p2: String):
	GameConfig.is_online_mode = false
	visible = true
	_load_assets(char_p1, char_p2)
	_apply_player_names("Joueur X", "Joueur O")
	_reset_board()
	_set_buttons_enabled(true)
	_hide_waiting_label()

func _load_assets(char_p1: String, char_p2: String):
	var resolved_x = _resolve_character(char_p1, GameConfig.CELL_X)
	var resolved_o = _resolve_character(char_p2, GameConfig.CELL_O)
	character_x_name = resolved_x
	character_o_name = resolved_o
	texture_x = GameConfig.get_character_texture(resolved_x)
	texture_o = GameConfig.get_character_texture(resolved_o)
	sound_x = GameConfig.get_character_sound(resolved_x)
	sound_o = GameConfig.get_character_sound(resolved_o)
	sound_x_win = load("res://assets/" + resolved_x + "/son2_" + resolved_x + ".mp3")
	sound_o_win = load("res://assets/" + resolved_o + "/son2_" + resolved_o + ".mp3")
	image_p1.texture = texture_x
	image_p2.texture = texture_o

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
	return fallback

func _apply_character_assets(char_x: String, char_o: String):
	if char_x != "" and (char_x != character_x_name or texture_x == null):
		var resolved_x = _resolve_character(char_x, GameConfig.CELL_X)
		character_x_name = resolved_x
		texture_x = GameConfig.get_character_texture(resolved_x)
		sound_x = GameConfig.get_character_sound(resolved_x)
		sound_x_win = load("res://assets/" + resolved_x + "/son2_" + resolved_x + ".mp3")
		image_p1.texture = texture_x
	if char_o != "" and (char_o != character_o_name or texture_o == null):
		var resolved_o = _resolve_character(char_o, GameConfig.CELL_O)
		character_o_name = resolved_o
		texture_o = GameConfig.get_character_texture(resolved_o)
		sound_o = GameConfig.get_character_sound(resolved_o)
		sound_o_win = load("res://assets/" + resolved_o + "/son2_" + resolved_o + ".mp3")
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
	button_restart.visible = false
	_hide_result_label()
	for button in buttons:
		button.text = ""
		button.icon = null
		button.disabled = false
		button.modulate = Color(1, 1, 1, 1)
	score_p1.visible = true
	score_p2.visible = true
	_set_buttons_enabled(true)
	_update_score_labels()

func _on_button_click(idx: int, button: Button):
	if game_ended:
		return
	if not GameConfig.is_position_free(x_pawns, o_pawns, idx):
		return
	_play_move(idx)

func _play_move(idx: int):
	var pawns = x_pawns if current_player == GameConfig.CELL_X else o_pawns
	pawns.append(idx)
	var texture = texture_x if current_player == GameConfig.CELL_X else texture_o
	buttons[idx].icon = texture
	buttons[idx].disabled = true
	buttons[idx].modulate = Color(1, 1, 1, 1)
	_update_piece_styles(pawns)
	if pawns.size() > GameConfig.MAX_PIECES:
		var oldest_idx = pawns.pop_front()
		if oldest_idx != null:
			buttons[oldest_idx].icon = null
			buttons[oldest_idx].disabled = false
			buttons[oldest_idx].modulate = Color(1, 1, 1, 1)
		_update_piece_styles(pawns)
	_check_victory()

func _check_victory():
	var is_winner = _check_winner(current_player)
	
	# Jouer le son approprié
	var sound: AudioStream
	if is_winner:
		sound = sound_x_win if current_player == GameConfig.CELL_X else sound_o_win
	else:
		sound = sound_x if current_player == GameConfig.CELL_X else sound_o
	
	if sound:
		audio_player.stream = sound
		audio_player.play()
	
	if is_winner:
		game_ended = true
		button_restart.visible = true
		_show_result_label_for_name(_get_display_name_for_cell(current_player))
		_set_buttons_enabled(false)
		if current_player == GameConfig.CELL_X:
			score_x += 1
		else:
			score_o += 1
		_update_score_labels()
		emit_signal("game_over", current_player)
	else:
		current_player = GameConfig.CELL_O if current_player == GameConfig.CELL_X else GameConfig.CELL_X

func _check_winner(player: String) -> bool:
	var pawns = x_pawns if player == GameConfig.CELL_X else o_pawns
	return GameConfig.check_winner_from_pawns(pawns)

func _update_piece_styles(pawns: Array):
	if pawns.size() == GameConfig.MAX_PIECES:
		buttons[pawns[0]].modulate = Color(1, 1, 1, 0.4)
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
	emit_signal("return_to_menu")

func _show_waiting_label():
	label_waiting.visible = true
	waiting_dots_count = 0
	label_waiting.text = "En attente."
	waiting_timer.start()

func _hide_waiting_label():
	label_waiting.visible = false
	waiting_timer.stop()

var waiting_dots_count: int = 0

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
