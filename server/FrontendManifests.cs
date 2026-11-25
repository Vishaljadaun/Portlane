using System.Text;
using System.Text.Json;

sealed partial class FrontendScanner
{
    void ReadManifest(string path, string file, string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        try
        {
            var json = new Utf8JsonReader(bytes, new JsonReaderOptions {AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip});
            // Track JSON paths and line numbers in one pass, including minified lockfiles.
            var parents = new List<string>();
            string key = "";
            int line = 1, offset = 0;
            while (json.Read())
            {
                while (offset < json.TokenStartIndex) if (bytes[offset++] == '\n') line++;
                switch (json.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        key = json.GetString()!;
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        parents.Add(key);
                        key = "";
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        parents.RemoveAt(parents.Count-1);
                        key = "";
                        break;
                    case JsonTokenType.String:
                        if ((file is "package.json" or "bower.json") && parents.Count == 2 &&
                            parents[1] is "dependencies" or "devDependencies" or "peerDependencies" or "optionalDependencies")
                            Dependency(path, line, key, json.GetString());
                        if (file == "package-lock.json" && key == "version" && parents.Count >= 3) {
                            var location = parents[^1];
                            if (parents.Count == 3 && parents[1] == "packages" && location.Contains("node_modules/", StringComparison.Ordinal))
                                Dependency(path, line, location[(location.LastIndexOf('/')+1)..], json.GetString());
                            else if (parents[^2] == "dependencies")
                                Dependency(path, line, location, json.GetString());
                        }
                        if (file == "libman.json" && key == "library" && parents.Count == 3 && parents[1] == "libraries") {
                            var spec = json.GetString()!;
                            var at = spec.IndexOf('@');
                            if (at > 0) Dependency(path, line, spec[..at], spec[(at+1)..]);
                        }
                        key = "";
                        break;
                }
            }
        }
        catch (JsonException)
        {
            Issue(path, 1, "Unreadable frontend manifest", "Frontend", file,
                "Correct the manifest syntax and upload again to complete dependency analysis for this file.",
                "Validate the JSON and confirm the dependency references appear in the new scan.", "https://www.json.org/json-en.html");
        }
    }
}
