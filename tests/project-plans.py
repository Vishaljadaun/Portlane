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
        payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
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
    assert len(requests_seen) == 2
    stub.shutdown()
print("Storefront fixture: MVC and session tasks present, unsupported instructions excluded.")
