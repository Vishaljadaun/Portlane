using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

record Upgrade(string Action, string Verify, string Guide);
sealed partial class FrontendScanner(Dictionary<string, FrontendRelease> releases)
{
    const string JQueryGuide = "https://jquery.com/upgrade/";
    const string BootstrapGuide = "https://getbootstrap.com/docs/5.3/migration/";
    const string JsGuide = "https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/String/substr";
    record Reference(string Path, int Line, string Package, string Spec, Version? Exact);
    public UsageBuilder Usage { get; } = new();
    readonly HashSet<string> badManifests = [];
    readonly List<Reference> references = [];
    readonly List<Finding> issues = [];
    readonly HashSet<string> manifests = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Scripts = [".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx"];
    static readonly HashSet<string> Markup = [".html", ".htm", ".cshtml", ".razor", ".aspx", ".ascx", ".master"];
    static readonly HashSet<string> JsonFiles = ["package.json", "package-lock.json", "bower.json", "libman.json"];
    public static bool Supports(string file, string ext) => Scripts.Contains(ext) || Markup.Contains(ext) || ext == ".css" || JsonFiles.Contains(file);
    static string Display(string package) => package == "jquery" ? "jQuery" : "Bootstrap";
    static string Guide(string package) => package == "jquery" ? JQueryGuide : BootstrapGuide;
    static Regex Pattern(string value, bool ignoreCase = false) => new(value,
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));

    static readonly Regex Asset = Pattern(@"(?:^|[/@])(?:twitter-)?(?<name>bootstrap|jquery)(?:[/@-]|/v)(?:v)?(?<v>[0-9]+\.[0-9]+\.[0-9]+)(?:/|(?:\.slim|\.bundle|\.min)*\.(?:js|css)(?:[?#]|$))", true);
    static readonly Regex Banner = Pattern(@"/\*![\s*]*(?<name>jQuery(?: JavaScript Library)?|Bootstrap)\s+v?(?<v>[0-9]+\.[0-9]+\.[0-9]+)(?:\s|[|*])", true);
    static readonly Regex Tag = Pattern("""<(?<tag>[A-Za-z][\w:-]*)(?:[^<>"']|"[^"]*"|'[^']*')*>""");
    static readonly Regex Attribute = Pattern("""\s+(?<key>[\w:-]+)\s*=\s*(?:"(?<double>[^"]*)"|'(?<single>[^']*)')""");
    static readonly Regex MaskMarkup = Pattern(@"<!--[\s\S]*?-->|@\*[\s\S]*?\*@|<%--[\s\S]*?--%>");
    static readonly Regex MaskJs = Pattern("""//[^\n]*|/\*[\s\S]*?\*/|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|`(?:\\[\s\S]|[^`\\])*`""");
    static readonly Regex Calls = Pattern(@"(?:^|[^\w$.])(?:jQuery|\$)\s*(?:\.\s*(?<api>trim|parseJSON|now)\s*\(|\([^()\n]*\)\s*\.\s*(?<api>size|bind|unbind|live|die)\s*\()|\.\s*(?<api>substr)\s*\(");
    static readonly Regex BrowserApi = Pattern(@"(?:^|[^\w$.])(?:(?:window\s*\.\s*)?document\s*\.\s*(?<write>write)\s*\(|(?:event|evt|e)\s*\.\s*(?<key>keyCode)\b)");
    static string Blank(Match m) => Regex.Replace(m.Value, @"[^\n]", " ");
    static readonly HashSet<string> Widgets = ["modal","collapse","dropdown","tab","pill","tooltip","popover","button","buttons","carousel","alert"];

    void Issue(string path, int line, string title, string category, string evidence, string action, string verify, string guide, Replacement? preview = null)
    {
        if (issues.Count >= 300) throw new ScanLimitException("More than 300 findings. Upload a smaller project.");
        issues.Add(new("", title, category, path, line, evidence, Upgrade: new(action, verify, guide), Preview: preview));
    }

    public void Dependency(string path, int line, string? name, string? spec)
    {
        name = name?.ToLowerInvariant();
        if (name is not ("jquery" or "bootstrap") || string.IsNullOrWhiteSpace(spec)) return;
        spec = spec.Trim();
        // Avoid retaining registry credentials or local URLs from a dependency declaration.
        if (!Regex.IsMatch(spec, @"^[0-9a-zA-Z.*~^<>=| +\-]{1,64}$")) spec = "(indirect reference)";
        Version.TryParse(spec, out var exact);
        references.Add(new(path, line, name, spec, exact?.Build >= 0 && exact.Revision == -1 ? exact : null));
    }

    public (string, string) InspectWithCoverage(string path, string file, string ext, string source)
    {
        Inspect(path, file, ext, source);
        if (badManifests.Contains(path)) return ("Unreadable", "Invalid dependency manifest");
        if (IsAssetOnly(path, file, ext, source)) return ("Limited", "Asset version metadata only");
        return ("Analyzed", JsonFiles.Contains(file) ? "Dependency declarations checked" : "Source rules checked");
    }

    static bool IsAssetOnly(string path, string file, string ext, string source) =>
        ext == ".css" || Asset.IsMatch(file) || Banner.IsMatch(source[..Math.Min(2048, source.Length)]) ||
        path.Split('/').Contains("vendor", StringComparer.OrdinalIgnoreCase) || file.Contains(".min.") ||
        file is "bootstrap.js" or "jquery.js" or "bootstrap.bundle.js";

    void Inspect(string path, string file, string ext, string source)
    {
        var lines = new List<int> {0};
        for (int i = 0; i < source.Length; i++) if (source[i] == '\n') lines.Add(i+1);
        int Line(int index) { int pos = lines.BinarySearch(index); return pos >= 0 ? pos+1 : ~pos; }
        if (JsonFiles.Contains(file))
        {
            if (file == "package.json") manifests.Add(Path.GetDirectoryName(path) ?? "");
            ReadManifest(path, file, source);
            return;
        }
        var banner = Banner.Match(source[..Math.Min(2048, source.Length)]);
        if (banner.Success) {
            Dependency(path, Line(banner.Index), banner.Groups["name"].Value.Split(' ')[0], banner.Groups["v"].Value);
            return;
        }
        var filename = Asset.Match(file);
        if (filename.Success) {
            Dependency(path, 1, filename.Groups["name"].Value, filename.Groups["v"].Value);
            return;
        }
        bool vendor = path.Split('/').Any(x => x.Equals("vendor", StringComparison.OrdinalIgnoreCase)) ||
            file.Contains(".min.") || file is "bootstrap.js" or "jquery.js" or "bootstrap.bundle.js";
        if (vendor || ext == ".css") return;
        void ReadScript(string script, int offset)
        {
            string code = MaskJs.Replace(script, Blank);
            foreach (Match m in BrowserApi.Matches(code)) {
                bool write = m.Groups["write"].Success;
                var symbol = write ? m.Groups["write"] : m.Groups["key"];
                Issue(path, Line(offset+symbol.Index), write ? "Deprecated document.write()" : "Review keyboard event keyCode", "JavaScript",
                    write ? "document.write()" : "keyCode",
                    write ? "Create the required DOM elements and insert them into a known container. Assign plain text with textContent." :
                        "Confirm the receiver is a KeyboardEvent. Use event.key for the interpreted key or event.code for the physical key.",
                    write ? "Check where and when the content appears, both during page load and after load." :
                        "Exercise keyboard shortcuts on supported layouts and with modifier keys.",
                    write ? "https://developer.mozilla.org/en-US/docs/Web/API/Document/write" : "https://developer.mozilla.org/en-US/docs/Web/API/KeyboardEvent/keyCode");
            }
            foreach (Match m in Calls.Matches(code)) {
                var api = m.Groups["api"];
                var (action, verify) = api.Value switch {
                    "size" => ("Replace the jQuery collection’s .size() call with its .length property.", "Compare counts for zero, one and several matches."),
                    "trim" => ("Replace $.trim(value) with String(value ?? '').trim() to preserve null handling.", "Check null, whitespace and non-string values."),
                    "parseJSON" => ("Replace $.parseJSON(text) with JSON.parse(text) and review error handling.", "Exercise valid, empty and malformed responses."),
                    "now" => ("Replace $.now() with Date.now().", "Confirm callers still receive a numeric timestamp in milliseconds."),
                    "bind" or "unbind" => ("Use .on() for .bind() and .off() for .unbind(). Preserve event namespaces, handlers and data.", "Trigger the event before and after unbinding and check it fires once."),
                    "live" or "die" => ("Attach delegated .on() or .off() to a stable ancestor with the original event and target selector.", "Test existing and dynamically inserted elements, then remove the handler."),
                    _ => ("Confirm the receiver is a string. Replace substr(start, length) with slice after mapping length to an end index and handling negative inputs.", "Compare omitted length, zero length and negative arguments with the old output.")
                };
                Replacement? preview = null;
                if (api.Value is "size" or "now") {
                    var token = api.Value == "size" ? @"\.\s*size\s*\(\s*\)" : @"(?:jQuery|\$)\s*\.\s*now\s*\(\s*\)";
                    var edit = Regex.Match(script[m.Index..Math.Min(script.Length, m.Index+m.Length+80)], token);
                    if (edit.Success && m.Index+edit.Index <= api.Index && api.Index < m.Index+edit.Index+edit.Length)
                        preview = new(edit.Value, api.Value == "size" ? ".length" : "Date.now()");
                }
                bool js = api.Value == "substr";
                Issue(path, Line(offset+api.Index), js ? "Review JavaScript substr()" : $"Legacy jQuery {api.Value}()", js ? "JavaScript" : "jQuery",
                    script.Substring(api.Index, api.Length)+"()", action, verify, js ? JsGuide : JQueryGuide, preview);
            }
        }
        string? jsxCode = null;
        if (Scripts.Contains(ext)) {
            ReadScript(source, 0);
            if (ext is not (".jsx" or ".tsx")) return;
            jsxCode = MaskJs.Replace(source, Blank);
        }
        string markup = MaskMarkup.Replace(source, Blank);
        if (ext == ".cshtml") Usage.Views(path, markup, Line);
        int afterScript = 0;
        foreach (Match tag in Tag.Matches(markup))
        {
            if (tag.Index < afterScript || (jsxCode != null && jsxCode[tag.Index] != '<')) continue;
            var element = tag.Groups["tag"].Value.ToLowerInvariant();
            foreach (Match a in Attribute.Matches(tag.Value))
            {
                var key = a.Groups["key"].Value.ToLowerInvariant();
                var value = a.Groups["double"].Success ? a.Groups["double"] : a.Groups["single"];
                int line = Line(tag.Index+a.Groups["key"].Index);
                if ((element == "script" && key == "src") || (element == "link" && key == "href")) {
                    Usage.Asset(path, line, value.Value);
                    foreach (Match m in Asset.Matches(value.Value))
                        Dependency(path, line, m.Groups["name"].Value, m.Groups["v"].Value);
                }
                if ((key is "data-toggle" or "data-dismiss") && Widgets.Contains(value.Value)) {
                    string attr = key.Replace("data-", "data-bs-");
                    Issue(path, line, "Legacy Bootstrap "+key, "Bootstrap", key+"="+value.Value,
                        $"For Bootstrap 5, rename {key} to {attr}. Update related plugin attributes such as data-target to data-bs-target on this component.",
                        "Exercise the component with mouse and keyboard. Confirm the intended target opens and closes.", BootstrapGuide, new(a.Groups["key"].Value, attr));
                }
                if (key is "class" or "classname")
                    foreach (string token in value.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct())
                        if (token is "sr-only" or "sr-only-focusable" or "float-left" or "float-right") {
                            var next = token switch {"sr-only" => "visually-hidden", "sr-only-focusable" => "visually-hidden-focusable", "float-left" => "float-start", _ => "float-end"};
                            Issue(path, Line(tag.Index+value.Index+value.Value.IndexOf(token, StringComparison.Ordinal)), "Legacy Bootstrap class "+token, "Bootstrap", token,
                                $"If this class comes from Bootstrap, migrate {token} to {next} for Bootstrap 5 and update any custom selectors.",
                                "Check visibility, focus and alignment at the supported viewport widths.", BootstrapGuide, new(token,next));
                        }
            }
            if (element == "script" && jsxCode == null) {
                int start = tag.Index + tag.Length;
                int end = markup.IndexOf("</script", start, StringComparison.OrdinalIgnoreCase);
                if (end >= 0) { ReadScript(markup[start..end], start); afterScript = end; }
            }
        }
    }

    public List<Finding> Complete(Func<string,string?> owner)
    {
        string Scope(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            while (dir != null) {
                if (manifests.Contains(dir)) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return owner(path) ?? "";
        }
        foreach (var group in references.Distinct().GroupBy(r => (Scope: Scope(r.Path), r.Package)))
        {
            var name = Display(group.Key.Package);
            var release = releases[group.Key.Package];
            var versions = group.Where(r=>r.Exact != null).Select(r=>r.Exact!).Distinct().Order().ToArray();
            var verify = group.Key.Package == "jquery" ? "Exercise existing plugins, event handlers and AJAX flows with the development console open." :
                "Verify the loaded CSS and JS versions in the browser, then test dropdowns, modals and responsive layout.";
            foreach (var r in group)
            {
                Usage.Package(r.Path, r.Line, name, r.Spec, group.Key.Scope);
                string evidence = $"{name} {r.Spec}";
                if (r.Exact != null && versions.Length > 1) {
                    string observed = string.Join(", ", versions.Take(12));
                    Issue(r.Path, r.Line, $"Multiple {name} versions referenced", name, evidence,
                        $"Versions observed in this scope: {observed}. Trace which assets each page loads. Align assets that run together and remove duplicate imports.", verify, Guide(r.Package));
                }
                if (r.Exact >= release.Version) continue;
                var step = r.Package == "jquery" ? "Use the jQuery upgrade guides and the matching Migrate plugin for each major upgrade. Resolve warnings before removing Migrate." :
                    "For older major releases, apply each intervening Bootstrap migration guide, then align CSS, JavaScript and plugin dependencies.";
                var action = r.Exact == null ? $"Resolve declaration {r.Spec} against the lockfile or browser assets before choosing an upgrade. " : "";
                Issue(r.Path, r.Line, r.Exact == null ? $"Verify {name} dependency declaration" : $"Upgrade {name} {r.Spec}", name, evidence,
                    $"{action}Stable reference: {release.Version} ({release.Source}). {step}", verify, Guide(r.Package));
            }
        }
        return issues;
    }
}
