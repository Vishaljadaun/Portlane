# Portlane
An evidence-linked migration planner for legacy .NET projects targeting .NET 10. Built with C#, Roslyn, SQLite, and React.

## Try the sample
Open web/index.html in a browser for the bundled Storefront report. Search findings, open source evidence, inspect the example plan, and export JSON. The sample plan is hand-authored. Download the sample ZIP to test the scanner when the server is running.

## Run live scans
Install the .NET 10 SDK. From the repository root run:

    dotnet run --project server/Portlane.csproj --urls http://localhost:5100

Open http://localhost:5100, upload web/storefront.zip, and inspect its six findings. The server stores findings, short excerpts, and validated plans in a local SQLite database. Upload and generation are separate actions. For local inference, install Ollama, run ollama pull qwen3.5:9b and start Ollama. Under Migration plan, select Generate migration plan.

For Groq:
    dotnet user-secrets set "AI:GroqApiKey" "YOUR_KEY" --project server
    AI__Provider=Groq ASPNETCORE_ENVIRONMENT=Development dotnet run --project server --urls http://localhost:5100

In PowerShell set $env:AI__Provider="Groq" and $env:ASPNETCORE_ENVIRONMENT="Development" before running dotnet. AI__Model overrides the model name. AI__OllamaUrl overrides the default local Ollama address. Hosted mode sends project roles and paths, task and evidence IDs, configuration key names and allowed migration checks. Source bodies and setting values stay on the server. Provider availability and quotas apply.

## Publish
Build the included Dockerfile and expose container port 8080. Configure Storage__Path on a writable persistent volume for durable reports. Supply model credentials through deployment secrets. Use a private deployment or access-controlled proxy for proprietary source. The unauthenticated single-workspace MVP uses unguessable report IDs and a shared API rate limit. A public portfolio can serve the web directory as an offline sample. Live scanning needs the .NET server.

## Analysis and validation
The scanner inspects C# syntax and project XML without executing the upload. Rules cover System.Web imports, session indexing, ConfigurationManager access, legacy target frameworks, package declarations, and session configuration. Syntactic findings require review, especially when an application defines similarly named symbols. XML excerpts for session configuration omit attributes.

The scanner associates findings with their nearest project and infers project roles from project metadata and source. The planner builds evidence-linked work items, then uses Groq or Ollama to order them and select verification checks. Displayed actions and citations come from the scanned task catalog. Run tests/project-plans.py against a local server to check console and MVC behavior with a mock provider. For migration guidance see https://learn.microsoft.com/aspnet/core/migration/fx-to-core/.

GitHub Actions publishes the .NET application and smoke-tests the React entry point and scanner on pushes and pull requests.

## Frontend migration findings
Upload the solution with its frontend source. Findings include an evidence location, a suggested change, a verification step and a documentation link. All four are available before generating an AI plan and in the JSON export.

Portlane reads JavaScript and TypeScript, HTML, Razor and Web Forms views, npm manifests and npm lockfiles, Bower, LibMan, NuGet declarations, versioned asset paths and recognizable jQuery or Bootstrap distribution banners. It compares exact versions within the nearest npm package or .NET project. A range is a declaration to verify. Multiple versions in one scope warrant checking which scripts and styles actually load together.

The server checks the public npm stable tags for jQuery and Bootstrap up to once every six hours. The finding identifies the reference version and its source. A reviewed release baseline works offline. Set `Frontend__Offline=true` for deterministic offline scans.

Usage rules cover selected jQuery event and utility calls, JavaScript substr candidates and common Bootstrap 5 markup changes. These static checks cannot resolve every alias, template expression or runtime plugin. Dependency directories are excluded. Vendor assets supply version evidence where recognizable. Existing ZIP, source and finding limits apply. A browser run of affected pages remains part of verifying an upgrade.

## Frontend dependencies
React and ReactDOM 18.3.1 (MIT) and HTM 3.1.1 (Apache-2.0) are bundled under web/vendor. The frontend runs without an npm install or runtime CDN.

## Reviewing and comparing scans

Coverage lists each archive file with a reason for its status. Limited files include vendor assets checked for version metadata and C# files with syntax errors. Oversized files and entries beyond the source budget appear as skipped. Unreadable files remain visible. Archive and finding count limits still apply.

Findings have impact, confidence and a rationale. In Dependencies, follow literal Razor layouts, partials, script or style bundles, local assets and scoped package references. MVC view-start conventions and unresolved targets are labeled. Dynamic references, multi-step bundle registration and runtime loading require manual inspection.

Upload a baseline, choose **Compare revised ZIP** and keep the same internal folder structure. Comparison uses rule evidence, project, file and a hash of the original source line. The page tracks the current comparison until refresh. Missing or incompletely scanned files yield unverified findings. Export the report to retain the comparison.

**Explain and preview fix** sends one finding's rule, location, approved replacement fragment, verification guidance and relationship labels to the configured provider. Portlane shows the provider's explanation and assumptions alongside a rule-backed fragment for supported edits. With insufficient edit evidence it requests more context. With an unavailable provider it displays rule guidance. The preview leaves uploaded code untouched.
