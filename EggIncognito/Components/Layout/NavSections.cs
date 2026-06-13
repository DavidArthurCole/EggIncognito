namespace EggIncognito.Components.Layout;

// Section name for the per-page toolbar in the top nav. A page pushes controls via SectionContent;
// the nav renders them through an interactive SectionOutlet. The content keeps the page's render mode,
// so click handlers survive the static-layout boundary.
public static class NavSections
{
    public const string Toolbar = "nav-toolbar";
}
