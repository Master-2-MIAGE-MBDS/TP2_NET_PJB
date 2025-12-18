extends Node2D

const CELL_EMPTY = ""
const CELL_X = "X"
const CELL_O = "O"
const MAX_PIECES = 3

const WIN_PATTERNS = [
	[0, 1, 2], [3, 4, 5], [6, 7, 8],
	[0, 3, 6], [1, 4, 7], [2, 5, 8],
	[0, 4, 8], [2, 4, 6]
]

const CHARACTERS = ["bayrou", "hollande", "lepen", "meluche", "sarkozy", "zemmour"]

# Mode de jeu
var is_online_mode = false

# Menu principal
@onready var main_menu = $MainMenu
@onready var btn_local = $MainMenu/ButtonLocal
@onready var btn_online = $MainMenu/ButtonOnline

# Menu de sélection
@onready var selection_menu = $SelectionMenu
@onready var button_start = $SelectionMenu/ButtonStart

# Sélection P1
@onready var btn_left_p1 = $SelectionMenu/SelectionP1/ButtonLeftP1
@onready var btn_right_p1 = $SelectionMenu/SelectionP1/ButtonRightP1
@onready var image_select_p1 = $SelectionMenu/SelectionP1/ImageSelectP1

# Sélection P2
@onready var btn_left_p2 = $SelectionMenu/SelectionP2/ButtonLeftP2
@onready var btn_right_p2 = $SelectionMenu/SelectionP2/ButtonRightP2
@onready var image_select_p2 = $SelectionMenu/SelectionP2/ImageSelectP2

# Plateau de jeu
@onready var buttons = $GridContainer.get_children()
@onready var button_restart = $ButtonRestart
@onready var label_score_p1 = $ScoreP1/LabelScoreP1
@onready var label_score_p2 = $ScoreP2/LabelScoreP2
@onready var image_p1 = $ScoreP1/ImageP1
@onready var image_p2 = $ScoreP2/ImageP2
@onready var audio_player = $AudioPlayer

# Éléments du jeu
@onready var score_p1 = $ScoreP1
@onready var score_p2 = $ScoreP2
@onready var grid_container = $GridContainer

# Index de sélection
var index_p1 = 0
var index_p2 = 1

# Assets
var texture_x
var texture_o
var sound_x
var sound_o

var current_player
var board
var game_over
var history_x = []
var history_o = []
var score_x = 0
var score_o = 0

func _ready():
	# Afficher uniquement le menu principal au démarrage
	_show_main_menu()
	
	# Connecter les boutons du menu principal
	btn_local.connect("pressed", _on_local_pressed)
	btn_online.connect("pressed", _on_online_pressed)
	
	# Connecter les flèches de sélection
	btn_left_p1.connect("pressed", _on_navigate.bind(-1, 1))
	btn_right_p1.connect("pressed", _on_navigate.bind(1, 1))
	btn_left_p2.connect("pressed", _on_navigate.bind(-1, 2))
	btn_right_p2.connect("pressed", _on_navigate.bind(1, 2))
	
	button_start.connect("pressed", _on_start_game)
	
	# Connecter les boutons du jeu
	var button_index = 0
	for button in buttons:
		button.connect("pressed", _on_button_click.bind(button_index, button))
		button_index += 1
	
	button_restart.connect("pressed", _on_restart_pressed)

func _show_main_menu():
	main_menu.visible = true
	selection_menu.visible = false
	_set_game_visible(false)

func _on_local_pressed():
	is_online_mode = false
	main_menu.visible = false
	selection_menu.visible = true
	_update_selection_display()

func _on_online_pressed():
	is_online_mode = true
	main_menu.visible = false
	# TODO: Afficher le menu en ligne
	print("Mode en ligne - À implémenter")

func _on_navigate(direction: int, player: int):
	if player == 1:
		index_p1 = (index_p1 + direction) % CHARACTERS.size()
		if index_p1 < 0:
			index_p1 = CHARACTERS.size() - 1
	else:
		index_p2 = (index_p2 + direction) % CHARACTERS.size()
		if index_p2 < 0:
			index_p2 = CHARACTERS.size() - 1
	
	_update_selection_display()

func _update_selection_display():
	var texture_p1 = load("res://assets/" + CHARACTERS[index_p1] + "/" + CHARACTERS[index_p1] + ".png")
	var texture_p2 = load("res://assets/" + CHARACTERS[index_p2] + "/" + CHARACTERS[index_p2] + ".png")
	
	image_select_p1.texture = texture_p1
	image_select_p2.texture = texture_p2

func _set_game_visible(visible: bool):
	score_p1.visible = visible
	score_p2.visible = visible
	grid_container.visible = visible
	button_restart.visible = false

func _on_start_game():
	var selected_p1 = CHARACTERS[index_p1]
	var selected_p2 = CHARACTERS[index_p2]
	
	texture_x = load("res://assets/" + selected_p1 + "/" + selected_p1 + ".png")
	texture_o = load("res://assets/" + selected_p2 + "/" + selected_p2 + ".png")
	sound_x = load("res://assets/" + selected_p1 + "/son_" + selected_p1 + ".mp3")
	sound_o = load("res://assets/" + selected_p2 + "/son_" + selected_p2 + ".mp3")
	
	image_p1.texture = texture_x
	image_p2.texture = texture_o
	
	selection_menu.visible = false
	_set_game_visible(true)
	
	_resetgame()
	_update_score_labels()

func _on_button_click(idx, button):
	if game_over:
		return
	
	if board[idx] != CELL_EMPTY:
		return
	
	var history = history_x if current_player == CELL_X else history_o
	
	if history.size() >= MAX_PIECES:
		var oldest_idx = history.pop_front()
		board[oldest_idx] = CELL_EMPTY
		buttons[oldest_idx].icon = null
		buttons[oldest_idx].disabled = false
		buttons[oldest_idx].modulate = Color(1, 1, 1, 1)
		_update_piece_styles(history, current_player)
	
	button.icon = texture_x if current_player == CELL_X else texture_o
	button.text = ""
	board[idx] = current_player
	button.disabled = true
	history.append(idx)
	
	audio_player.stream = sound_x if current_player == CELL_X else sound_o
	audio_player.play()
	
	_update_piece_styles(history, current_player)
	
	if _check_winner(current_player):
		game_over = true
		button_restart.visible = true
		
		if current_player == CELL_X:
			score_x += 1
		else:
			score_o += 1
		_update_score_labels()
	else:
		current_player = CELL_O if current_player == CELL_X else CELL_X

func _on_restart_pressed():
	_resetgame()

func _update_piece_styles(history, player):
	if history.size() == MAX_PIECES:
		var oldest_btn = buttons[history[0]]
		oldest_btn.modulate = Color(1, 1, 1, 0.4)
	
	for i in range(1, history.size()):
		buttons[history[i]].modulate = Color(1, 1, 1, 1)

func _check_winner(player):
	for pattern in WIN_PATTERNS:
		if board[pattern[0]] == player and board[pattern[1]] == player and board[pattern[2]] == player:
			return true
	return false

func _update_score_labels():
	if label_score_p1:
		label_score_p1.text = str(score_x)
	if label_score_p2:
		label_score_p2.text = str(score_o)

func _resetgame():
	current_player = CELL_X
	board = [CELL_EMPTY, CELL_EMPTY, CELL_EMPTY,
			 CELL_EMPTY, CELL_EMPTY, CELL_EMPTY,
			 CELL_EMPTY, CELL_EMPTY, CELL_EMPTY]
	game_over = false
	history_x = []
	history_o = []
	
	for button in buttons:
		button.text = ""
		button.icon = null
		button.disabled = false
		button.modulate = Color(1, 1, 1, 1)
	
	if button_restart:
		button_restart.visible = false
