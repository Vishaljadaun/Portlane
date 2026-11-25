"""Exercise the public API with synthetic projects. Pass --live for a provider-backed check."""
import io
import json
import os
import sys
import threading
import urllib.request
import uuid
import zipfile
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

live = "--live" in sys.argv
site = os.getenv("PORTLANE_TEST_URL", "http://127.0.0.1:5100")
requests_seen = []

class Provider(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            body = bytearray()
            while size := int(self.rfile.readline().split(b";")[0].strip(), 16):
                body.extend(self.rfile.read(size))
                self.rfile.read(2)
            self.rfile.readline()
        else:
            body = self.rfile.read(int(self.headers["Content-Length"]))
        payload = json.loads(body)
        tasks = json.loads(payload["messages"][1]["content"])
        requests_seen.append(tasks)
        # Unrecognized tasks and injected prose must never enter the displayed plan.
        steps = [] if isinstance(tasks,dict) else [{"id": "unknown", "check": 0}, {"id": tasks[-1]["Id"], "check": 1,
                 "action": "Install a Razor views project"}]
        body = json.dumps({"message": {"content": json.dumps({"steps": steps} if steps else {"explanation":"This call counts matching items.", "assumption":"Verify the jQuery version."})}}).encode()
        self.send_response(200)
        self.end_headers()
        self.wfile.write(body)

def post(path, body, headers):
    request = urllib.request.Request(site + path, data=body, headers=headers, method="POST")
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.load(response)

def scan(data):
    boundary = uuid.uuid4().hex
    header = f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="sample.zip"\r\nContent-Type: application/zip\r\n\r\n'
    return post("/api/scans", header.encode()+data+f"\r\n--{boundary}--\r\n".encode(),
                {"Content-Type": f"multipart/form-data; boundary={boundary}"})

def plan(result):
    doc = post("/api/scans/"+result["id"]+"/plan", b"", {})
    assert doc["version"] == 2
    found = {f["id"] for f in result["scan"]["findings"]}
    assert {e for p in doc["phases"] for e in p["evidence"]} == found
    assert all(p["action"] and p["test"] for p in doc["phases"])
    return doc["phases"]

def inventory():
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as z:
        for name, kind in [("InventoryTracker.App","Exe"),("InventoryTracker.Core","Library"),("InventoryTracker.Tests","Exe")]:
            z.writestr(f"{name}/{name}.csproj", f"<Project><PropertyGroup><OutputType>{kind}</OutputType><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>")
        z.writestr("InventoryTracker.App/Program.cs", 'class Program { static void Main() { var a=ConfigurationManager.AppSettings["DataFilePath"]; var b=ConfigurationManager.AppSettings["CsvExportPath"]; } }')
        z.writestr("InventoryTracker.Tests/TestRunner.cs", "class Runner { static void Main() {} }")
    return out.getvalue()

def frontend():
    files = {
        "Shop/Web.csproj": "<Project/>",
        "Shop/package.json": '{"dependencies":{"bootstrap":"5.0.0","jquery":"^3.7.1"},"description":"bootstrap"}',
        "Shop/package-lock.json": '{"packages":{"node_modules/jquery":{"version":"3.7.1"}}}',
        "Shop/Views/_Layout.cshtml": """<link href="/bootstrap/4.6.2/css/bootstrap.min.css" rel="stylesheet">
<button
 data-toggle="modal" data-target="#cart">Cart</button>
<script>
const count = $('.cart').size();
</script>""",
        "Shop/Scripts/cart.js": """// $.now()
const message = "$.parseJSON(payload)";
const stamp = $.now();
const label = name.substr(1, 3);
function setup() { $('#cart').bind('click', openCart); }""",
        "Shop/libman.json": '{"libraries":[{"library":"bootstrap@4.6.2"}]}',
        "Shop/Content/jquery-1.12.4.min.js": "/* vendor */",
        "Shop/node_modules/a/bad.js": "$.trim(value)",
        "Other/package.json": '{"dependencies":{"bootstrap":"3.4.1"}}',
        "Shop/Cart.tsx": """// const ignored = '<div className="sr-only">';
export const Cart = () => <span className="sr-only">Cart</span>;""",
        "Shop/keys.js": "if (event.keyCode === 13) document.write('Ready');",
        "Other/modern.js": "const value = Date.now();",
    }
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as z:
        for name, body in files.items():
            z.writestr(name, body)
    result = scan(out.getvalue())
    report = result["scan"]
    assert report["files"] == 11, report
    items = report["findings"]
    hits = {(f["path"],f["line"],f["title"]) for f in items}
    for item in [
        ("Shop/Views/_Layout.cshtml", 3, "Legacy Bootstrap data-toggle"),
        ("Shop/Cart.tsx", 2, "Legacy Bootstrap class sr-only"),
        ("Shop/keys.js", 1, "Review keyboard event keyCode"),
        ("Shop/keys.js", 1, "Deprecated document.write()"),
        ("Shop/Views/_Layout.cshtml", 5, "Legacy jQuery size()"),
        ("Shop/Scripts/cart.js", 3, "Legacy jQuery now()"),
        ("Shop/Scripts/cart.js", 4, "Review JavaScript substr()"),
        ("Shop/Scripts/cart.js", 5, "Legacy jQuery bind()"),
        ("Shop/package-lock.json", 1, "Upgrade jQuery 3.7.1"),
        ("Shop/package.json", 1, "Verify jQuery dependency declaration"),
        ("Shop/libman.json", 1, "Multiple Bootstrap versions referenced"),
        ("Shop/Views/_Layout.cshtml", 1, "Multiple Bootstrap versions referenced"),
        ("Shop/Content/jquery-1.12.4.min.js", 1, "Upgrade jQuery 1.12.4"),
    ]:
        assert item in hits, (item, hits)
    assert ("Shop/Cart.tsx", 1, "Legacy Bootstrap class sr-only") not in hits
    assert all("parseJSON" not in f["title"] and "trim()" not in f["title"] for f in items)
    assert all("Multiple" not in f["title"] for f in items if f["path"].startswith("Other/"))
    assert all(f["upgrade"]["guide"].startswith("https://") and f["upgrade"]["verify"] for f in items)
    print(f"Frontend ZIP: {report['files']} files, {len(items)} findings. Mixed versions, source lines, upgrade guidance and independent project scope verified.")
    return result

def review_fixture():
    files = {
        "Shop/Web.csproj": "<Project/>",
        "Shop/package.json": '{"dependencies":{"jquery":"3.7.1"}}',
        "Shop/Views/Cart/Edit.cshtml": '@Html.Partial("_Items")',
        "Shop/Views/Shared/_Items.cshtml": "<p>Items</p>",
        "Shop/Views/_ViewStart.cshtml": '@{ Layout = "~/Views/Shared/_Layout.cshtml"; }',
        "Shop/Views/Shared/_Layout.cshtml": '@Scripts.Render("~/bundles/cart")',
        "Shop/App_Start/BundleConfig.cs": 'class App { void Register() { bundles.Add(new ScriptBundle("~/bundles/cart").Include("~/Scripts/jquery-{version}.js", "~/Scripts/cart.js")); } }',
        "Shop/Scripts/jquery-3.7.1.js": "/*! jQuery v3.7.1 */",
        "Shop/Scripts/cart.js": "let itemCount = $('.cart').size();",
        "Shop/node_modules/big.js": "ignored",
        "Shop/broken.csproj": "<Project>",
        "Shop/unreadable.js": b"\x80",
        "Shop/large.js": " " * 513000,
        "Shop/logo.svg": "<svg/>",
    }
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name,data in files.items():
            z.writestr(name,data)
    result = scan(out.getvalue())
    doc = result["scan"]
    coverage = {f["path"]:f["status"] for f in doc["coverage"]}
    assert len(coverage) == len(files), coverage
    assert coverage["Shop/logo.svg"] == coverage["Shop/large.js"] == "Skipped"
    assert coverage["Shop/unreadable.js"] == coverage["Shop/broken.csproj"] == "Unreadable"
    assert coverage["Shop/Scripts/jquery-3.7.1.js"] == "Limited"
    edges = {(e["from"],e["to"]) for e in doc["usage"]["edges"]}
    bundle = "Shop/Web.csproj · ~/bundles/cart"
    for edge in [("Shop/Views/Cart/Edit.cshtml","Shop/Views/Shared/_Items.cshtml"),
                 ("Shop/Views/Cart/Edit.cshtml","Shop/Views/_ViewStart.cshtml"),
                 ("Shop/Views/_ViewStart.cshtml","Shop/Views/Shared/_Layout.cshtml"),
                 ("Shop/Views/Shared/_Layout.cshtml",bundle),
                 (bundle,"Shop/Scripts/jquery-3.7.1.js"),(bundle,"Shop/Scripts/cart.js")]:
        assert edge in edges, (edge,edges)
    finding = next(f for f in doc["findings"] if f["title"] == "Legacy jQuery size()")
    assert finding["assessment"]["impact"] == "High" and finding["context"]
    path = "/api/scans/"+result["id"]+"/findings/"+finding["id"]+"/review"
    review = post(path,b"",{})
    assert review["edit"] == {"before":".size()", "after":".length"} and review["verify"]
    assert review["source"] in ("Groq","Rule guidance") if live else review["source"] == "Ollama"
    print("Validated file coverage, ordered MVC references, triage and finding preview using", review["source"])
    return path

if "--review-only" in sys.argv:
    review_fixture()
    sys.exit(0)

if "--frontend-only" in sys.argv:
    frontend()
    sys.exit(0)

if not live:
    stub = HTTPServer(("127.0.0.1",11434), Provider)
    threading.Thread(target=stub.serve_forever, daemon=True).start()
result = scan(inventory())
assert {p["kind"] for p in result["scan"]["projects"]} == {"Executable test runner","Library","Console application"}
assert len(result["scan"]["findings"]) == 5
assert all(f["project"] and f["path"].split("/")[0] == f["project"].split("/")[0] for f in result["scan"]["findings"])
console_plan = json.dumps(plan(result)).lower()
assert all(word not in console_plan for word in ["mvc","session","controller","razor","route"])
assert all(word in console_plan for word in ["console","test runner","library","datafilepath","csvexportpath"])
print("InventoryTracker fixture: five findings; console, library, executable test runner; no web tasks.")
store = scan(Path("web/storefront.zip").read_bytes())
assert store["scan"]["projects"][0]["kind"] == "MVC web application"
web_plan = json.dumps(plan(store)).lower()
assert "mvc" in web_plan and "session" in web_plan and "razor" not in web_plan
if not live:
    frontend_plan = plan(frontend())
    assert any('data-bs-toggle' in p['action'] for p in frontend_plan)
    assert len(requests_seen) == 3
    path = review_fixture()
    stub.shutdown()
    stub.server_close()
    fallback = post(path,b"",{})
    assert fallback["source"] == "Rule guidance" and fallback["edit"]["after"] == ".length"
print("Storefront fixture: MVC and session tasks present, unsupported instructions excluded.")
