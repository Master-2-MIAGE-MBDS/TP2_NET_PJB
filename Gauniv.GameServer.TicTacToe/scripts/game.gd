extends Node2D

# Référence aux menus
@onready var main_menu = $MainMenu
@onready var selection_menu = $SelectionMenu
@onready var online_menu = $OnlineMenu

# Référence au jeu
@onready var board = $Board

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
	
	board.game_over.connect(_on_game_over)
	board.return_to_menu.connect(_on_back_to_main)
	
	# Afficher le menu principal
	_show_main_menu()

func _show_main_menu():
	main_menu.visible = true
	selection_menu.visible = false
	online_menu.visible = false
	board.visible = false
	GameConfig.reset_online_state()

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
	board.start_local_game(char_p1, char_p2)

func _on_online_game_created(game_id: String, character: String):
	GameConfig.online_game_id = game_id
	GameConfig.my_player_role = GameConfig.CELL_X
	GameConfig.selected_character_p1 = character
	online_menu.visible = false
	board.start_online_game_as_host(character)

func _on_online_game_joined(game_data: Dictionary):
	GameConfig.online_game_id = game_data["id"]
	GameConfig.my_player_role = GameConfig.CELL_O
	online_menu.visible = false
	board.start_online_game_as_guest(game_data)

func _on_game_over(winner: String):
	emit_signal("game_ended", winner)
