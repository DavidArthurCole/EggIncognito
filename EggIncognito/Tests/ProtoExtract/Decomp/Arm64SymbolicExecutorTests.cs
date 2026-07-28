using EggIncognito.Services.ProtoExtract;
using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class Arm64SymbolicExecutorTests {
    private static uint Fmul(int d, int n, int m) =>
        0x1E200800u | ((uint)(m & 31) << 16) | ((uint)(n & 31) << 5) | (uint)(d & 31);

    private static uint Fadd(int d, int n, int m) =>
        0x1E202800u | ((uint)(m & 31) << 16) | ((uint)(n & 31) << 5) | (uint)(d & 31);

    private static uint FmovImm(int d, uint imm8) => 0x1E201000u | (imm8 << 13) | (uint)(d & 31);
    private static uint Ret() => 0xD65F03C0u;
    private static uint BlRel(long pc, long target) => 0x94000000u | (uint)(((target - pc) >> 2) & 0x03FFFFFF);

    private static byte[] Words(params uint[] w) => [.. w.SelectMany(BitConverter.GetBytes)];

    private static ExprNode RunChain(byte[] code, Dictionary<string, ExprNode> seed,
        Func<string, ExprNode[], ExprNode?> resolve, string resultReg, out int opaque) {
        var syms = new List<MachoSymbols.Symbol>();
        var fn = new MachoSymbols.FuncRange("f", 0, (ulong)code.Length);
        var r = Arm64SymbolicExecutor.Run(code, fn, syms, seed, resolve);
        opaque = r.Opaque;
        return r.Reg(resultReg) ?? new Opaque("unset", []);
    }

    [Fact]
    public void Fmul_Then_Fadd_BuildsBinaryTree() {
        byte[] code = Words(Fmul(2, 0, 1), Fadd(3, 2, 0), Ret());
        var seed = new Dictionary<string, ExprNode> { ["s0"] = new Input("t"), ["s1"] = new Const(2) };
        var res = RunChain(code, seed, (_, _) => null, "s3", out _);
        var b = Assert.IsType<Binary>(ExprNode.Fold(res));
        Assert.Equal(BinOp.Add, b.Op);
    }

    [Fact]
    public void FmovImm_SetsConst() {
        byte[] code = Words(FmovImm(0, 0x70), Ret());
        var res = RunChain(code, [], (_, _) => null, "s0", out _);
        Assert.Equal(1.0, Assert.IsType<Const>(ExprNode.Fold(res)).V, 3);
    }

    [Fact]
    public void UnknownCall_BecomesOpaque() {
        byte[] code = Words(BlRel(0, 0x4000), Ret());
        var res = RunChain(code, [], (_, _) => null, "s0", out int opaque);
        Assert.IsType<Opaque>(res);
        Assert.Equal(1, opaque);
    }


    private static uint MovReg(int d, int m) => 0xAA0003E0u | ((uint)(m & 31) << 16) | (uint)(d & 31);

    private static uint StrS(int t, int n, uint imm) =>
        0xBD000000u | (((imm / 4) & 0xFFF) << 10) | ((uint)(n & 31) << 5) | (uint)(t & 31);

    [Fact]
    public void SretOutParam_CapturesStoredVec3() {
        byte[] code = Words(MovReg(19, 8), FmovImm(1, 0x04), StrS(1, 19, 0), FmovImm(2, 0x70), StrS(2, 19, 4),
            FmovImm(3, 0x16), StrS(3, 19, 8), Ret());
        var syms = new List<MachoSymbols.Symbol>();
        var fn = new MachoSymbols.FuncRange("f", 0, (ulong)code.Length);
        var r = Arm64SymbolicExecutor.Run(code, fn, syms, new Dictionary<string, ExprNode>(),
            new Dictionary<string, string> { ["x8"] = "ret" }, (_, _) => null);
        Assert.Equal(2.5, Assert.IsType<Const>(ExprNode.Fold(r.RetVec[0])).V, 3);
        Assert.Equal(1.0, Assert.IsType<Const>(ExprNode.Fold(r.RetVec[4])).V, 3);
        Assert.Equal(5.5, Assert.IsType<Const>(ExprNode.Fold(r.RetVec[8])).V, 3);
    }

    [Fact]
    public void GcField_NamesStructLoad() {
        static uint LdrS(int t, int n, uint imm) {
            return 0xBD400000u | (((imm / 4) & 0xFFF) << 10) | ((uint)(n & 31) << 5) | (uint)(t & 31);
        }

        byte[] code = Words(LdrS(0, 0, 0x3d8), Ret());
        var syms = new List<MachoSymbols.Symbol>();
        var fn = new MachoSymbols.FuncRange("f", 0, (ulong)code.Length);
        var r = Arm64SymbolicExecutor.Run(code, fn, syms, new Dictionary<string, ExprNode>(),
            new Dictionary<string, string> { ["x0"] = "gc" }, (_, _) => null);
        var f = Assert.IsType<Field>(r.Reg("s0"));
        Assert.Equal("gc", f.Base);
        Assert.Equal(0x3d8, f.Offset);
    }

    [Fact]
    public void KnownCall_AppliesModel() {
        byte[] code = Words(BlRel(0, 0x4000), Ret());
        var seed = new Dictionary<string, ExprNode> { ["s0"] = new Input("t") };

        static ExprNode? resolve(string _, ExprNode[] args) {
            return new Unary(UnOp.Sin, args.Length > 0 ? args[0] : new Const(0));
        }

        var res = RunChain(code, seed, resolve, "s0", out int opaque);
        Assert.Equal(UnOp.Sin, Assert.IsType<Unary>(res).Op);
        Assert.Equal(0, opaque);
    }
}
