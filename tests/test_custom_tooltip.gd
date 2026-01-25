extends GutTest

var tooltip: PanelContainer

func before_all():
	gut.p("CustomTooltip Tests")

func before_each():
	tooltip = PanelContainer.new()
	
	var script_res = load("res://scripts/ui/CustomTooltip.cs")
	if script_res:
		tooltip.set_script(script_res)
	
	var lbl = Label.new()
	lbl.name = "Label"
	tooltip.add_child(lbl)
	
	add_child(tooltip)
	
	await get_tree().create_timer(0.5).timeout

func after_each():
	if tooltip:
		tooltip.queue_free()

func test_structure():
	var lbl = tooltip.get_node_or_null("Label")
	assert_true(lbl != null)
	gut.p("Label znaleziony OK")

func test_show_sets_label_text():
	var lbl: Label = tooltip.get_node("Label")
	
	tooltip.Show("Test text")
	
	assert_eq(lbl.text, "Test text")

func test_hide_makes_invisible():
	tooltip.Show("Test")
	tooltip.Hide()
	
	assert_false(tooltip.visible)

func test_initially_invisible():
	assert_false(tooltip.visible)

func test_multiple_show_last_wins():
	var lbl: Label = tooltip.get_node("Label")
	
	tooltip.Show("Pierwszy")
	tooltip.Show("Drugi")
	tooltip.Show("Trzeci")
	
	assert_eq(lbl.text, "Trzeci")

func test_show_after_hide():
	var lbl: Label = tooltip.get_node("Label")
	
	tooltip.Show("First")
	tooltip.Hide()
	tooltip.Show("Second")
	
	assert_eq(lbl.text, "Second")

func test_on_show_timer_timeout_shows_tooltip():
	tooltip.visible = false
	
	tooltip.call("OnShowTimerTimeout")
	
	assert_true(tooltip.visible)

func test_update_size_resets_dimensions():
	var lbl: Label = tooltip.get_node("Label")
	
	tooltip.size = Vector2(300, 150)
	tooltip.custom_minimum_size = Vector2(100, 50)
	lbl.size = Vector2(80, 40)
	
	tooltip.call("UpdateSize")
	
	assert_eq(tooltip.custom_minimum_size, Vector2.ZERO)

func test_update_position_follows_mouse():
	tooltip.visible = true
	tooltip.position = Vector2.ZERO 
	
	get_viewport().size = Vector2(1920, 1080)
	
	tooltip.call("UpdatePosition")
	
	assert_ne(tooltip.position, Vector2.ZERO)

func test_show_calls_update_size():
	var lbl: Label = tooltip.get_node("Label")
	lbl.text = "Długi tekst do zmiany rozmiaru"
	
	var old_size = tooltip.size
	
	tooltip.Show("Nowy tekst")
	
	await get_tree().process_frame
	assert_ne(tooltip.size, old_size)
