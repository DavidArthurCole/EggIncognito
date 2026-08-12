using System.Reflection;
using System.Runtime.CompilerServices;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EggIncognito.Tests;

public class ApiAccessGuardTests {
    private const byte OpCall = 0x28;
    private const byte OpCallVirt = 0x6F;
    private const byte OpLdcI4 = 0x20;
    private const byte OpLdcI4S = 0x1F;
    private const byte OpLdcI4First = 0x16;
    private const byte OpLdcI4Last = 0x1E;
    private const byte TableMethodDef = 0x06;
    private const byte TableMemberRef = 0x0A;

    private static readonly byte[] ConditionalBranches = [0x2C, 0x2D, 0x39, 0x3A];

    private static readonly HashSet<string> FloorMismatchBaseline = [
        "ApiKeysController",
        "CaptureController",
        "ConfigController",
        "DocsController",
        "ProtoRegistryController",
        "StoredEndpointController"
    ];

    private static readonly int IsAtLeastToken =
        typeof(ICurrentUser).GetMethod(nameof(ICurrentUser.IsAtLeast))!.MetadataToken;

    [Fact]
    public void EveryController_DeclaresApiAccessPolicy() {
        var missing = new List<string>();
        foreach (var t in Controllers()) {
            if (t.GetCustomAttributes(typeof(ApiAccessAttribute), true).Length > 0) continue;

            var actions = Actions(t);
            if (actions.Length > 0 &&
                actions.All(m => m.GetCustomAttributes(typeof(ApiAccessAttribute), true).Length > 0)) {
                continue;
            }

            missing.Add(t.Name);
        }

        Assert.True(missing.Count == 0, "controllers missing [ApiAccess]: " + string.Join(", ", missing));
    }

    [Fact]
    public void ActionPolicy_NeverBelowControllerFloor() {
        var offenders = new List<string>();
        foreach (var t in Controllers()) {
            if (t.GetCustomAttribute<ApiAccessAttribute>(true)?.Level is not { } floor) continue;
            foreach (var a in Actions(t)) {
                if (a.GetCustomAttribute<ApiAccessAttribute>() is { } own && own.Level < floor)
                    offenders.Add($"{t.Name}.{a.Name} declares {own.Level} under controller floor {floor}");
            }
        }

        Assert.True(offenders.Count == 0,
            "action [ApiAccess] may not sit below its controller floor: " + string.Join("; ", offenders));
    }

    [Fact]
    public void DeclaredFloor_MatchesInMethodRoleGate() {
        var offenders = new List<string>();
        foreach (var t in Controllers()) {
            if (FloorMismatchBaseline.Contains(t.Name)) continue;
            foreach (var a in Actions(t)) {
                if (GateOf(t, a) is not { } gate) continue;
                var floor = DeclaredFloor(a);
                if (floor != gate)
                    offenders.Add($"{t.Name}.{a.Name} declares {Name(floor)} but gates on {gate}");
            }
        }

        Assert.True(offenders.Count == 0,
            "declared [ApiAccess] floor must equal the in-method role gate: " + string.Join("; ", offenders));
    }

    [Fact]
    public void RoleGateScanner_StillSeesKnownGates() {
        var devices = typeof(DevicesController);
        var designs = typeof(EnvDesignController);

        Assert.Equal(ApiAccessLevel.Admin, GateOf(devices, devices.GetMethod(nameof(DevicesController.History))!));
        Assert.Equal(ApiAccessLevel.Contributor,
            GateOf(designs, designs.GetMethod(nameof(EnvDesignController.Save))!));
        Assert.Equal(ApiAccessLevel.Contributor,
            GateOf(designs, designs.GetMethod(nameof(EnvDesignController.List))!));
        Assert.Null(GateOf(devices, devices.GetMethod(nameof(DevicesController.Status))!));
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

    private static MethodInfo[] Actions(Type t) =>
        [
            .. t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Length > 0)
        ];

    private static ApiAccessLevel? DeclaredFloor(MethodInfo action) =>
        action.GetCustomAttribute<ApiAccessAttribute>()?.Level
        ?? action.DeclaringType?.GetCustomAttribute<ApiAccessAttribute>(true)?.Level;

    private static ApiAccessLevel? GateOf(Type controller, MethodInfo action) {
        var levels = RolesIn(BodyOf(action), controller, true).Select(LevelFor).ToList();
        return levels.Count == 0 ? null : levels.Min();
    }

    private static List<UserRole> RolesIn(byte[] il, Type controller, bool followHelpers) {
        var found = new List<UserRole>();
        if (il.Length == 0) return found;

        HashSet<int> roleArgHelpers = followHelpers ? RoleArgHelpers(controller) : [];
        Dictionary<int, UserRole> flatHelpers = followHelpers ? FlatHelpers(controller) : [];

        for (int i = 0; i + 5 <= il.Length; i++) {
            if (il[i] is not (OpCall or OpCallVirt)) continue;
            int token = BitConverter.ToInt32(il, i + 1);
            if ((byte)(token >>> 24) is not (TableMethodDef or TableMemberRef)) continue;

            if (token == IsAtLeastToken && GuardsBranch(il, i + 5) && RoleAt(il, i) is { } direct) {
                found.Add(direct);
                continue;
            }

            if (roleArgHelpers.Contains(token) && RoleAt(il, i) is { } passed) {
                found.Add(passed);
                continue;
            }

            if (flatHelpers.TryGetValue(token, out var helperRole)) found.Add(helperRole);
        }

        return found;
    }

    private static HashSet<int> RoleArgHelpers(Type controller) =>
        [
            .. GateShapedMethods(controller)
                .Where(m => m.GetParameters() is [var p] && p.ParameterType == typeof(UserRole))
                .Select(m => m.MetadataToken)
        ];

    private static Dictionary<int, UserRole> FlatHelpers(Type controller) {
        var map = new Dictionary<int, UserRole>();
        foreach (var m in GateShapedMethods(controller).Where(m => m.GetParameters().Length == 0)) {
            var roles = RolesIn(BodyOf(m), controller, false);
            if (roles.Count > 0) map[m.MetadataToken] = roles.Min();
        }

        return map;
    }

    private static IEnumerable<MethodInfo> GateShapedMethods(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public |
                              BindingFlags.DeclaredOnly)
            .Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType));

    private static bool GuardsBranch(byte[] il, int next) =>
        next < il.Length && Array.IndexOf(ConditionalBranches, il[next]) >= 0;

    private static UserRole? RoleAt(byte[] il, int callAt) {
        int? value = ConstBefore(il, callAt);
        return value is { } v && Enum.IsDefined((UserRole)v) ? (UserRole)v : null;
    }

    private static int? ConstBefore(byte[] il, int callAt) {
        if (callAt >= 2 && il[callAt - 2] == OpLdcI4S) return (sbyte)il[callAt - 1];
        if (callAt >= 5 && il[callAt - 5] == OpLdcI4) return BitConverter.ToInt32(il, callAt - 4);
        if (callAt >= 1 && il[callAt - 1] >= OpLdcI4First && il[callAt - 1] <= OpLdcI4Last)
            return il[callAt - 1] - OpLdcI4First;
        return null;
    }

    private static byte[] BodyOf(MethodInfo m) {
        var machine = m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        var move = machine?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(x => x.Name.EndsWith("MoveNext", StringComparison.Ordinal));
        return ((MethodBase?)move ?? m).GetMethodBody()?.GetILAsByteArray() ?? [];
    }

    private static ApiAccessLevel LevelFor(UserRole role) => role switch {
        UserRole.Admin => ApiAccessLevel.Admin,
        UserRole.Contributor => ApiAccessLevel.Contributor,
        _ => ApiAccessLevel.Authenticated
    };

    private static string Name(ApiAccessLevel? level) => level?.ToString() ?? "nothing";
}
