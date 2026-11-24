using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

record Finding(string Id, string Title, string Category, string Path, int Line, string Excerpt);
record Scan(string Name, int Files, List<Finding> Findings);

static class Scanner
{
    public static Scan Read(Stream stream, string name)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 1500) throw new InvalidDataException("Limit the archive to 1,500 entries.");
        var findings = new List<Finding>();
        int files = 0;
        long total = 0;
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.Split('/').Any(p => p is "bin" or "obj" or ".git" or "packages" or "node_modules")) continue;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string file = Path.GetFileName(path).ToLowerInvariant();
            if (ext is not (".cs" or ".csproj") && file is not ("web.config" or "packages.config")) continue;
            total += entry.Length;
            if (entry.Length > 512_000 || total > 8_000_000) throw new InvalidDataException("Source limit exceeded (512 KB per file, 8 MB total).");
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
            void Add(string title, string category, int line, string? safeExcerpt = null)
            {
                if (findings.Count >= 300) throw new InvalidDataException("More than 300 findings. Upload a smaller project.");
                var excerpt = safeExcerpt ?? lines[line-1].Trim();
                findings.Add(new($"F{findings.Count+1:D3}", title, category, path, line, excerpt[..Math.Min(excerpt.Length, 240)]));
            }
            if (ext == ".cs")
            {
                var root = CSharpSyntaxTree.ParseText(source).GetRoot();
                foreach (var node in root.DescendantNodes())
                {
                    int Line() => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    if (node is UsingDirectiveSyntax u && (u.Name?.ToString() == "System.Web" || u.Name?.ToString().StartsWith("System.Web.") == true))
                        Add("Classic ASP.NET dependency", "Framework", Line());
                    if (node is ElementAccessExpressionSyntax e && e.Expression.ToString().Split('.').Last() == "Session")
                        Add("Session state access", "State", Line());
                    if (node is MemberAccessExpressionSyntax m && m.Expression.ToString().Split('.').Last() == "ConfigurationManager")
                        Add("Legacy configuration access", "Configuration", Line());
                }
            }
            else
            {
                using var xml = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                var doc = XDocument.Load(xml, LoadOptions.SetLineInfo);
                foreach (var el in doc.Descendants())
                {
                    var tag = el.Name.LocalName;
                    var line = ((IXmlLineInfo)el).LineNumber;
                    if (tag == "package" && file == "packages.config") Add("Legacy package reference", "Packages", line, $"{el.Attribute("id")?.Value} {el.Attribute("version")?.Value}");
                    if (tag == "sessionState") Add("Session configuration", "State", line, "<sessionState … />");
                    if (tag == "TargetFrameworkVersion") Add("Legacy project target", "Framework", line, el.Value);
                }
            }
        }
        if (files == 0) throw new InvalidDataException("No supported C# or project files found.");
        return new(name, files, findings);
    }
}
