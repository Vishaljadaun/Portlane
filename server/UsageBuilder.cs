using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

record UsageEdge(string From, int Line, string Relation, string To, string Confidence);
record UsageMap(List<UsageEdge> Edges, bool Truncated);
sealed class UsageBuilder
{
    record Link(string From, int Line, string Relation, string Target, string Kind);
    const int Limit = 2000;
    readonly List<Link> links = [];
    bool truncated;
    void Add(string path, int line, string relation, string target, string kind)
    {
        if (links.Count >= Limit) { truncated = true; return; }
        if (target.Length is > 0 and <= 500) links.Add(new(path, line, relation, target, kind));
    }

    public void Asset(string path, int line, string target)
    {
        target = target.Split('?', '#')[0];
        if (target.StartsWith("//")) target = "https:" + target;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") {
            Add(path, line, "References CDN asset", uri.Scheme + "://" + uri.Authority + uri.AbsolutePath, "external");
        } else if (!target.Contains('@') && !target.Contains(':'))
            Add(path, line, "References asset", target, "asset");
    }

    public void Package(string path, int line, string name, string spec, string scope) =>
        Add(path, line, "Identifies " + name + " " + spec, $"{name} · {(scope.Length == 0 ? "archive root" : scope)}", "package");

    static readonly Regex Razor = new("""(?:\bLayout\s*=\s*"(?<layout>[^"]+)"|\b(?:Scripts|Styles)\.Render\s*\(\s*"(?<bundle>[^"]+)"|\b(?:PartialAsync|Partial|RenderPartial)\s*\(\s*"(?<partial>[^"]+)"|<partial\s+name\s*=\s*"(?<partial>[^"]+)")""", RegexOptions.NonBacktracking);
    readonly HashSet<string> explicitLayouts = [];
    public void Views(string path, string source, Func<int,int> line)
    {
        if (Regex.IsMatch(source, @"\bLayout\s*=")) explicitLayouts.Add(path);
        foreach (Match m in Razor.Matches(source)) {
            var kind = m.Groups["bundle"].Success ? "bundle" : m.Groups["layout"].Success ? "layout" : "partial";
            Add(path, line(m.Index), kind switch {"bundle"=>"Renders bundle","layout"=>"Uses layout",_=>"Renders partial"}, m.Groups[kind].Value, kind);
        }
    }

    public void Bundles(string path, SyntaxNode root)
    {
        static string? Literal(ExpressionSyntax? expression) => (expression as LiteralExpressionSyntax)?.Token.Value as string;
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (call.Expression is not MemberAccessExpressionSyntax member || member.Name.Identifier.Text != "Include" ||
                member.Expression is not ObjectCreationExpressionSyntax creation || creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not {} arg ||
                Literal(arg) is not {} bundle || !(creation.Type.ToString().EndsWith("ScriptBundle") || creation.Type.ToString().EndsWith("StyleBundle"))) continue;
            Add(path, arg.GetLocation().GetLineSpan().StartLinePosition.Line+1, "Defines bundle", bundle, "definition");
            foreach (var item in call.ArgumentList.Arguments)
                if (Literal(item.Expression) is {} asset)
                    Add(path, item.GetLocation().GetLineSpan().StartLinePosition.Line+1, bundle, asset, "include");
        }
    }

    public UsageMap Resolve(HashSet<string> paths, Func<string,string?> owner)
    {
        var edges = new HashSet<UsageEdge>();
        void Edge(string from, int line, string rel, string to, string certainty) {
            if (edges.Count < Limit) edges.Add(new(from,line,rel,to,certainty)); else truncated = true;
        }
        var byScope = paths.GroupBy(p=>owner(p) ?? "").ToDictionary(g=>g.Key,g=>g.ToArray());
        string Root(string p) => Path.GetDirectoryName(owner(p) ?? "") ?? "";
        string Bundle(string p,string target) => $"{owner(p) ?? "archive root"} · {target}";
        static string Join(string a, string b) => (a.Length == 0 ? "" : a+"/")+b;
        foreach (var link in links) {
            var (path,line,rel,target,kind) = link;
            string from = path;
            if (kind is "bundle" or "definition") { Edge(path,line,rel,Bundle(path,target),"Literal"); continue; }
            if (kind is "package" or "external") { Edge(path,line,rel,target,"Literal"); continue; }
            if (kind == "include") { from = Bundle(path,rel); rel = "Includes asset"; }
            var dir = Path.GetDirectoryName(path) ?? "";
            string root = Root(path);
            var scoped = byScope[owner(path) ?? ""];
            string clean = target.TrimStart('~','/').Replace('\\','/');
            if ((kind is "partial" or "layout") && !Path.HasExtension(clean)) clean += ".cshtml";
            var expected = target.StartsWith('/') || target.StartsWith("~/") ? Join(root,clean) : Join(dir,clean);
            var candidates = scoped.Where(p => p == expected || ((kind is "partial" or "layout") && p == Join(root,"Views/Shared/"+clean))).ToArray();
            if (candidates.Length == 0 && clean.Contains("{version}")) {
                var parts = expected.Split("{version}");
                if (parts.Length == 2) candidates = scoped.Where(p => p.StartsWith(parts[0],StringComparison.Ordinal) && p.EndsWith(parts[1],StringComparison.Ordinal)
                    && p.Length > parts[0].Length + parts[1].Length && Version.TryParse(p[parts[0].Length..(p.Length-parts[1].Length)], out _)).ToArray();
            }
            if (candidates.Length == 0 && root.Length == 0 && target.StartsWith("~/"))
                candidates = scoped.Where(p=>p.EndsWith("/"+clean,StringComparison.Ordinal)).ToArray();
            Edge(from,line,rel,candidates.Length == 1 ? candidates[0] : target,
                candidates.Length == 1 ? (expected.Contains("{version}") ? "Version pattern" : "Matched file") : candidates.Length > 1 ? "Ambiguous" : "Unresolved");
        }
        foreach (var path in paths.Where(p=>p.EndsWith(".cshtml",StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).StartsWith('_') && !explicitLayouts.Contains(p))) {
            var dir = Path.GetDirectoryName(path);
            while (dir != null) {
                string start = Join(dir,"_ViewStart.cshtml");
                if (paths.Contains(start) && owner(start)==owner(path)) { Edge(path,0,"May inherit view start",start,"MVC convention"); break; }
                dir = Path.GetDirectoryName(dir);
            }
        }
        return new(edges.OrderBy(e=>e.From,StringComparer.Ordinal).ThenBy(e=>e.Line).ToList(),truncated);
    }
}
