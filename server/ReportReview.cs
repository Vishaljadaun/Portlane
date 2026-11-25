using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

record FileCoverage(string Path, string Status, string Reason);
record Assessment(string Impact, string Confidence, string Reason);
record Replacement(string Before, string After);
static class ReportReview
{
    public static string Context(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim())))[..24];

    public static Assessment Assess(Finding f)
    {
        if (f.Title.StartsWith("Multiple "))
            return new("Medium", "Needs review", "Different versions were observed in this scope. Check whether the same page loads them together.");
        if (f.Title.StartsWith("Verify ") || f.Title.StartsWith("Review "))
            return new("Medium", "Needs review", "The declaration or receiver is visible. Its runtime version or type needs confirmation.");
        if (f.Title.StartsWith("Legacy jQuery "))
            return new("High", "High", "A jQuery-shaped call was matched directly. Check its availability in the target release and exercise the calling flow.");
        if (f.Title.StartsWith("Legacy Bootstrap"))
            return new("Medium", "Medium", "This markup matches an older Bootstrap convention. Confirm that Bootstrap controls this element.");
        if (f.Title is "Legacy project target" or "Classic ASP.NET dependency")
            return new("High", "High", "The project target or source import explicitly identifies a dependency that needs review for .NET 10.");
        if (f.Title.StartsWith("Upgrade "))
            return new("Medium", "High", "An exact version was found below the reference release. Assess compatibility before updating.");
        if (f.Title.Contains("Unreadable"))
            return new("High", "High", "A parsing failure prevents complete dependency analysis for this file.");
        return new("Medium", "Medium", "The scanner matched a source or configuration rule. The affected behavior needs verification in the application.");
    }

}
