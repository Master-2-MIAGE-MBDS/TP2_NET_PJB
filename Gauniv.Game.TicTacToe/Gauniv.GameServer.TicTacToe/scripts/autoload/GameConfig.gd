extends Node

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

# Configuration réseau
const SERVER_HOST = "127.0.0.1"  # localhost pour test local
const SERVER_PORT = 7777
const SERVER_URL = "http://serveur.com/api"  # Ancien système HTTP (à supprimer plus tard)

# État global du jeu
var is_online_mode = false
var online_game_id = ""
var my_player_role = ""  # "X" ou "O"
var my_player_id = ""    # UUID du joueur
var player_name = ""     # Pseudo du joueur

# Personnages sélectionnés
var selected_character_p1 = ""
var selected_character_p2 = ""

func get_character_texture(character: String) -> Texture2D:
	return load("res://assets/" + character + "/" + character + ".png")

func get_character_sound(character: String) -> AudioStream:
	return load("res://assets/" + character + "/son_" + character + ".mp3")

func reset_online_state():
	is_online_mode = false
	online_game_id = ""
	my_player_role = ""
	my_player_id = ""
	player_name = ""

# === FONCTIONS UTILITAIRES POUR LE NOUVEAU FORMAT ===

# Reconstruire le board visuel à partir des pawns
func build_board_from_pawns(x_pawns: Array, o_pawns: Array) -> Array:
	var board = ["", "", "", "", "", "", "", "", ""]
	for pos in x_pawns:
		board[pos] = CELL_X
	for pos in o_pawns:
		board[pos] = CELL_O
	return board

# Déterminer à qui c'est le tour
func get_current_player_from_pawns(x_pawns: Array, o_pawns: Array) -> String:
	if x_pawns.size() == o_pawns.size():
		return CELL_X
	else:
		return CELL_O

# Vérifier si une position est libre
func is_position_free(x_pawns: Array, o_pawns: Array, pos: int) -> bool:
	return not (pos in x_pawns) and not (pos in o_pawns)

# Vérifier une victoire à partir des pawns
func check_winner_from_pawns(pawns: Array) -> bool:
	for pattern in WIN_PATTERNS:
		if pattern[0] in pawns and pattern[1] in pawns and pattern[2] in pawns:
			return true
	return false
