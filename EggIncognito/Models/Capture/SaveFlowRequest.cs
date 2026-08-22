using System.Text.Json.Serialization;

namespace EggIncognito.Models.Capture;

public sealed record SaveFlowRequest([property: JsonRequired] long Id);
