using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

record Finding(string Id, string Title, string Category, string Path, int Line, string Excerpt, string? Project = null, string? Subject = null, Upgrade? Upgrade = null);
record Scan(string Name, int Files, List<Finding> Findings, List<ProjectInfo>? Projects = null);
record ProjectInfo(string Path, string Kind);

class ScanLimitException(string message) : Exception(message) { }

static class Scanner
{
    public static Scan Read(Stream stream, string name, Dictionary<string, FrontendRelease> releases)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 1500) throw new ScanLimitException("Limit the archive to 1,500 entries.");
        var findings = new List<Finding>();
        var projects = new List<ProjectInfo>();
        var entryPoints = new HashSet<string>();
        var mvcFiles = new HashSet<string>();
        var frontend = new FrontendScanner(releases);
        int files = 0;
        long total = 0;
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.Split('/').Any(p => p is "bin" or "obj" or ".git" or "packages" or "node_modules")) continue;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string file = Path.GetFileName(path).ToLowerInvariant();
            bool web = FrontendScanner.Supports(file, ext);
            if (!web && ext is not (".cs" or ".csproj") && file is not ("web.config" or "packages.config")) continue;
            total += entry.Length;
            if (entry.Length > 512_000 || total > 8_000_000) throw new ScanLimitException("Source limit exceeded (512 KB per file, 8 MB total).");
            using var input = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = input.Read(chunk)) > 0)
            {
                if (buffer.Length + read > entry.Length) throw new InvalidDataException("Invalid entry size.");
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            using var reader = new StreamReader(buffer);
            var source = reader.ReadToEnd();
            files++;
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            void Add(string title, string category, int line, string? safeExcerpt = null, string? subject = null)
            {
                if (findings.Count >= 300) throw new ScanLimitException("More than 300 findings. Upload a smaller project.");
                var excerpt = safeExcerpt ?? lines[line-1].Trim();
                findings.Add(new($"F{findings.Count+1:D3}", title, category, path, line, excerpt[..Math.Min(excerpt.Length, 240)], Subject: subject));
            }
            if (web) frontend.Inspect(path, file, ext, string.Join("\n", lines));
            else if (ext == ".cs")
            {
                var root = CSharpSyntaxTree.ParseText(source).GetRoot();
                foreach (var node in root.DescendantNodes())
                {
                    if (node is MethodDeclarationSyntax method && method.Identifier.ValueText == "Main" &&
                        method.Modifiers.Any(m => m.RawKind == (int)SyntaxKind.StaticKeyword))
                        entryPoints.Add(path);
                    if (node is UsingDirectiveSyntax mvc && mvc.Name?.ToString() == "System.Web.Mvc")
                        mvcFiles.Add(path);
                    int Line() => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    if (node is UsingDirectiveSyntax u && (u.Name?.ToString() == "System.Web" || u.Name?.ToString().StartsWith("System.Web.") == true))
                        Add("Classic ASP.NET dependency", "Framework", Line());
                    if (node is ElementAccessExpressionSyntax e && e.Expression.ToString().Split('.').Last() == "Session")
                        Add("Session state access", "State", Line());
                    if (node is MemberAccessExpressionSyntax m && m.Expression.ToString().Split('.').Last() == "ConfigurationManager")
                    {
                        string? key = null;
                        if (m.Parent is ElementAccessExpressionSyntax access && access.ArgumentList.Arguments.Count == 1 &&
                            access.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                            literal.RawKind == (int)SyntaxKind.StringLiteralExpression && literal.Token.ValueText.Length <= 80)
                            key = literal.Token.ValueText;
                        Add("Legacy configuration access", "Configuration", Line(), subject: key);
                    }
                }
            }
            else
            {
                using var xml = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                var doc = XDocument.Load(xml, LoadOptions.SetLineInfo);
                if (ext == ".csproj") projects.Add(new(path, DetectKind(doc, path)));
                foreach (var el in doc.Descendants())
                {
                    var tag = el.Name.LocalName;
                    var line = ((IXmlLineInfo)el).LineNumber;
                    if (tag == "package" && file == "packages.config") Add("Legacy package reference", "Packages", line, $"{el.Attribute("id")?.Value} {el.Attribute("version")?.Value}");
                    if (tag is "package" or "PackageReference")
                        frontend.Dependency(path, line, el.Attribute("id")?.Value ?? el.Attribute("Include")?.Value, el.Attribute("version")?.Value ?? el.Attribute("Version")?.Value ?? el.Element("Version")?.Value);
                    if (tag == "sessionState") Add("Session configuration", "State", line, "<sessionState … />");
                    if (tag == "TargetFrameworkVersion") Add("Legacy project target", "Framework", line, el.Value);
                }
            }
        }
        if (files == 0) throw new ScanLimitException("No supported .NET or frontend source files found.");
        // Use the nearest project directory. Keep ambiguous ownership explicit.
        var directories = projects.GroupBy(p => Path.GetDirectoryName(p.Path) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First().Path : null, StringComparer.OrdinalIgnoreCase);
        string? Owner(string file)
        {
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return file;
            var dir = Path.GetDirectoryName(file) ?? "";
            while (true)
            {
                if (directories.TryGetValue(dir, out var owner)) return owner;
                if (dir.Length == 0) return null;
                dir = Path.GetDirectoryName(dir) ?? "";
            }
        }
        var mvcProjects = mvcFiles.Select(Owner).ToHashSet();
        var mainProjects = entryPoints.Select(Owner).ToHashSet();
        for (int i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            if (p.Kind is "Unknown" or "Library" && mvcProjects.Contains(p.Path))
                projects[i] = p with {Kind = "MVC web application"};
            else if (p.Kind == "Unknown" && mainProjects.Contains(p.Path))
                projects[i] = p with {Kind = "Console application"};
        }
        foreach (var item in frontend.Complete(Owner))
        {
            if (findings.Count >= 300) throw new ScanLimitException("More than 300 findings. Upload a smaller project.");
            findings.Add(item with {Id = $"F{findings.Count+1:D3}"});
        }
        return new(name, files, findings.Select(f => f with {Project = Owner(f.Path)}).ToList(), projects);
    }

    static string DetectKind(XDocument doc, string path)
    {
        string? Property(string name) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        var output = Property("OutputType")?.ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(path);
        var test = string.Equals(Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase);
        if (test) return output == "exe" ? "Executable test runner" : "Test project";
        if (doc.Root?.Attribute("Sdk")?.Value.StartsWith("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) == true ||
            Property("ProjectTypeGuids")?.Contains("349c5851-65df-11da-9384-00065b846f21", StringComparison.OrdinalIgnoreCase) == true)
            return "Web application";
        return output switch { "exe" => "Console application", "winexe" => "Desktop application", "library" => "Library", _ => "Unknown" };
    }
}
