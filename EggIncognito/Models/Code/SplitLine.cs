using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Models.Code;

public sealed record SplitLine(DiffRow Row, int Hunk, int LeftIndex, int RightIndex);
