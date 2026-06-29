namespace EggIncognito.Services.ProtoExtract.Decomp;

// Maps a called symbol to its IR semantics so the executor can model the math without recursing into the
// callee. libm scalars become Unary nodes; the particle sink (addParticle) returns the special "@sink" Opaque
// carrying its Transform arg so the executor captures the per-particle placement. Anything not listed returns
// null = an honest Opaque leaf upstream. Adding a new effect's calls here is the extension point that replaces
// per-effect manual disasm reading.
public static class KnownCallModels
{
    public static ExprNode? Resolve(string mangled, ExprNode[] args)
    {
        ExprNode A(int i) => i < args.Length ? args[i] : new Const(0);

        if (Has(mangled, "sinf") || Has(mangled, "3sinE") || mangled is "_sin") return new Unary(UnOp.Sin, A(0));
        if (Has(mangled, "cosf") || Has(mangled, "3cosE") || mangled is "_cos") return new Unary(UnOp.Cos, A(0));
        if (Has(mangled, "sqrtf") || Has(mangled, "4sqrtE") || mangled is "_sqrt") return new Unary(UnOp.Sqrt, A(0));
        if (Has(mangled, "fabsf") || Has(mangled, "_fabs")) return new Unary(UnOp.Abs, A(0));

        if (Has(mangled, "ParticleBatchedMesh11addParticle") || Has(mangled, "11addParticle"))
            return new Opaque("@sink", [A(0)]);

        return null;
    }

    private static bool Has(string s, string needle) => s.Contains(needle, StringComparison.Ordinal);
}
