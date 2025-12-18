extends Control

signal game_created(game_id: String, character: String)
signal game_joined(game_data: Dictionary)
signal back_pressed

# Éléments du lobby
@onready var btn_create = $ButtonCreate
@onready var btn_back = $ButtonBack
@onready var lobby_container = $LobbyContainer
@onready var label_status = $LabelStatus

# Éléments de sélection de personnage
@onready var selection_container = $SelectionContainer
@onready var btn_left = $SelectionContainer/HBoxContainer/SelectionP1/ButtonLeft
@onready var btn_right = $SelectionContainer/HBoxContainer/SelectionP1/ButtonRight
@onready var image_select = $SelectionContainer/HBoxContainer/SelectionP1/ImageSelect
@onready var btn_confirm_create = $SelectionContainer/ButtonConfirmCreate

# Champs de saisie
@onready var input_player_name = $SelectionContainer/InputPlayerName
@onready var input_game_name = $SelectionContainer/InputGameName
@onready var label_player_name = $SelectionContainer/LabelPlayerName
@onready var label_game_name = $SelectionContainer/LabelGameName

var tcp_manager: Node
var GameMessages
var index_selection = 0
var joining_game_id = ""
var is_connecting = false
var refresh_timer: Timer
var auto_refresh_enabled = false

func _ready():
	# Créer le gestionnaire TCP
	tcp_manager = preload("res://scripts/network/TCPManager.gd").new()
	add_child(tcp_manager)
	
	# Charger GameMessages
	GameMessages = preload("res://scripts/network/GameMessages.gd")
	
	# Connecter les signaux TCP
	tcp_manager.connected_to_server.connect(_on_tcp_connected)
	tcp_manager.disconnected_from_server.connect(_on_tcp_disconnected)
	tcp_manager.message_received.connect(_on_tcp_message_received)
	tcp_manager.connection_failed.connect(_on_tcp_connection_failed)
	
	btn_create.connect("pressed", _on_create_pressed)
	btn_back.connect("pressed", _on_back_pressed)
	
	btn_left.connect("pressed", _on_navigate.bind(-1))
	btn_right.connect("pressed", _on_navigate.bind(1))
	btn_confirm_create.connect("pressed", _on_confirm_create)
	
	# Créer le timer pour auto-refresh
	refresh_timer = Timer.new()
	refresh_timer.wait_time = 5.0
	refresh_timer.autostart = false
	refresh_timer.timeout.connect(_on_refresh_timer_timeout)
	add_child(refresh_timer)

func show_lobby():
	visible = true
	_show_lobby_elements()
	
	# Se connecter au serveur si pas déjà connecté
	if not tcp_manager.is_connected and not is_connecting:
		await _connect_to_server()
		# Une fois connecté, demander la liste
		if tcp_manager.is_connected:
			_on_refresh_pressed()

func _show_lobby_elements():
	# Afficher les éléments du lobby
	btn_create.visible = true
	lobby_container.visible = true
	label_status.visible = true
	
	# Cacher la sélection de personnage
	selection_container.visible = false
	
	# Démarrer l'auto-refresh
	auto_refresh_enabled = true
	refresh_timer.start()

func _show_selection_elements():
	# Cacher les éléments du lobby
	btn_create.visible = false
	lobby_container.visible = false
	label_status.visible = false
	
	# Arrêter l'auto-refresh
	auto_refresh_enabled = false
	refresh_timer.stop()
	
	# Afficher la sélection de personnage
	selection_container.visible = true
	_update_selection_display()
	
	# Afficher/cacher les champs selon le mode (créer ou rejoindre)
	if joining_game_id == "":
		# Mode création: afficher les deux champs
		label_player_name.visible = true
		input_player_name.visible = true
		label_game_name.visible = true
		input_game_name.visible = true
	else:
		# Mode rejoindre: seulement le pseudo
		label_player_name.visible = true
		input_player_name.visible = true
		label_game_name.visible = false
		input_game_name.visible = false

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
	var player_name = input_player_name.text.strip_edges()
	
	# Validation du pseudo
	if player_name == "":
		label_status.visible = true
		label_status.text = "Veuillez entrer un pseudo"
		return
	
	# Sauvegarder le pseudo dans GameConfig
	GameConfig.player_name = player_name
	
	if joining_game_id == "":
		# Mode création
		var game_name = input_game_name.text.strip_edges()
		if game_name == "":
			label_status.visible = true
			label_status.text = "Veuillez entrer un nom de partie"
			return
		
		# NOUVEAU FLOW : Basculer immédiatement sur le board
		var temp_game_id = "pending_" + str(Time.get_ticks_msec())
		GameConfig.online_game_id = temp_game_id
		GameConfig.my_player_role = GameConfig.CELL_X
		GameConfig.selected_character_p1 = character
		
		# Émettre le signal pour basculer sur le board
		print("[OnlineMenu] Basculement immédiat sur le board")
		emit_signal("game_created", temp_game_id, character)
		
		# Ensuite, créer la partie en arrière-plan
		_create_game_async(character, game_name, player_name)
	else:
		# Mode rejoindre - mémoriser notre personnage
		GameConfig.selected_character_p2 = character
		GameConfig.player_name = player_name
		# On peut attendre ici
		_join_game_with_wait(joining_game_id, character, player_name)

func _ensure_player_identity(player_name: String) -> bool:
	if GameConfig.my_player_id != "":
		print("[OnlineMenu] PlayerId déjà connu: ", GameConfig.my_player_id)
		return true

	if player_name == "":
		print("[OnlineMenu] ERREUR: pseudo vide pour PlayerConnect")
		return false

	if not tcp_manager.is_connected:
		print("[OnlineMenu] Connexion requise avant PlayerConnect")
		var success = await tcp_manager.connect_to_server(GameConfig.SERVER_HOST, GameConfig.SERVER_PORT)
		if not success:
			print("[OnlineMenu] Impossible de se connecter pour PlayerConnect")
			return false

	print("[OnlineMenu] Envoi PlayerConnect (ensure) pour: ", player_name)
	tcp_manager.send_player_connect(player_name)

	var timeout = 5.0
	var interval = 0.25
	while timeout > 0:
		if GameConfig.my_player_id != "":
			print("[OnlineMenu] PlayerId reçu après PlayerConnect: ", GameConfig.my_player_id)
			return true
		await get_tree().create_timer(interval).timeout
		timeout -= interval

	print("[OnlineMenu] TIMEOUT: PlayerId non reçu")
	return false

# Nouvelle fonction pour créer la partie en arrière-plan
func _create_game_async(character: String, game_name: String, player_name: String):
	# Vérifier la connexion au serveur
	if not tcp_manager.is_connected:
		print("[OnlineMenu] Connexion au serveur en cours...")
		var success = await tcp_manager.connect_to_server(GameConfig.SERVER_HOST, GameConfig.SERVER_PORT)
		if not success:
			print("[OnlineMenu] ERREUR: Impossible de se connecter")
			return
	
	# Si pas encore de player_id, envoyer PlayerConnect et attendre
	if not await _ensure_player_identity(player_name):
		print("[OnlineMenu] ABANDON création: PlayerId manquant")
		return
	
	print("[OnlineMenu] === CRÉATION DE PARTIE ===")
	print("[OnlineMenu] Nom: ", game_name)
	print("[OnlineMenu] Character: ", character)
	print("[OnlineMenu] Player name: ", player_name)
	print("[OnlineMenu] Player ID: ", GameConfig.my_player_id)
	
	tcp_manager.send_create_game(game_name, character)

# Fonction pour rejoindre avec attente
func _join_game_with_wait(game_id: String, character: String, player_name: String):
	# Vérifier la connexion au serveur
	if not tcp_manager.is_connected:
		label_status.text = "Connexion au serveur..."
		var success = await tcp_manager.connect_to_server(GameConfig.SERVER_HOST, GameConfig.SERVER_PORT)
		if not success:
			label_status.text = "Impossible de se connecter"
			return
	
	# Si pas encore de player_id, envoyer PlayerConnect
	if not await _ensure_player_identity(player_name):
		label_status.text = "PlayerId introuvable"
		return
	
	print("[OnlineMenu] Tentative de rejoindre: ", game_id)
	tcp_manager.send_join_game(game_id, character)

func _on_refresh_pressed():
	if not tcp_manager.is_connected:
		label_status.text = "Non connecté au serveur"
		return
	
	for child in lobby_container.get_children():
		child.queue_free()
	
	label_status.text = "Chargement..."
	tcp_manager.send_list_games()

func _on_refresh_timer_timeout():
	# Rafraîchir automatiquement si on est dans le lobby et connecté
	if auto_refresh_enabled and tcp_manager.is_connected and lobby_container.visible:
		print("[OnlineMenu] Auto-refresh de la liste des parties")
		tcp_manager.send_list_games()
	else:
		# Arrêter le timer si on n'est plus dans le lobby
		refresh_timer.stop()

func _on_select_game(game_id: String):
	joining_game_id = game_id
	_show_selection_elements()
	btn_confirm_create.text = "Rejoindre"

# === Fonctions de connexion TCP ===

func _connect_to_server():
	label_status.text = "Connexion au serveur..."
	is_connecting = true
	var success = await tcp_manager.connect_to_server(GameConfig.SERVER_HOST, GameConfig.SERVER_PORT)
	is_connecting = false

func _on_tcp_connected():
	print("[OnlineMenu] Connecté au serveur TCP")
	label_status.text = "Connecté! Créez ou rejoignez une partie"
	
	# Ne plus envoyer PlayerConnect ici, on attend que l'utilisateur entre son pseudo

func _on_tcp_disconnected():
	print("[OnlineMenu] Déconnecté du serveur")
	label_status.text = "Déconnecté du serveur"

func _on_tcp_connection_failed():
	print("[OnlineMenu] Échec de connexion")
	label_status.text = "Impossible de se connecter au serveur"
	is_connecting = false

func _on_tcp_message_received(message):
	var msg_type = GameMessages.get_message_type(message)
	var msg_type_name = GameMessages.get_message_type_name(msg_type)
	print("[OnlineMenu] Message reçu type: ", msg_type_name, " (", msg_type, ")")
	
	match msg_type:
		GameMessages.MessageType.ServerWelcome:
			_handle_server_welcome(message)
		GameMessages.MessageType.GameCreated:
			_handle_game_created(message)
		GameMessages.MessageType.GameList:
			_handle_game_list(message)
		GameMessages.MessageType.GameJoined:
			_handle_game_joined(message)
		GameMessages.MessageType.ServerError:
			_handle_server_error(message)

func _handle_server_welcome(message):
	var data = GameMessages.parse_server_welcome(message)
	var player_id = data.get("PlayerId", "")
	if player_id != "":
		GameConfig.my_player_id = player_id
		print("[OnlineMenu] PlayerId reçu: ", player_id)

func _handle_game_created(message):
	var data = GameMessages.parse_game_created(message)
	var game_id = data.get("GameId", "")
	
	print("[OnlineMenu] Partie créée avec succès!")
	print("[OnlineMenu] Vrai game_id reçu: ", game_id)
	
	# Mettre à jour le vrai game_id (le board est déjà affiché)
	GameConfig.online_game_id = game_id
	
	# NE PAS émettre le signal, le board est déjà affiché

func _handle_game_list(message):
	var games = GameMessages.parse_game_list(message)
	
	print("[OnlineMenu] Games reçues - type: ", typeof(games), ", taille: ", games.size() if games is Array else "N/A")
	if games.size() > 0:
		print("[OnlineMenu] Premier game - type: ", typeof(games[0]), ", contenu: ", games[0])
	
	# Vider le lobby
	for child in lobby_container.get_children():
		child.queue_free()
	
	if games.size() == 0:
		var no_game_label = Label.new()
		no_game_label.text = "Aucune partie disponible"
		no_game_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		no_game_label.add_theme_font_size_override("font_size", 16)
		no_game_label.modulate = Color(0.7, 0.7, 0.7)
		lobby_container.add_child(no_game_label)
		label_status.text = "Auto-refresh toutes les 5s"
		return
	
	label_status.text = str(games.size()) + " partie(s) • Auto-refresh toutes les 5s"
	
	# Séparer les parties en attente et en cours
	var waiting_games = []
	var in_progress_games = []
	
	for game in games:
		# game est un Array [GameId, Name, PlayerCount, MaxPlayers, Status, SpectatorCount]
		# Indices : [0]=GameId, [1]=Name, [2]=PlayerCount, [3]=MaxPlayers, [4]=Status, [5]=SpectatorCount
		var status = ""
		if game is Array and game.size() > 4:
			status = game[4]  # Index 4 = Status
		elif game is Dictionary:
			status = game.get("Status", "")
		
		if status == "WAITING":
			waiting_games.append(game)
		elif status == "IN_PROGRESS":
			in_progress_games.append(game)
	
	# Afficher les parties en attente
	if waiting_games.size() > 0:
		var label_waiting = Label.new()
		label_waiting.text = "⏳ PARTIES EN ATTENTE"
		label_waiting.add_theme_font_size_override("font_size", 26)
		label_waiting.modulate = Color(0.3, 1.0, 0.3)  # Vert clair
		label_waiting.add_theme_color_override("font_outline_color", Color.BLACK)
		label_waiting.add_theme_constant_override("outline_size", 3)
		lobby_container.add_child(label_waiting)
		
		# Espacement
		var spacer1 = Control.new()
		spacer1.custom_minimum_size = Vector2(0, 15)
		lobby_container.add_child(spacer1)
		
		for game in waiting_games:
			var game_panel = PanelContainer.new()
			game_panel.custom_minimum_size = Vector2(400, 120)
			var stylebox = StyleBoxFlat.new()
			stylebox.bg_color = Color(0.2, 0.3, 0.2, 0.8)
			stylebox.border_color = Color(0.3, 1.0, 0.3)
			stylebox.border_width_left = 4
			stylebox.border_width_right = 4
			stylebox.border_width_top = 4
			stylebox.border_width_bottom = 4
			stylebox.corner_radius_top_left = 10
			stylebox.corner_radius_top_right = 10
			stylebox.corner_radius_bottom_left = 10
			stylebox.corner_radius_bottom_right = 10
			stylebox.content_margin_left = 15
			stylebox.content_margin_right = 15
			stylebox.content_margin_top = 12
			stylebox.content_margin_bottom = 12
			game_panel.add_theme_stylebox_override("panel", stylebox)
			
			var vbox = VBoxContainer.new()
			vbox.add_theme_constant_override("separation", 8)
			
			var game_id = ""
			var game_name = "Partie sans nom"
			var player_count = 0
			
			if game is Array and game.size() >= 3:
				game_id = game[0]       # Index 0 = GameId
				game_name = game[1]     # Index 1 = Name
				player_count = game[2]  # Index 2 = PlayerCount
			elif game is Dictionary:
				game_id = game.get("GameId", "")
				game_name = game.get("Name", "Partie sans nom")
				player_count = game.get("PlayerCount", 0)
			
			var name_label = Label.new()
			name_label.text = "🎮 " + game_name
			name_label.add_theme_font_size_override("font_size", 22)
			name_label.add_theme_color_override("font_color", Color.WHITE)
			vbox.add_child(name_label)
			
			var info_label = Label.new()
			info_label.text = "👥 " + str(player_count) + "/2 joueurs"
			info_label.add_theme_font_size_override("font_size", 17)
			info_label.modulate = Color(0.8, 0.8, 0.8)
			vbox.add_child(info_label)
			
			var join_button = Button.new()
			join_button.text = "▶ Rejoindre"
			join_button.custom_minimum_size = Vector2(0, 42)
			join_button.add_theme_font_size_override("font_size", 18)
			var btn_style = StyleBoxFlat.new()
			btn_style.bg_color = Color(0.2, 0.6, 0.2)
			btn_style.corner_radius_top_left = 5
			btn_style.corner_radius_top_right = 5
			btn_style.corner_radius_bottom_left = 5
			btn_style.corner_radius_bottom_right = 5
			join_button.add_theme_stylebox_override("normal", btn_style)
			var btn_style_hover = StyleBoxFlat.new()
			btn_style_hover.bg_color = Color(0.3, 0.8, 0.3)
			btn_style_hover.corner_radius_top_left = 5
			btn_style_hover.corner_radius_top_right = 5
			btn_style_hover.corner_radius_bottom_left = 5
			btn_style_hover.corner_radius_bottom_right = 5
			join_button.add_theme_stylebox_override("hover", btn_style_hover)
			join_button.connect("pressed", _on_select_game.bind(game_id))
			vbox.add_child(join_button)
			
			game_panel.add_child(vbox)
			lobby_container.add_child(game_panel)
			
			# Espacement entre les parties
			var spacer = Control.new()
			spacer.custom_minimum_size = Vector2(0, 12)
			lobby_container.add_child(spacer)
	
	# Afficher les parties en cours
	if in_progress_games.size() > 0:
		# Ajouter un séparateur
		if waiting_games.size() > 0:
			var big_spacer = Control.new()
			big_spacer.custom_minimum_size = Vector2(0, 25)
			lobby_container.add_child(big_spacer)
		
		var label_in_progress = Label.new()
		label_in_progress.text = "⚔️ PARTIES EN COURS"
		label_in_progress.add_theme_font_size_override("font_size", 26)
		label_in_progress.modulate = Color(1.0, 0.5, 0.2)  # Orange
		label_in_progress.add_theme_color_override("font_outline_color", Color.BLACK)
		label_in_progress.add_theme_constant_override("outline_size", 3)
		lobby_container.add_child(label_in_progress)
		
		# Espacement
		var spacer2 = Control.new()
		spacer2.custom_minimum_size = Vector2(0, 15)
		lobby_container.add_child(spacer2)
		
		for game in in_progress_games:
			var game_panel = PanelContainer.new()
			game_panel.custom_minimum_size = Vector2(400, 60)
			var stylebox = StyleBoxFlat.new()
			stylebox.bg_color = Color(0.3, 0.2, 0.2, 0.6)
			stylebox.border_color = Color(1.0, 0.5, 0.2, 0.5)
			stylebox.border_width_left = 3
			stylebox.border_width_right = 3
			stylebox.border_width_top = 3
			stylebox.border_width_bottom = 3
			stylebox.corner_radius_top_left = 8
			stylebox.corner_radius_top_right = 8
			stylebox.corner_radius_bottom_left = 8
			stylebox.corner_radius_bottom_right = 8
			stylebox.content_margin_left = 12
			stylebox.content_margin_right = 12
			stylebox.content_margin_top = 10
			stylebox.content_margin_bottom = 10
			game_panel.add_theme_stylebox_override("panel", stylebox)
			
			var hbox = HBoxContainer.new()
			hbox.add_theme_constant_override("separation", 15)
			
			var game_name = "Partie sans nom"
			var player_count = 0
			
			if game is Array and game.size() >= 3:
				game_name = game[1]     # Index 1 = Name
				player_count = game[2]  # Index 2 = PlayerCount
			elif game is Dictionary:
				game_name = game.get("Name", "Partie sans nom")
				player_count = game.get("PlayerCount", 0)
			
			var game_label = Label.new()
			game_label.text = "🎮 " + game_name + "  |  👥 " + str(player_count) + "/2"
			game_label.add_theme_font_size_override("font_size", 18)
			game_label.modulate = Color(0.9, 0.9, 0.9)
			hbox.add_child(game_label)
			
			game_panel.add_child(hbox)
			lobby_container.add_child(game_panel)
			
			# Espacement
			var spacer = Control.new()
			spacer.custom_minimum_size = Vector2(0, 10)
			lobby_container.add_child(spacer)

func _handle_game_joined(message):
	var data = GameMessages.parse_game_joined(message)
	var game_data = data.get("Game", {})
	var role = data.get("Role", "")
	
	print("[OnlineMenu] Partie rejointe, rôle: ", role)
	GameConfig.online_game_id = game_data.get("GameId", "")
	GameConfig.my_player_role = GameConfig.CELL_O
	
	emit_signal("game_joined", game_data)

func _handle_server_error(message):
	var error_data = GameMessages.parse_error(message)
	var error_msg = error_data.get("Message", "Erreur inconnue")
	print("[OnlineMenu] Erreur serveur: ", error_msg)
	label_status.text = "Erreur: " + error_msg
	_show_lobby_elements()
