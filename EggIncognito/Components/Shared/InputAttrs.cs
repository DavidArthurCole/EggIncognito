namespace EggIncognito.Components.Shared;

public static class InputAttrs {
    public static readonly IReadOnlyDictionary<string, object> NoAutofill = new Dictionary<string, object> {
        ["autocomplete"] = "off",
        ["data-1p-ignore"] = true,
        ["data-lpignore"] = "true",
        ["data-bwignore"] = true,
        ["data-form-type"] = "other"
    };
}
