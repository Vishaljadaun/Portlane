record MigrationTask(string Id, string Project, string Kind, string Title, string Action, string[] Checks, string[] Evidence);

static class MigrationTasks
{
    public static List<MigrationTask> Build(Scan scan)
    {
        var projects = (scan.Projects ?? []).ToDictionary(p => p.Path, p => p.Kind);
        var tasks = new List<MigrationTask>();
        foreach (var group in scan.Findings.GroupBy(f => (f.Project, f.Title)))
        {
            var project = group.Key.Project;
            var kind = project != null && projects.TryGetValue(project, out var value) ? value : "Unknown";
            var scope = project == null ? group.First().Path : Path.GetFileNameWithoutExtension(project);
            var role = kind switch {
                "Console application" => "Preserve the console entry point and command-line behavior.",
                "Executable test runner" => "Preserve the executable test runner and its exit status.",
                "Library" => "Preserve the library's public API for its callers.",
                "Desktop application" => "Preserve the desktop entry point and window behavior.",
                "Unknown" => "Confirm the project type before changing its hosting model.",
                _ => $"Preserve the existing {kind.ToLowerInvariant()} behavior."
            };
            var verify = kind switch {
                "Console application" => "Run the console application with representative input and compare its output.",
                "Executable test runner" => "Run the test executable and compare its results and exit code.",
                "Library" => "Build its dependent projects and exercise the library through its callers.",
                _ => "Run existing behavior checks for the project."
            };
            var keys = group.Select(f => f.Subject).OfType<string>().Distinct().ToArray();
            var settings = keys.Length == 0 ? "the referenced settings" : string.Join(", ", keys);
            var (title, action, check, other) = group.Key.Title switch {
                "Legacy project target" => ("Retarget to .NET 10", $"Review the project file, update its target and resolve build incompatibilities. {role}", verify, "Build for .NET 10 and run the project's existing verification steps."),
                "Legacy configuration access" => ("Preserve configuration reads", $"Trace {settings} to the consuming code. Keep the configured values and defaults working when migrating configuration access.", $"Test explicit overrides and defaults for {settings}.", $"Exercise the operations that consume {settings} and compare their results."),
                "Session state access" or "Session configuration" => ("Migrate session behavior", "Trace the evidenced session access or configuration and preserve its lifetime and stored values.", "Check a new session, an existing session and session expiry.", "Compare state before and after a session expires."),
                "Legacy package reference" => ("Review package compatibility", "Check the listed package references for .NET 10 support and validate any necessary replacements.", "Restore packages and build the affected project.", "Exercise the behavior that uses the affected packages."),
                "Classic ASP.NET dependency" when kind == "MVC web application" => ("Port MVC dependencies", "Review the identified MVC dependency. Port the affected controller behavior to ASP.NET Core MVC.", "Compare the migrated controller response with the existing behavior.", "Verify the affected action's routing and response."),
                "Classic ASP.NET dependency" => ("Review System.Web usage", "Trace the referenced System.Web APIs and select compatible replacements for the APIs actually used.", "Build and exercise the affected code paths.", "Compare behavior at the cited call sites."),
                _ => ("Review migration finding", "Inspect the cited source before selecting a migration change.", verify, "Verify the behavior at the cited location.")
            };
            tasks.Add(new($"T{tasks.Count+1}", project ?? group.First().Path, kind, $"{scope} · {title}", action, [check, other], group.Select(f=>f.Id).ToArray()));
        }
        return tasks;
    }
}
