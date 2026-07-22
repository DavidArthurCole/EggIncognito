using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Tests.ProtoExtract;

public class CapstoneSpikeTests {
    [Fact]
    public void Capstone_Disassembles_Arm64_Nop() {

        byte[] code = [0x1F, 0x20, 0x03, 0xD5];
        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        var insns = cs.Disassemble(code, 0x1000);
        Assert.Single(insns);
        Assert.Equal("nop", insns[0].Mnemonic);
    }
}
