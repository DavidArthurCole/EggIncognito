namespace EggIncognito.Core.Services.ProtoExtract.Decomp;

public static class KnownCallModels {
    public static ExprNode? Resolve(string mangled, ExprNode[] args) {
        ExprNode A(int i) {
            return i < args.Length ? args[i] : new ConstExpr(0);
        }

        if (Has(mangled, "sinf") || Has(mangled, "3sinE") || mangled is "_sin") return new Unary(UnOp.Sin, A(0));
        if (Has(mangled, "cosf") || Has(mangled, "3cosE") || mangled is "_cos") return new Unary(UnOp.Cos, A(0));
        if (Has(mangled, "sqrtf") || Has(mangled, "4sqrtE") || mangled is "_sqrt") return new Unary(UnOp.Sqrt, A(0));
        return Has(mangled, "fabsf") || Has(mangled, "_fabs")
            ? new Unary(UnOp.Abs, A(0))
            : Has(mangled, "ParticleBatchedMesh11addParticle") || Has(mangled, "11addParticle")
                ? new Opaque("@sink", [A(0)])
                : (ExprNode?)null;
    }

    private static bool Has(string s, string needle) => s.Contains(needle, StringComparison.Ordinal);
}
