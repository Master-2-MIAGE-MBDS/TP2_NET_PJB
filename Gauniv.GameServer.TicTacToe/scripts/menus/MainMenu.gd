extends Control

signal local_selected
signal online_selected

@onready var btn_local = $ButtonLocal
@onready var btn_online = $ButtonOnline

func _ready():
	btn_local.connect("pressed", _on_local_pressed)
	btn_online.connect("pressed", _on_online_pressed)

func _on_local_pressed():
	emit_signal("local_selected")

func _on_online_pressed():
	emit_signal("online_selected")
