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

## Frontend dependencies
React and ReactDOM 18.3.1 (MIT) and HTM 3.1.1 (Apache-2.0) are bundled under web/vendor. The frontend runs without an npm install or runtime CDN.
