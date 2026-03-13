extends SceneTree


func fail(message: String, code: int = 1) -> void:
	push_error(message)
	quit(code)


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 3 or args.size() % 2 == 0:
		fail("usage: godot_pack_mod.gd <output-pck> <res-path> <source-path> [<res-path> <source-path> ...]", 2)
		return

	var output_path := args[0]
	var packer := PCKPacker.new()
	var err := packer.pck_start(output_path)
	if err != OK:
		fail("pck_start failed: %s" % err, err)
		return

	for index in range(1, args.size(), 2):
		var resource_path := args[index]
		var source_path := args[index + 1]
		if not FileAccess.file_exists(source_path):
			fail("source file does not exist: %s" % source_path, 3)
			return

		err = packer.add_file(resource_path, source_path)
		if err != OK:
			fail("add_file failed for %s: %s" % [resource_path, err], err)
			return

	err = packer.flush(false)
	if err != OK:
		fail("flush failed: %s" % err, err)
		return

	print("packed:%s" % output_path)
	quit(0)
