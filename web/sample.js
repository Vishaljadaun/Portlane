const example = {
  "name": "Storefront",
  "files": 4,
  "findings": [
    {
      "id": "F001",
      "title": "Classic ASP.NET dependency",
      "category": "Framework",
      "path": "Controllers/OrdersController.cs",
      "line": 1,
      "excerpt": "using System.Web.Mvc;"
    },
    {
      "id": "F002",
      "title": "Session state access",
      "category": "State",
      "path": "Controllers/OrdersController.cs",
      "line": 10,
      "excerpt": "var owner = Session[\"UserId\"];"
    },
    {
      "id": "F003",
      "title": "Legacy configuration access",
      "category": "Configuration",
      "path": "Controllers/OrdersController.cs",
      "line": 11,
      "excerpt": "var region = ConfigurationManager.AppSettings[\"Region\"];"
    },
    {
      "id": "F004",
      "title": "Legacy project target",
      "category": "Framework",
      "path": "Storefront.csproj",
      "line": 3,
      "excerpt": "v4.8"
    },
    {
      "id": "F005",
      "title": "Legacy package reference",
      "category": "Packages",
      "path": "packages.config",
      "line": 2,
      "excerpt": "Microsoft.AspNet.Mvc 5.2.9"
    },
    {
      "id": "F006",
      "title": "Session configuration",
      "category": "State",
      "path": "web.config",
      "line": 3,
      "excerpt": "<sessionState \u2026 />"
    }
  ]
};
const examplePlan = {
  "phases": [
    {
      "title": "Establish the Core host",
      "action": "Set up the .NET 10 MVC host and check compatibility of the legacy MVC package before porting the controller.",
      "test": "Resolve the new route and verify the controller response.",
      "evidence": [
        "F001",
        "F004",
        "F005"
      ]
    },
    {
      "title": "Port session behavior",
      "action": "Trace the UserId write path and preserve the intended expiry behavior when configuring session.",
      "test": "Exercise a fresh session, an existing session, and expiry.",
      "evidence": [
        "F002",
        "F006"
      ]
    },
    {
      "title": "Move application settings",
      "action": "Supply Region through ASP.NET Core configuration and inject it into the orders controller.",
      "test": "Check Region overrides across environments.",
      "evidence": [
        "F003"
      ]
    }
  ]
};
