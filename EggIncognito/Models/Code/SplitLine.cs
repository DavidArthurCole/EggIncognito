using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Models.Code;

public sealed record SplitLine(DiffRow Row, int Hunk, int LeftIndex, int RightIndex);
