extends GutTest

var handler

func before_each():
	handler = Node.new()

	var script_res = load("res://scripts/ui/EscapeBackHandler.cs")
	if script_res:
		handler.set_script(script_res)

	add_child(handler)

	# pozwól _Ready() się wykonać
	await get_tree().process_frame


func simulate_esc():
	var ev := InputEventKey.new()
	ev.pressed = true
	ev.echo = false
	ev.keycode = KEY_ESCAPE  # ważne: to ma tę samą wartość co C# Key.Escape
	handler._UnhandledInput(ev)


func test_escape_removes_focus_instead_of_changing_scene_when_input_active():
	var line := LineEdit.new()
	add_child(line)
	line.grab_focus()

	# tu nie sprawdzamy zmiany sceny, tylko to, że ESC zdejmuje focus
	simulate_esc()

	assert_false(line.has_focus())


func test_click_outside_input_removes_focus():
	var line := LineEdit.new()
	add_child(line)
	line.grab_focus()

	var ev := InputEventMouseButton.new()
	ev.pressed = true
	ev.position = Vector2(9999, 9999) # poza inputem
	handler._Input(ev)

	assert_false(line.has_focus())
