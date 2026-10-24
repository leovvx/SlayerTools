// interopscan: lists the Unity / game methods called by a plugin DLL that would throw MissingMethodException
// at runtime because their interop body uses Il2CppSystem.Span / ReadOnlySpan (e.g. ColorUtility.TryParseHtmlString).
// Usage: dotnet run -- <BepInEx\interop folder> <SlayerTools.dll>
using Mono.Cecil; using Mono.Cecil.Cil;
// args[0] = interop dir, args[1] = plugin dll
var dir = args[0];
var bad = new HashSet<string>();      // methods whose body uses Il2CppSystem Span/ReadOnlySpan pinnable refs
var bodies = new Dictionary<string, MethodDefinition>();
foreach (var f in Directory.GetFiles(dir, "*.dll")) {
  ModuleDefinition m; try { m = ModuleDefinition.ReadModule(f); } catch { continue; }
  foreach (var t in m.GetTypes()) foreach (var md in t.Methods) {
    if (!md.HasBody) continue;
    var key = md.FullName; bodies[key] = md;
    foreach (var ins in md.Body.Instructions)
      if (ins.Operand is MethodReference mr && (mr.DeclaringType.FullName.Contains("Span`1") && mr.DeclaringType.FullName.StartsWith("Il2CppSystem"))) { bad.Add(key); break; }
  }
}
// propagate 3 levels
for (int lvl = 0; lvl < 3; lvl++) foreach (var kv in bodies) { if (bad.Contains(kv.Key)) continue;
  foreach (var ins in kv.Value.Body.Instructions) if (ins.Operand is MethodReference mr && bad.Contains(mr.FullName)) { bad.Add(kv.Key); break; } }
var plugin = ModuleDefinition.ReadModule(args[1]);
var hits = new SortedSet<string>();
foreach (var t in plugin.GetTypes()) foreach (var md in t.Methods) if (md.HasBody)
  foreach (var ins in md.Body.Instructions) if (ins.Operand is MethodReference mr && bad.Contains(mr.FullName)) hits.Add(mr.FullName + "   <- " + t.Name + "." + md.Name);
Console.WriteLine($"risky interop methods: {bad.Count}");
foreach (var h in hits) Console.WriteLine(h);
