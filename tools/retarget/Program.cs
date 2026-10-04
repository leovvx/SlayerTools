// retarget: rewrites System.* 8.0.0.0 references to 6.0.0.0 so a net8.0 build loads on BepInEx (.NET 6).
// Usage: dotnet run -- <input.dll> <output.dll>
using Mono.Cecil;
var res = new DefaultAssemblyResolver();
var bepinex = Environment.GetEnvironmentVariable("BEPINEX_DIR") ?? @"C:\Program Files (x86)\Steam\steamapps\common\Idle Slayer\BepInEx";
res.AddSearchDirectory(Path.Combine(bepinex, "interop"));
res.AddSearchDirectory(Path.Combine(bepinex, "core"));
var mod = ModuleDefinition.ReadModule(args[0], new ReaderParameters{InMemory=true, AssemblyResolver=res});
foreach (var r in mod.AssemblyReferences) {
  if (r.Version.Major == 8 && (r.Name.StartsWith("System") || r.Name=="netstandard" || r.Name=="mscorlib")) { r.Version = new Version(6,0,0,0); Console.WriteLine($"{r.Name} -> 6.0.0.0"); }
}
mod.Write(args[1]);
