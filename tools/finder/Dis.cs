using Iced.Intel;
using LibCpp2IL;
public static class Dis {
  public static void Dump(byte[] bytes, ulong textVA, ulong from, ulong to, Func<ulong,string> owner, Dictionary<ulong,string> byPtr) {
    var dec = Decoder.Create(64, new ByteArrayCodeReader(bytes, (int)(from - textVA), (int)(to - from)));
    dec.IP = from;
    var f = new IntelFormatter();
    var o = new StringOutput();
    while (dec.IP < to) {
      dec.Decode(out var ins);
      f.Format(ins, o);
      string s = o.ToStringAndReset();
      string extra = "";
      if (ins.Op0Kind == OpKind.NearBranch64 && (ins.FlowControl==FlowControl.Call||ins.FlowControl==FlowControl.UnconditionalBranch) && byPtr.TryGetValue(ins.NearBranchTarget, out var n)) extra = "  ; " + n;
      if (ins.IsIPRelativeMemoryOperand) {
        var a = ins.IPRelativeMemoryAddress;
        try { var lit = LibCpp2IlMain.GetLiteralByAddress(a); if (lit != null) extra += "  ; \"" + lit + "\""; } catch {}
        try { var mm = LibCpp2IlMain.GetMethodDefinitionByGlobalAddress(a); if (mm != null) extra += "  ; M:" + mm.DeclaringType?.Name+"::"+mm.Name; } catch {}
        try { var fd = LibCpp2IlMain.GetFieldGlobalByAddress(a); if (fd != null) extra += "  ; F:" + fd.Name; } catch {}
        try { var tg = LibCpp2IlMain.GetTypeGlobalByAddress(a); if (tg != null) extra += "  ; T:" + tg; } catch {}
      }
      Console.WriteLine($"{ins.IP - from:X5}  {s}{extra}");
    }
  }
}
