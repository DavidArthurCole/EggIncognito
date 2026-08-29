using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoDiffTests {
    [Fact]
    public void Identical_Protos_Empty() {
        const string p = "message Foo {\n    optional uint32 a = 1;\n}\n";
        Assert.True(ProtoDiff.Compute(p, p).IsEmpty);
        Assert.Equal("", ProtoDiff.Diff(p, p));
    }

    [Fact]
    public void NamespaceOnly_Change_Is_Empty() {
        const string oldP = "message Foo {\n    optional aux.Bar b = 1;\n}\n";
        const string newP = "message Foo {\n    optional Bar b = 1;\n}\n";
        Assert.True(ProtoDiff.Compute(oldP, newP).IsEmpty);
    }

    [Fact]
    public void Qualified_Vs_Relative_Nested_Type_Is_Empty() {
        const string oldP = "message Backup {\n    optional Settings settings = 4;\n    message Settings {\n        optional bool sfx = 1;\n    }\n}\n";
        const string newP = "message Backup {\n    message Settings {\n        optional bool sfx = 1;\n    }\n\n    optional ei.Backup.Settings settings = 4;\n}\n";
        Assert.True(ProtoDiff.Compute(oldP, newP).IsEmpty);
    }

    [Fact]
    public void Different_Scope_Same_Leaf_Parent_Mismatch_Is_Changed() {
        const string oldP = "message Foo {\n    optional ei.Alpha.Status s = 1;\n}\n";
        const string newP = "message Foo {\n    optional ei.Beta.Status s = 1;\n}\n";
        var e = Assert.Single(ProtoDiff.Compute(oldP, newP).Entries);
        var c = Assert.Single(e.FieldChanges);
        Assert.Equal(FieldChangeKind.Changed, c.Kind);
    }

    [Fact]
    public void Added_Field_By_Number() {
        const string oldP = "message Foo {\n    optional uint32 a = 1;\n}\n";
        const string newP = "message Foo {\n    optional uint32 a = 1;\n    optional uint32 b = 2;\n}\n";
        var r = ProtoDiff.Compute(oldP, newP);
        var e = Assert.Single(r.Entries);
        Assert.Equal(MessageDiffKind.Modified, e.Kind);
        var c = Assert.Single(e.FieldChanges);
        Assert.Equal(FieldChangeKind.Added, c.Kind);
        Assert.Equal(2, c.Number);
        Assert.Contains("+    optional uint32 b = 2;", ProtoDiff.Diff(oldP, newP));
        Assert.Contains("@@ message Foo @@", ProtoDiff.Diff(oldP, newP));
    }

    [Fact]
    public void Field_Rename_Same_Number_Is_Changed_Not_AddRemove() {
        const string oldP = "message Foo {\n    optional uint32 old_name = 1;\n}\n";
        const string newP = "message Foo {\n    optional uint32 new_name = 1;\n}\n";
        var e = Assert.Single(ProtoDiff.Compute(oldP, newP).Entries);
        var c = Assert.Single(e.FieldChanges);
        Assert.Equal(FieldChangeKind.Changed, c.Kind);
        Assert.Equal("old_name", c.Old!.Name);
        Assert.Equal("new_name", c.New!.Name);
    }

    [Fact]
    public void Field_Retype_Same_Number_Is_Changed() {
        const string oldP = "message Foo {\n    optional uint32 a = 1;\n}\n";
        const string newP = "message Foo {\n    optional string a = 1;\n}\n";
        var c = Assert.Single(Assert.Single(ProtoDiff.Compute(oldP, newP).Entries).FieldChanges);
        Assert.Equal(FieldChangeKind.Changed, c.Kind);
        Assert.Equal("uint32", c.Old!.Type);
        Assert.Equal("string", c.New!.Type);
    }

    [Fact]
    public void Message_Rename_Detected_By_Field_Similarity() {
        const string oldP = "message FirstContact {\n    optional string user_id = 1;\n    optional uint32 client_version = 2;\n    optional Backup backup = 3;\n}\n";
        const string newP = "message EggIncFirstContactRequest {\n    optional string user_id = 1;\n    optional uint32 client_version = 2;\n    optional Backup backup = 3;\n    optional string device_id = 4;\n}\n";
        var e = Assert.Single(ProtoDiff.Compute(oldP, newP).Entries);
        Assert.Equal(MessageDiffKind.Renamed, e.Kind);
        Assert.Equal("FirstContact", e.OldPath);
        Assert.Equal("EggIncFirstContactRequest", e.NewPath);
        var c = Assert.Single(e.FieldChanges);
        Assert.Equal(FieldChangeKind.Added, c.Kind);
        Assert.Contains("@@ message FirstContact -> EggIncFirstContactRequest @@", ProtoDiff.Diff(oldP, newP));
    }

    [Fact]
    public void Dissimilar_Messages_Are_Add_And_Remove_With_Body() {
        const string oldP = "message Alpha {\n    optional uint32 a = 1;\n}\n";
        const string newP = "message Beta {\n    optional string b = 7;\n    optional string c = 8;\n}\n";
        var r = ProtoDiff.Compute(oldP, newP);
        Assert.Equal(2, r.Entries.Count);
        Assert.Contains(r.Entries, e => e.Kind == MessageDiffKind.Removed && e.OldPath == "Alpha" && e.Body.Count > 0);
        Assert.Contains(r.Entries, e => e.Kind == MessageDiffKind.Added && e.NewPath == "Beta" && e.Body.Count > 0);
    }

    [Fact]
    public void Nested_Children_Match_Inside_Renamed_Parent() {
        const string oldP = "message Outer {\n    optional uint32 x = 1;\n    optional uint32 y = 2;\n    message Inner {\n        optional uint32 a = 1;\n    }\n}\n";
        const string newP = "message OuterRenamed {\n    optional uint32 x = 1;\n    optional uint32 y = 2;\n    message Inner {\n        optional uint32 a = 1;\n        optional uint32 b = 2;\n    }\n}\n";
        var r = ProtoDiff.Compute(oldP, newP);
        Assert.DoesNotContain(r.Entries, e => e.Kind is MessageDiffKind.Added or MessageDiffKind.Removed);
        Assert.Contains(r.Entries, e => e.Kind == MessageDiffKind.Renamed && e.OldPath == "Outer");
        Assert.Contains(r.Entries, e => e.Kind == MessageDiffKind.Modified && e.NewPath == "OuterRenamed.Inner"
            && e.FieldChanges.Count == 1 && e.FieldChanges[0].Kind == FieldChangeKind.Added);
    }

    [Fact]
    public void Enum_Value_Added() {
        const string oldP = "message Foo {\n    enum E {\n        X = 0;\n    }\n}\n";
        const string newP = "message Foo {\n    enum E {\n        X = 0;\n        Y = 1;\n    }\n}\n";
        var e = Assert.Single(ProtoDiff.Compute(oldP, newP).Entries);
        var c = Assert.Single(e.EnumChanges);
        Assert.Equal(FieldChangeKind.Added, c.Kind);
        Assert.Equal("E", c.EnumName);
        Assert.Equal("Y", c.New!.Name);
    }

    [Fact]
    public void Oneof_Members_Are_Fields_Of_Message() {
        const string oldP = "message Foo {\n    oneof pick {\n        uint32 a = 1;\n    }\n}\n";
        const string newP = "message Foo {\n    oneof pick {\n        uint32 a = 1;\n        string b = 2;\n    }\n}\n";
        var e = Assert.Single(ProtoDiff.Compute(oldP, newP).Entries);
        Assert.Single(e.FieldChanges);
    }

    [Fact]
    public void Map_Field_Parses_And_Diffs() {
        const string oldP = "message Foo {\n    map<string, uint32> m = 1;\n}\n";
        const string newP = "message Foo {\n    map<string, uint64> m = 1;\n}\n";
        var c = Assert.Single(Assert.Single(ProtoDiff.Compute(oldP, newP).Entries).FieldChanges);
        Assert.Equal(FieldChangeKind.Changed, c.Kind);
    }
}
