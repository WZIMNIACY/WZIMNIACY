extends Control

@onready var menu_panel: Panel = $MenuPanel

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	menu_panel.visible = false

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
	
	
func _on_menu_button_pressed() -> void:
	print_debug("Opening menu...")
	menu_panel.visible = true


func _on_menu_background_button_down() -> void:
	print_debug("Menu background clicked; closing menu...")
	menu_panel.visible = false


func _on_quit_button_pressed() -> void:
	pass # Replace with function body.


func _on_pause_button_pressed() -> void:
	pass # Replace with function body.


func _on_settings_button_pressed() -> void:
	pass # Replace with function body.


func _on_help_button_pressed() -> void:
	pass # Replace with function body.


func _on_resume_button_pressed() -> void:
	print_debug("Closing menu...")
	menu_panel.visible = false
