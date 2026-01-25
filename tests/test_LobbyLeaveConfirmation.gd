extends GutTest

var handler
var mock_eos

class MockEOS:
	var isLobbyOwner = false
	var currentLobbyId = ""
	func LeaveLobby(): pass


func before_each():
	handler = Node.new()
	var script_res = load("res://scripts/ui/LobbyLeaveConfirmation.cs")
	handler.set_script(script_res)
	add_child(handler)

	mock_eos = MockEOS.new()
	get_tree().root.set("EOSManager", mock_eos)

	handler.popupSystem = null

	await get_tree().process_frame


func test_show_confirmation_safe_when_no_popup():
	handler.ShowConfirmation()
	assert_true(true) # brak wyjątku = sukces


func test_ready_does_not_crash_without_popup_scene():
	# Usuwamy popupSystem, aby wymusić ścieżkę null
	handler.popupSystem = null

	# Wywołujemy _Ready() ponownie
	handler._Ready()

	assert_true(true) # brak wyjątku = sukces


func test_eosmanager_loaded_from_autoload():
	handler._Ready()
	assert_true(handler.eosManager != null)
