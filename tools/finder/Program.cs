// finder: maps GameAssembly.dll code to method names (LibCpp2IL) and disassembles it (Iced).
// Usage: dotnet run -c Release -- "Type::Method"   -> lists callers
//        DUMP="Type::Method" [LO=hex HI=hex] [ADDR=hex] dotnet run -c Release   -> disassembly
using System.Reflection.PortableExecutable;
using AssetRipper.Primitives;
using Iced.Intel;
using LibCpp2IL;

var gameDir = Environment.GetEnvironmentVariable("IDLE_SLAYER_DIR") ?? @"C:\Program Files (x86)\Steam\steamapps\common\Idle Slayer";
var ga = Path.Combine(gameDir, "GameAssembly.dll");
var meta = Path.Combine(gameDir, "Idle Slayer_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = false;
if (!LibCpp2IlMain.LoadFromFile(ga, meta, UnityVersion.Parse("6000.3.14f1"))) { Console.WriteLine("load failed"); return; }
var md = LibCpp2IlMain.TheMetadata;
var all = new List<(ulong ptr, string name)>();
var targets = new Dictionary<ulong, string>();
string[] watch = args.Length > 0 ? args : new[] { "UnityEngine.Application::Quit" };
foreach (var m in md.methodDefs)
{
    ulong p = m.MethodPointer;
    if (p == 0) continue;
    string n = (m.DeclaringType?.FullName ?? "?") + "::" + m.Name;
    all.Add((p, n));
    foreach (var w in watch) if (n == w) targets[p] = n + "(" + m.parameterCount + ")";
}
all.Sort((a, b) => a.ptr.CompareTo(b.ptr));
Console.WriteLine($"{all.Count} methods; targets: " + string.Join(", ", targets.Select(t => $"{t.Value}@0x{t.Key:X}")));

using var fs = File.OpenRead(ga);
using var pe = new PEReader(fs);
ulong imageBase = pe.PEHeaders.PEHeader.ImageBase;
var text = pe.PEHeaders.SectionHeaders.First(s => s.Name == "il2cpp");
fs.Position = text.PointerToRawData;
var bytes = new byte[text.SizeOfRawData];
fs.ReadExactly(bytes);
ulong textVA = imageBase + (ulong)text.VirtualAddress;
var dec = Decoder.Create(64, new ByteArrayCodeReader(bytes));
dec.IP = textVA;
ulong end = textVA + (ulong)bytes.Length;
string Owner(ulong addr)
{
    int lo = 0, hi = all.Count - 1, best = -1;
    while (lo <= hi) { int mid = (lo + hi) / 2; if (all[mid].ptr <= addr) { best = mid; lo = mid + 1; } else hi = mid - 1; }
    return best < 0 ? "?" : $"{all[best].name}+0x{addr - all[best].ptr:X}";
}
var byPtr = all.GroupBy(a=>a.ptr).ToDictionary(g=>g.Key,g=>g.First().name);
if (Environment.GetEnvironmentVariable("DUMP") is string dm) {
  var mstart = Environment.GetEnvironmentVariable("ADDR") is string ad ? Convert.ToUInt64(ad,16) : all.First(a => a.name == dm).ptr;
  int idx = all.FindIndex(a => a.ptr == mstart); ulong mend = mstart + 0x4000;
  for (int i = idx+1; i < all.Count; i++) if (all[i].ptr > mstart) { mend = all[i].ptr; break; }
  ulong lo = mstart + Convert.ToUInt64(Environment.GetEnvironmentVariable("LO") ?? "0", 16);
  ulong hi = Math.Min(mend, mstart + Convert.ToUInt64(Environment.GetEnvironmentVariable("HI") ?? "4000", 16));
  Console.WriteLine($"== {dm} 0x{mstart:X} size 0x{mend-mstart:X}");
  Dis.Dump(bytes, textVA, lo, hi, Owner, byPtr);
  return;
}
int hits = 0;
while (dec.IP < end)
{
    dec.Decode(out var ins);
    if ((ins.FlowControl == FlowControl.Call || ins.FlowControl == FlowControl.UnconditionalBranch) && ins.Op0Kind == OpKind.NearBranch64)
    {
        if (targets.TryGetValue(ins.NearBranchTarget, out var tn))
        { Console.WriteLine($"{ins.Mnemonic} {tn} from 0x{ins.IP:X} (GA+0x{ins.IP - imageBase:X}) in {Owner(ins.IP)}"); hits++; }
    }
}
Console.WriteLine("hits=" + hits);
