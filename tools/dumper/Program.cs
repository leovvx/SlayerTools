// dumper: prints properties, methods and enum values of the given classes in an interop assembly.
// Usage: dotnet run -- <Assembly-CSharp.dll> <ClassName> [ClassName...]
using Mono.Cecil;
var mod = ModuleDefinition.ReadModule(args[0]);
foreach (var name in args.Skip(1)) {
  foreach (var t in mod.GetTypes().Where(x => x.Name == name)) {
    Console.WriteLine($"## {t.FullName} : {t.BaseType?.FullName}");
    if (t.IsEnum) foreach (var f in t.Fields.Where(f=>f.IsLiteral)) Console.WriteLine($"  E {f.Name} = {f.Constant}");
    foreach (var p in t.Properties) {
      var g = p.GetMethod; Console.WriteLine($"  P {(g!=null&&g.IsStatic?"static ":"")}{p.PropertyType.FullName} {p.Name}{(p.SetMethod!=null?" {set}":"")}");
    }
    foreach (var m in t.Methods) { if (m.IsSpecialName || m.IsConstructor) continue;
      Console.WriteLine($"  M {(m.IsStatic?"static ":"")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.Parameters.Select(p=>p.ParameterType.Name+" "+p.Name))})"); }
  }
}
