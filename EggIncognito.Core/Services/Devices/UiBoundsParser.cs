namespace EggIncognito.Core.Services.Devices;

public static class UiBoundsParser {
    public static bool TryParse(string s, out UiBounds bounds) {
        bounds = default;
        string[] nums = s.Replace('[', ' ').Replace(']', ' ').Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nums.Length != 4 || !int.TryParse(nums[0], out int l) || !int.TryParse(nums[1], out int t)
            || !int.TryParse(nums[2], out int r) || !int.TryParse(nums[3], out int b)) {
            return false;
        }
        bounds = new UiBounds(l, t, r, b);
        return true;
    }
}
