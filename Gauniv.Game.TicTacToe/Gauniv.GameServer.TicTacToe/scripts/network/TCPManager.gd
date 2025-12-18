extends Node

signal connected_to_server()
signal disconnected_from_server()
signal message_received(message: Dictionary)
signal connection_failed()

var tcp_client: StreamPeerTCP
var is_connected: bool = false
var GameMessages
var MessagePackEncoder

func _ready():
	tcp_client = StreamPeerTCP.new()
	GameMessages = preload("res://scripts/network/GameMessages.gd")
	MessagePackEncoder = preload("res://scripts/network/MessagePackEncoder.gd")

# Se connecter au serveur
func connect_to_server(host: String, port: int) -> bool:
	print("[TCPManager] Tentative de connexion à ", host, ":", port)
	
	# Si déjà connecté, pas besoin de se reconnecter
	if is_connected and tcp_client.get_status() == StreamPeerTCP.STATUS_CONNECTED:
		print("[TCPManager] Déjà connecté!")
		return true
	
	# Réinitialiser le client TCP seulement si en erreur
	var current_status = tcp_client.get_status()
	if current_status == StreamPeerTCP.STATUS_ERROR:
		print("[TCPManager] Socket en erreur, création d'un nouveau client")
		tcp_client = StreamPeerTCP.new()
	elif current_status != StreamPeerTCP.STATUS_NONE:
		print("[TCPManager] Déconnexion du socket existant (status: ", current_status, ")")
		tcp_client.disconnect_from_host()
		await get_tree().create_timer(0.1).timeout
		tcp_client = StreamPeerTCP.new()
	
	var error = tcp_client.connect_to_host(host, port)
	
	if error != OK:
		print("[TCPManager] Erreur de connexion: ", error)
		emit_signal("connection_failed")
		return false
	
	# Attendre que la connexion soit établie (max 10 secondes)
	var timeout = 10.0
	var elapsed = 0.0
	while tcp_client.get_status() == StreamPeerTCP.STATUS_CONNECTING and elapsed < timeout:
		tcp_client.poll()  # IMPORTANT: forcer la mise à jour du statut
		await get_tree().create_timer(0.1).timeout
		elapsed += 0.1
		if int(elapsed * 10) % 10 == 0:  # Log toutes les secondes
			print("[TCPManager] Connexion en cours... (", elapsed, "s) - Status: ", tcp_client.get_status())
	
	var final_status = tcp_client.get_status()
	print("[TCPManager] Status final: ", final_status)
	
	if final_status == StreamPeerTCP.STATUS_CONNECTED:
		is_connected = true
		print("[TCPManager] Connecté au serveur!")
		emit_signal("connected_to_server")
		return true
	else:
		print("[TCPManager] Échec de connexion (timeout ou erreur)")
		print("[TCPManager] Status détails - NONE: 0, CONNECTING: 1, CONNECTED: 2, ERROR: 3")
		emit_signal("connection_failed")
		return false

# Déconnecter
func disconnect_from_server():
	if tcp_client:
		tcp_client.disconnect_from_host()
	is_connected = false
	emit_signal("disconnected_from_server")
	print("[TCPManager] Déconnecté du serveur")

# Envoyer un message au format MessagePack
func send_message(message: Array):
	if not is_connected or tcp_client.get_status() != StreamPeerTCP.STATUS_CONNECTED:
		print("[TCPManager] Erreur: pas connecté")
		return
	
	# Encoder le message en MessagePack (array format)
	var data = MessagePackEncoder.encode_value(message)
	
	print("[TCPManager] Message à envoyer - Type: ", GameMessages.get_message_type_name(message[0]))
	print("[TCPManager] Message array: ", message)
	print("[TCPManager] Taille des données: ", data.size(), " bytes")
	print("[TCPManager] Data hex: ", data.hex_encode())
	
	# Envoyer la taille (4 bytes)
	var length = data.size()
	var length_bytes = PackedByteArray()
	length_bytes.resize(4)
	length_bytes.encode_s32(0, length)
	
	var result1 = tcp_client.put_data(length_bytes)
	var result2 = tcp_client.put_data(data)
	
	if result1 != OK or result2 != OK:
		print("[TCPManager] Erreur lors de l'envoi: ", result1, ", ", result2)
	else:
		print("[TCPManager] Message envoyé avec succès")

# Vérifier et recevoir les messages (à appeler dans _process)
func poll_messages():
	if not is_connected:
		return
	
	# Forcer la mise à jour du statut
	tcp_client.poll()
	
	var status = tcp_client.get_status()
	if status != StreamPeerTCP.STATUS_CONNECTED:
		if is_connected:
			is_connected = false
			print("[TCPManager] Déconnexion détectée")
			emit_signal("disconnected_from_server")
		return
	
	var available = tcp_client.get_available_bytes()
	if available >= 4:
		# Lire la taille du message
		var length_bytes = tcp_client.get_data(4)
		if length_bytes[0] != OK:
			return
		
		var length = length_bytes[1].decode_s32(0)
		
		# Attendre d'avoir toutes les données
		if tcp_client.get_available_bytes() >= length:
			var data_result = tcp_client.get_data(length)
			if data_result[0] == OK:
				# Décoder le MessagePack
				var message = MessagePackEncoder.decode_message(data_result[1])
				
				print("[TCPManager] Message décodé - type: ", typeof(message), ", contenu: ", message)
				
				if message != null:
					var msg_type = GameMessages.get_message_type(message)
					print("[TCPManager] Message reçu: ", GameMessages.get_message_type_name(msg_type))
					emit_signal("message_received", message)
				else:
					print("[TCPManager] Erreur: message invalide ou non-décodable")

func _process(_delta):
	if is_connected:
		poll_messages()

# === Fonctions helper pour créer et envoyer des messages ===

func send_player_connect(player_name: String):
	var message = GameMessages.create_player_connect(player_name, "")
	send_message(message)

func send_create_game(game_name: String, character: String):
	var message = GameMessages.create_create_game(game_name, character)
	send_message(message)

func send_join_game(game_id: String, character: String):
	var message = GameMessages.create_join_game(game_id, character)
	send_message(message)

func send_list_games():
	var message = GameMessages.create_list_games()
	send_message(message)

func send_make_move(player_id: String, position: int):
	var message = GameMessages.create_make_move(player_id, position)
	send_message(message)

func send_sync_game_state(player_id: String):
	var message = GameMessages.create_sync_game_state(player_id)
	send_message(message)

func send_disconnect(player_id: String):
	var message = GameMessages.create_player_disconnect(player_id)
	send_message(message)
