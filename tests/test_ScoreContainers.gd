extends GutTest

var handler
var label
var diode

func before_each():
	# Utwórz instancję C# przez set_script
	handler = PanelContainer.new()
	var script_res = load("res://scripts/ui/ScoreContainer.cs")
	handler.set_script(script_res)

	# Utwórz zależności eksportowane w C#
	label = Label.new()
	diode = ColorRect.new()

	# Podłącz do handlera
	handler.scoreLabel = label
	handler.diode = diode

	# Dodaj do drzewa
	add_child(handler)

	# Pozwól wykonać _Ready()
	await get_tree().process_frame


func test_change_score_text_updates_label():
	handler.ChangeScoreText("42")
	assert_eq(label.text, "42")


func test_set_diode_on_sets_visible_true():
	diode.visible = false

	handler.SetDiodeOn()

	assert_true(diode.visible)


func test_set_diode_off_sets_visible_false():
	diode.visible = true

	handler.SetDiodeOff()

	assert_false(diode.visible)
