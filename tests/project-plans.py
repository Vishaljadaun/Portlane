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
        steps = [{"id": "unknown", "check": 0}, {"id": tasks[-1]["Id"], "check": 1,
                 "action": "Install a Razor views project"}]
        body = json.dumps({"message": {"content": json.dumps({"steps": steps})}}).encode()
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
    stub.shutdown()
print("Storefront fixture: MVC and session tasks present, unsupported instructions excluded.")
