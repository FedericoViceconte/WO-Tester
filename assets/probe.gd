extends SceneTree
# External WO adapter. Loaded by Godot with the selected project; never installed in the game.
var phase := "catalog"
var requested_speed := 1
var test_seed := 20261105
var failed := 0
var emitted := 0
var visible := false
var output_dir := ""
var target := ""
var data
var profile
var settings

func _initialize() -> void:
 for argument in OS.get_cmdline_user_args():
  if argument.begins_with("--wo-phase="): phase = argument.get_slice("=", 1)
  if argument.begins_with("--wo-speed="): requested_speed = clampi(int(argument.get_slice("=", 1)), 1, 10)
  if argument.begins_with("--wo-seed="): test_seed = int(argument.get_slice("=", 1))
  if argument.begins_with("--wo-output="): output_dir = argument.trim_prefix("--wo-output=")
  if argument.begins_with("--wo-target="): target = argument.trim_prefix("--wo-target=")
  if argument == "--wo-visible": visible = true
 _run.call_deferred()

func emit_result(id: String, name: String, category: String, ok: bool, detail: String, checks: int = 1) -> void:
 emitted += 1
 if not ok: failed += 1
 print("WO_TESTER_EVENT " + JSON.stringify({"Id": id, "Name": name, "Category": category, "Status": "PASS" if ok else "FAIL", "Severity": "media" if not ok else "", "Detail": detail, "Checks": checks, "Suggestion": "Revisar el contenido indicado y repetir el escenario con la misma semilla." if not ok else ""}))

func _run() -> void:
 await process_frame
 if OS.get_environment("WO_TESTER_ISOLATED") != "1":
  push_error("WO Tester requires its own isolated profile.")
  quit(2)
  return
 data = root.get_node_or_null("GameData")
 profile = root.get_node_or_null("Profile")
 settings = root.get_node_or_null("Settings")
 if data == null or profile == null or settings == null:
  emit_result("adapter:compatibility", "Compatibilidad del adaptador", "Entorno", false, "Faltan GameData, Profile o Settings en el proyecto.")
  quit(2)
  return
 seed(test_seed)
 profile.file_path = OS.get_user_data_dir().path_join("wo-tester-profile.json")
 settings.file_path = OS.get_user_data_dir().path_join("wo-tester-settings.cfg")
 profile.free_play = true
 profile.saw_feeding = true
 settings.music_volume = 0
 settings.sfx_volume = 0
 settings._apply_audio()
 data.practice_match = true
 data.medal_match = false
 data.bot_count = 1
 data.current_mode = 0
 data.game_minutes = 5
 data.turn_seconds = 20
 if phase == "catalog":
  _catalog()
 elif phase == "maps":
  await _maps()
 elif phase == "weapons":
  await _weapons()
 elif phase == "soak":
  await _soak()
 else:
  emit_result("adapter:phase", "Fase desconocida", "Entorno", false, phase)
 print("WO_TESTER_DONE " + JSON.stringify({"phase": phase, "results": emitted, "failed": failed, "speed": requested_speed, "seed": test_seed}))
 quit(0 if failed == 0 else 1)

func _catalog() -> void:
 for type in data.weapons:
  var props: Dictionary = data.weapon(str(type))
  var ok := not props.is_empty()
  for key in ["damage", "Vmax", "gravity", "R1", "R2", "Rdirt", "projectiles"]:
   var number := float(props.get(key, 0))
   ok = ok and is_finite(number)
  emit_result("catalog:weapon:" + str(type), "Definición de arma · " + str(type), "Catálogo", ok, "Propiedades resueltas y valores numéricos finitos. No valida el balance ni el daño de cada combinación.", 8)
 for type in data.pet_names:
  var props: Dictionary = data.pets.get(type, {})
  var ok := not props.is_empty() and float(props.get("hp", 0)) > 0 and float(props.get("size", 0)) > 0
  emit_result("catalog:pet:" + str(type), "Mascota · " + str(type), "Catálogo", ok, "Definición habilitada, vida y tamaño positivos.", 3)
 emit_result("catalog:maps", "Catálogo de mapas habilitados", "Catálogo", not data.map_names.is_empty(), "%d mapas detectados por el juego." % data.map_names.size())

func _battle(map_index: int):
 data.current_map = map_index
 data.practice_match = true
 var scene = load("res://scenes/main.tscn")
 if scene == null: return null
 var battle = scene.instantiate()
 battle.set_physics_process(false)
 root.add_child(battle)
 battle.set_physics_process(false)
 await process_frame
 battle.set_physics_process(false)
 for pet in battle.pets:
  pet.bot = null
 return battle

func _steps(battle, count: int) -> void:
 # Advance the game's original fixed ticks in batches; time_scale stays at 1.
 # This game ignores delta and advances Physical.DT per tick.
 var batch := 1 if visible else requested_speed * 8
 for tick_index in count:
  if not is_instance_valid(battle) or battle.game_over: break
  battle._physics_process(0.01)
  if tick_index % batch == 0:
   await process_frame

func _release(battle) -> void:
 if is_instance_valid(battle): battle.queue_free()
 for unused in 3: await process_frame

func _maps() -> void:
 for index in data.map_names.size():
  var map_name: String = data.map_names[index]
  if target != "" and target != map_name: continue
  seed(test_seed + index)
  var battle = await _battle(index)
  if battle == null:
   emit_result("map:" + map_name, "Mapa · " + map_name, "Mapas", false, "La escena principal no pudo cargarse.")
   continue
  var ok: bool = battle.terrain != null and battle.pets.size() >= 2
  var checks := 2
  if ok:
   await _steps(battle, 160)
   for pet in battle.pets:
    ok = ok and is_finite(pet.X) and is_finite(pet.Y) and is_finite(pet.hp)
    checks += 3
   ok = ok and battle.tick > 0
   checks += 1
  emit_result("map:" + map_name, "Carga y simulación · " + map_name, "Mapas", ok, "Partida real, terreno, jugadores y 160 pasos de simulación. Las rutas y colisiones detalladas se verifican en las pruebas nativas.", checks)
  if visible and output_dir != "":
   await process_frame
   await RenderingServer.frame_post_draw
   root.get_texture().get_image().save_png(output_dir.path_join("map-%02d.png" % index))
  await _release(battle)

func _weapons() -> void:
 var controls = load("res://scripts/game_controls.gd")
 if controls == null:
  emit_result("adapter:weapons", "Catálogo de armas equipables", "Combate", false, "No se encontró GameControls.")
  return
 for type in controls.WEAPONS:
  if target != "" and target != str(type): continue
  if not data.weapon_released(type): continue
  seed(test_seed)
  var battle = await _battle(0)
  if battle == null or battle.pets.is_empty():
   emit_result("fire:" + str(type), "Disparo · " + str(type), "Combate", false, "No se pudo preparar la partida.")
   await _release(battle)
   continue
  await _steps(battle, 120)
  var pet = battle.local_pet()
  var ok: bool = pet != null and not pet.dead
  if ok:
   pet.ammo[type] = 10
   pet.aim_angle = -0.65
   var before: int = int(pet.ammo[type])
   battle.fire(pet, type, 0.7)
   ok = int(pet.ammo.get(type, before)) == before - 1
   await _steps(battle, 120)
   for projectile in battle.projectiles:
    if is_instance_valid(projectile):
     ok = ok and is_finite(projectile.X) and is_finite(projectile.Y)
   for player in battle.pets:
    ok = ok and is_finite(player.hp) and is_finite(player.X) and is_finite(player.Y)
  emit_result("fire:" + str(type), "Disparo y munición · " + str(type), "Combate", ok, "Disparo real con potencia 0.7, consumo de una unidad y 120 pasos posteriores. No certifica todas las trayectorias, impactos ni combinaciones.", 3)
  await _release(battle)

func _soak() -> void:
 seed(test_seed)
 var battle = await _battle(0)
 if battle == null:
  emit_result("soak:match", "Partida prolongada", "Estabilidad", false, "La escena no pudo cargarse.")
  return
 var bots = load("res://scripts/bot.gd")
 for pet in battle.pets:
  if bots != null:
   pet.bot = bots.new(battle, pet)
 var start := Time.get_ticks_msec()
 await _steps(battle, 30000)
 var ok: bool = battle.tick > 0
 for pet in battle.pets:
  ok = ok and is_finite(pet.X) and is_finite(pet.Y) and is_finite(pet.hp)
 emit_result("soak:match", "Partida prolongada · 30.000 pasos", "Estabilidad", ok, "%d pasos ejecutados en %.1f segundos reales; puede finalizar antes si termina la partida." % [battle.tick, (Time.get_ticks_msec() - start) / 1000.0], 4)
 await _release(battle)
