using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;

namespace EggIncognito.Tests.ProtoExtract;

// Guards that the capstone native lib loads + decodes arm64 on this host. If this fails with a native-load
// error the managed binding is unworkable here and MachoArm64Disassembler must fall back to an in-house decoder.
public class CapstoneSpikeTests
{
    [Fact]
    public void Capstone_Disassembles_Arm64_Nop()
    {
        // NOP = 0xD503201F, little-endian on disk: 1F 20 03 D5
        var code = new byte[] { 0x1F, 0x20, 0x03, 0xD5 };
        using var cs = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.LittleEndian);
        var insns = cs.Disassemble(code, 0x1000);
        Assert.Single(insns);
        Assert.Equal("nop", insns[0].Mnemonic);
    }
}
