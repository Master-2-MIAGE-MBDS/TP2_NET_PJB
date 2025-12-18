extends Control

signal game_created(game_id: String, character: String)
signal game_joined(game_data: Dictionary)
signal back_pressed

# Éléments du lobby
@onready var btn_create = $ButtonCreate
@onready var btn_refresh = $ButtonRefresh
@onready var btn_back = $ButtonBack
@onready var lobby_container = $LobbyContainer
@onready var label_status = $LabelStatus

# Éléments de sélection de personnage
@onready var selection_container = $SelectionContainer
@onready var btn_left = $SelectionContainer/HBoxContainer/SelectionP1/ButtonLeft
@onready var btn_right = $SelectionContainer/HBoxContainer/SelectionP1/ButtonRight
@onready var image_select = $SelectionContainer/HBoxContainer/SelectionP1/ImageSelect
@onready var btn_confirm_create = $SelectionContainer/ButtonConfirmCreate

var http_request: HTTPRequest
var index_selection = 0
var joining_game_id = ""

func _ready():
	http_request = HTTPRequest.new()
	add_child(http_request)
	
	btn_create.connect("pressed", _on_create_pressed)
	btn_refresh.connect("pressed", _on_refresh_pressed)
	btn_back.connect("pressed", _on_back_pressed)
	
	btn_left.connect("pressed", _on_navigate.bind(-1))
	btn_right.connect("pressed", _on_navigate.bind(1))
	btn_confirm_create.connect("pressed", _on_confirm_create)

func show_lobby():
	visible = true
	_show_lobby_elements()
	_on_refresh_pressed()

func _show_lobby_elements():
	# Afficher les éléments du lobby
	btn_create.visible = true
	btn_refresh.visible = true
	lobby_container.visible = true
	label_status.visible = true
	
	# Cacher la sélection de personnage
	selection_container.visible = false

func _show_selection_elements():
	# Cacher les éléments du lobby
	btn_create.visible = false
	btn_refresh.visible = false
	lobby_container.visible = false
	label_status.visible = false
	
	# Afficher la sélection de personnage
	selection_container.visible = true
	_update_selection_display()

func _on_back_pressed():
	# Si on est dans la sélection, revenir au lobby
	if selection_container.visible:
		_show_lobby_elements()
	else:
		# Sinon, retour au menu principal
		emit_signal("back_pressed")

func _on_create_pressed():
	_show_selection_elements()
	btn_confirm_create.text = "Créer la partie"
	joining_game_id = ""

func _on_navigate(direction: int):
	var characters = GameConfig.CHARACTERS
	index_selection = (index_selection + direction) % characters.size()
	if index_selection < 0:
		index_selection = characters.size() - 1
	_update_selection_display()

func _update_selection_display():
	var character = GameConfig.CHARACTERS[index_selection]
	image_select.texture = GameConfig.get_character_texture(character)

func _on_confirm_create():
	var character = GameConfig.CHARACTERS[index_selection]
	
	if joining_game_id == "":
		_create_game(character)
	else:
		_join_game(joining_game_id, character)

func _on_refresh_pressed():
	for child in lobby_container.get_children():
		child.queue_free()
	
	label_status.text = "Chargement..."
	
	http_request.request_completed.connect(_on_lobby_received, CONNECT_ONE_SHOT)
	http_request.request(GameConfig.SERVER_URL + "/games")

func _on_lobby_received(result, response_code, headers, body):
	for child in lobby_container.get_children():
		child.queue_free()
	
	if response_code != 200:
		label_status.text = "Erreur de connexion"
		return
	
	var json = JSON.parse_string(body.get_string_from_utf8())
	
	if json == null or json.size() == 0:
		label_status.text = "Aucune partie disponible"
		return
	
	label_status.text = "Parties disponibles :"
	
	for game in json:
		if game["status"] == "waiting":
			var game_button = Button.new()
			game_button.text = game["host"] + " (" + game["host_character"] + ")"
			game_button.connect("pressed", _on_select_game.bind(game["id"]))
			lobby_container.add_child(game_button)

func _on_select_game(game_id: String):
	joining_game_id = game_id
	_show_selection_elements()
	btn_confirm_create.text = "Rejoindre"

func _create_game(character: String):
	var data = {"host_character": character}
	var json_data = JSON.stringify(data)
	var headers = ["Content-Type: application/json"]
	
	http_request.request_completed.connect(_on_game_created, CONNECT_ONE_SHOT)
	http_request.request(GameConfig.SERVER_URL + "/games", headers, HTTPClient.METHOD_POST, json_data)

func _on_game_created(result, response_code, headers, body):
	if response_code != 200 and response_code != 201:
		label_status.text = "Erreur création"
		_show_lobby_elements()
		return
	
	var json = JSON.parse_string(body.get_string_from_utf8())
	var character = GameConfig.CHARACTERS[index_selection]
	
	if json.has("player_id"):
		GameConfig.my_player_id = json["player_id"]
	
	emit_signal("game_created", json["id"], character)

func _join_game(game_id: String, character: String):
	var data = {"player_character": character}
	var json_data = JSON.stringify(data)
	var headers = ["Content-Type: application/json"]
	
	http_request.request_completed.connect(_on_game_joined, CONNECT_ONE_SHOT)
	http_request.request(GameConfig.SERVER_URL + "/games/" + game_id + "/join", headers, HTTPClient.METHOD_POST, json_data)

func _on_game_joined(result, response_code, headers, body):
	if response_code != 200:
		label_status.text = "Erreur connexion"
		_show_lobby_elements()
		return
	
	var json = JSON.parse_string(body.get_string_from_utf8())
	
	if json.has("player_id"):
		GameConfig.my_player_id = json["player_id"]
	
	emit_signal("game_joined", json)
