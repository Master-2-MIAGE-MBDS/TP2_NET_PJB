extends Control

signal characters_selected(char_p1: String, char_p2: String)
signal back_pressed

@onready var btn_left_p1 = $SelectionP1/ButtonLeftP1
@onready var btn_right_p1 = $SelectionP1/ButtonRightP1
@onready var image_select_p1 = $SelectionP1/ImageSelectP1

@onready var label_p2 = $LabelP2
@onready var selection_p2 = $SelectionP2
@onready var btn_left_p2 = $SelectionP2/ButtonLeftP2
@onready var btn_right_p2 = $SelectionP2/ButtonRightP2
@onready var image_select_p2 = $SelectionP2/ImageSelectP2

@onready var button_start = $ButtonStart
@onready var button_back = $ButtonBack

var index_p1 = 0
var index_p2 = 1
var is_local_mode = true

func _ready():
	btn_left_p1.connect("pressed", _on_navigate.bind(-1, 1))
	btn_right_p1.connect("pressed", _on_navigate.bind(1, 1))
	btn_left_p2.connect("pressed", _on_navigate.bind(-1, 2))
	btn_right_p2.connect("pressed", _on_navigate.bind(1, 2))
	button_start.connect("pressed", _on_start_pressed)
	button_back.connect("pressed", func(): emit_signal("back_pressed"))

func show_for_local():
	is_local_mode = true
	visible = true
	label_p2.visible = true
	selection_p2.visible = true
	button_start.text = "Lancer le débat !"
	_update_display()

func show_for_online():
	is_local_mode = false
	visible = true
	label_p2.visible = false
	selection_p2.visible = false
	button_start.text = "Créer la partie"
	_update_display()

func _on_navigate(direction: int, player: int):
	var characters = GameConfig.CHARACTERS
	
	if player == 1:
		index_p1 = (index_p1 + direction) % characters.size()
		if index_p1 < 0:
			index_p1 = characters.size() - 1
	else:
		index_p2 = (index_p2 + direction) % characters.size()
		if index_p2 < 0:
			index_p2 = characters.size() - 1
	
	_update_display()

func _update_display():
	var characters = GameConfig.CHARACTERS
	image_select_p1.texture = GameConfig.get_character_texture(characters[index_p1])
	
	if selection_p2.visible:
		image_select_p2.texture = GameConfig.get_character_texture(characters[index_p2])

func _on_start_pressed():
	var characters = GameConfig.CHARACTERS
	var char_p1 = characters[index_p1]
	var char_p2 = characters[index_p2] if is_local_mode else ""
	emit_signal("characters_selected", char_p1, char_p2)

func get_selected_character() -> String:
	return GameConfig.CHARACTERS[index_p1]
