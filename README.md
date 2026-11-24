# CoreWaypoint
An evidence-linked migration planner for classic ASP.NET MVC applications targeting ASP.NET Core on .NET 10. Built with C#, Roslyn, SQLite, and React.

## Try the sample
Open web/index.html in a browser for the bundled Storefront report. Search findings, open source evidence, inspect the example plan, and export JSON. The sample plan is hand-authored. Download the sample ZIP to test the scanner when the server is running.

## Run live scans
Install the .NET 10 SDK. From the repository root run:

    dotnet run --project server/CoreWaypoint.csproj --urls http://localhost:5100

Open http://localhost:5100, upload web/storefront.zip, and inspect its six findings. The server stores findings, short excerpts, and validated plans in a local SQLite database. Upload and generation are separate actions. For local inference, install Ollama, run ollama pull qwen3.5:9b and start Ollama. Under Migration plan, select Generate migration plan.

For Groq:
    dotnet user-secrets set "AI:GroqApiKey" "YOUR_KEY" --project server
    AI__Provider=Groq ASPNETCORE_ENVIRONMENT=Development dotnet run --project server --urls http://localhost:5100

In PowerShell set $env:AI__Provider="Groq" and $env:ASPNETCORE_ENVIRONMENT="Development" before running dotnet. AI__Model overrides the model name. AI__OllamaUrl overrides the default local Ollama address. Hosted mode sends only rule names, category, IDs, file paths and line numbers. Provider availability and quotas apply.

## Publish
Build the included Dockerfile and expose container port 8080. Configure Storage__Path on a writable persistent volume for durable reports. Supply model credentials through deployment secrets. Use a private deployment or access-controlled proxy for proprietary source. The unauthenticated single-workspace MVP uses unguessable report IDs and a shared API rate limit. A public portfolio can serve the web directory as an offline sample. Live scanning needs the .NET server.

## Analysis and validation
The scanner inspects C# syntax and project XML without executing the upload. Rules cover System.Web imports, session indexing, ConfigurationManager access, legacy target frameworks, package declarations, and session configuration. Syntactic findings require review, especially when an application defines similarly named symbols. XML excerpts for session configuration omit attributes.

The planner sends compact evidence and category-specific migration guidance to the selected model. Every phase contains work, a verification check, and known finding IDs. The API rejects malformed or incomplete plans and keeps the scan available for retry. Review recommendations against the original project. For migration guidance see https://learn.microsoft.com/aspnet/core/migration/fx-to-core/.

GitHub Actions publishes the .NET application and smoke-tests the React entry point and scanner on pushes and pull requests.

## Frontend dependencies
React and ReactDOM 18.3.1 (MIT) and HTM 3.1.1 (Apache-2.0) are bundled under web/vendor. The frontend runs without an npm install or runtime CDN.
