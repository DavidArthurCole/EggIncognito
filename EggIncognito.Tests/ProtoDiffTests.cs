using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;


public class ProtoDiffTests
{
    [Fact]
    public void Identical_Protos_Empty_Diff()
    {
        const string p = "message Foo {\n    optional uint32 a = 1;\n}\n";
        Assert.Equal("", ProtoDiff.Diff(p, p));
    }

    [Fact]
    public void NamespaceOnly_Change_Normalizes_To_Empty()
    {
        const string oldP = "message Foo {\n    optional aux.Bar b = 1;\n}\n";
        const string newP = "message Foo {\n    optional Bar b = 1;\n}\n";
        Assert.Equal("", ProtoDiff.Diff(oldP, newP));
    }

    [Fact]
    public void AddedField_Surfaces_As_Plus_Under_Header()
    {
        const string oldP = "message Foo {\n    optional uint32 a = 1;\n}\n";
        const string newP = "message Foo {\n    optional uint32 a = 1;\n    optional uint32 b = 2;\n}\n";
        var diff = ProtoDiff.Diff(oldP, newP);
        Assert.Contains("@@ message Foo @@", diff);
        Assert.Contains("+    optional uint32 b = 2;", diff);
        Assert.DoesNotContain("-    optional uint32 a = 1;", diff);
    }

    [Fact]
    public void Message_Only_In_New_Emits_Its_Lines_As_Plus()
    {
        const string oldP = "message Foo {\n    optional uint32 a = 1;\n}\n";
        const string newP =
            "message Foo {\n    optional uint32 a = 1;\n}\n" +
            "message Bar {\n    optional uint32 z = 9;\n}\n";
        var diff = ProtoDiff.Diff(oldP, newP);
        Assert.Contains("@@ message Bar @@", diff);
        Assert.Contains("+    optional uint32 z = 9;", diff);
        Assert.DoesNotContain("@@ message Foo @@", diff);
    }

    [Fact]
    public void Nested_Message_Path_Uses_Parent_Notation()
    {
        const string oldP = "message Outer {\n    message Inner {\n        optional uint32 a = 1;\n    }\n}\n";
        const string newP = "message Outer {\n    message Inner {\n        optional uint32 a = 1;\n        optional uint32 b = 2;\n    }\n}\n";
        var diff = ProtoDiff.Diff(oldP, newP);
        Assert.Contains("@@ message (Outer.)Inner @@", diff);
        Assert.Contains("+        optional uint32 b = 2;", diff);
    }

    [Fact]
    public void Enum_Is_Content_Of_Enclosing_Message()
    {
        const string oldP = "message Foo {\n    enum E {\n        X = 0;\n    }\n}\n";
        const string newP = "message Foo {\n    enum E {\n        X = 0;\n        Y = 1;\n    }\n}\n";
        var diff = ProtoDiff.Diff(oldP, newP);
       
        Assert.Contains("@@ message Foo @@", diff);
        Assert.DoesNotContain("@@ message E @@", diff);
        Assert.Contains("+        Y = 1;", diff);
    }
}
