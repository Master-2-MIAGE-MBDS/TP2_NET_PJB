extends Node

signal board_updated(game_state: Dictionary)
signal opponent_joined(character: String)
signal game_started()

var http_request: HTTPRequest
var poll_timer: Timer
var is_polling: bool = false
var opponent_already_joined: bool = false

func _ready():
	http_request = HTTPRequest.new()
	add_child(http_request)
	
	poll_timer = Timer.new()
	poll_timer.wait_time = 1.0
	poll_timer.connect("timeout", _on_poll_timeout)
	add_child(poll_timer)

func start_polling():
	is_polling = true
	opponent_already_joined = false
	poll_timer.start()

func stop_polling():
	is_polling = false
	poll_timer.stop()

# Envoyer l'état du plateau au serveur
func send_boardstate(x_pawns: Array, o_pawns: Array, winner):
	var data = {
		"player_id": GameConfig.my_player_id,
		"boardstate": {
			"X_pawns": x_pawns,
			"O_pawns": o_pawns,
			"winner": winner
		}
	}
	
	var json_data = JSON.stringify(data)
	var headers = ["Content-Type: application/json"]
	var url = GameConfig.SERVER_URL + "/games/" + GameConfig.online_game_id + "/boardstate"
	
	http_request.request(url, headers, HTTPClient.METHOD_PUT, json_data)

func _on_poll_timeout():
	if not is_polling or GameConfig.online_game_id == "":
		return
	
	http_request.request_completed.connect(_on_game_state_received, CONNECT_ONE_SHOT)
	http_request.request(GameConfig.SERVER_URL + "/games/" + GameConfig.online_game_id)

func _on_game_state_received(result, response_code, headers, body):
	if response_code != 200:
		return
	
	var json = JSON.parse_string(body.get_string_from_utf8())
	
	if json == null:
		return
	
	# Vérifier si un adversaire a rejoint (une seule fois)
	if not opponent_already_joined and json.has("player_o") and json["player_o"] != null:
		opponent_already_joined = true
		emit_signal("opponent_joined", json["player_o"]["character"])
		emit_signal("game_started")
	
	# Émettre la mise à jour du boardstate
	if json.has("boardstate"):
		emit_signal("board_updated", json["boardstate"])
