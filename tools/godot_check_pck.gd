extends SceneTree


func fail(message: String, code: int = 1) -> void:
	push_error(message)
	quit(code)


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		fail("usage: godot_check_pck.gd <pack-path> <resource-path>", 2)
		return

	var pack_path := args[0]
	var resource_path := args[1]
	if not FileAccess.file_exists(pack_path):
		fail("pack file does not exist: %s" % pack_path, 3)
		return

	var loaded := ProjectSettings.load_resource_pack(pack_path, false)
	if not loaded:
		fail("failed to load resource pack: %s" % pack_path, 4)
		return

	if not FileAccess.file_exists(resource_path):
		fail("resource not found in pack: %s" % resource_path, 5)
		return

	print("validated:%s" % resource_path)
	quit(0)
