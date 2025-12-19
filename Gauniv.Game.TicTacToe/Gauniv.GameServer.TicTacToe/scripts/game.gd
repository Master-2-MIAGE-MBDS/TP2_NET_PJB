extends Node2D

# Référence aux menus
@onready var main_menu = $MainMenu
@onready var selection_menu = $SelectionMenu
@onready var online_menu = $OnlineMenu

# Référence au jeu
@onready var board_container = $BoardContainer

const BOARD_SCENE = preload("res://scenes/Board.tscn")
const BOARD_LOCAL_SCRIPT = preload("res://scripts/game/BoardLocal.gd")
const BOARD_ONLINE_SCRIPT = preload("res://scripts/game/BoardOnline.gd")

var current_board: BoardLocal = null
var current_board_is_online: bool = false

# Signaux pour la communication entre composants
signal game_started(is_online: bool)
signal game_ended(winner: String)
signal return_to_menu

func _ready():
	# Connecter les signaux des menus
	main_menu.local_selected.connect(_on_local_selected)
	main_menu.online_selected.connect(_on_online_selected)
	
	selection_menu.characters_selected.connect(_on_characters_selected)
	selection_menu.back_pressed.connect(_on_back_to_main)
	
	online_menu.game_created.connect(_on_online_game_created)
	online_menu.game_joined.connect(_on_online_game_joined)
	online_menu.back_pressed.connect(_on_back_to_main)
	
	# Afficher le menu principal
	_show_main_menu()

func _show_main_menu():
	main_menu.visible = true
	selection_menu.visible = false
	online_menu.visible = false
	if current_board:
		current_board.visible = false
	GameConfig.reset_online_state()
	_dispose_current_board()

func _on_local_selected():
	GameConfig.is_online_mode = false
	main_menu.visible = false
	selection_menu.show_for_local()

func _on_online_selected():
	GameConfig.is_online_mode = true
	main_menu.visible = false
	online_menu.show_lobby()

func _on_back_to_main():
	_show_main_menu()

func _on_characters_selected(char_p1: String, char_p2: String):
	GameConfig.selected_character_p1 = char_p1
	GameConfig.selected_character_p2 = char_p2
	selection_menu.visible = false
	var board = _ensure_board(false)
	board.start_local_game(char_p1, char_p2)

func _on_online_game_created(game_id: String, character: String):
	print("[Game] Signal game_created reçu!")
	print("[Game] game_id: ", game_id)
	print("[Game] character: ", character)
	
	GameConfig.online_game_id = game_id
	GameConfig.my_player_role = GameConfig.CELL_X
	GameConfig.selected_character_p1 = character
	online_menu.visible = false
	var board = _ensure_board(true)
	var online_board: BoardOnline = board as BoardOnline
	if online_board:
		online_board.start_online_game_as_host(character)

func _on_online_game_joined(game_data):
	# game_data peut être un Array [GameId, Name, PlayerCount, MaxPlayers, Status, SpectatorCount]
	# ou un Dictionary {"GameId": ..., "Name": ...}
	var game_id = ""
	if game_data is Array and game_data.size() > 0:
		game_id = game_data[0]  # Index 0 = GameId
	elif game_data is Dictionary:
		game_id = game_data.get("GameId", game_data.get("id", ""))
	
	GameConfig.online_game_id = game_id
	GameConfig.my_player_role = GameConfig.CELL_O
	online_menu.visible = false
	var board = _ensure_board(true)
	var online_board: BoardOnline = board as BoardOnline
	if online_board:
		online_board.start_online_game_as_guest(game_data)

func _on_game_over(winner: String):
	emit_signal("game_ended", winner)

func _ensure_board(is_online: bool) -> BoardLocal:
	if current_board and current_board_is_online == is_online and is_instance_valid(current_board):
		return current_board
	_dispose_current_board()
	var script_resource = BOARD_ONLINE_SCRIPT if is_online else BOARD_LOCAL_SCRIPT
	var board_instance = BOARD_SCENE.instantiate()
	board_instance.set_script(script_resource)
	board_container.add_child(board_instance)
	board_instance.visible = false
	board_instance.game_over.connect(_on_game_over)
	board_instance.return_to_menu.connect(_on_back_to_main)
	current_board = board_instance
	current_board_is_online = is_online
	return current_board

func _dispose_current_board():
	if current_board and is_instance_valid(current_board):
		var online_board: BoardOnline = current_board as BoardOnline
		if online_board and online_board.online_manager:
			online_board.online_manager.stop_polling()
		current_board.queue_free()
	current_board = null
	current_board_is_online = false
