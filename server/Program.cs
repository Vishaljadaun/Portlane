using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Xml;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions {Args=args, WebRootPath=Path.Combine(AppContext.BaseDirectory,"wwwroot")});
builder.WebHost.ConfigureKestrel(k=>k.Limits.MaxRequestBodySize=12_000_000);
builder.Services.AddHttpClient<Planner>(c=>c.Timeout=TimeSpan.FromSeconds(100));
builder.Services.AddRateLimiter(o=>{
    o.RejectionStatusCode=429;
    o.AddFixedWindowLimiter("api", p=>{p.PermitLimit=30; p.Window=TimeSpan.FromMinutes(1); p.QueueLimit=0;});
});
var app=builder.Build();
var dbPath=builder.Configuration["Storage:Path"] ?? "portlane.db";
SqliteConnection Open(){
    var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=dbPath}.ToString());
    db.Open();
    return db;
}
using(var db=Open()){
    using var cmd=db.CreateCommand();
    cmd.CommandText="CREATE TABLE IF NOT EXISTS reports (id TEXT PRIMARY KEY, scan TEXT NOT NULL, plan TEXT)";
    cmd.ExecuteNonQuery();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
var api=app.MapGroup("/api").RequireRateLimiting("api");
api.MapGet("/provider",(Planner p)=>new {p.Provider,p.Model});
api.MapPost("/scans", async (HttpRequest request,CancellationToken ct)=>{
    if(!request.HasFormContentType) return Results.BadRequest("Send a ZIP file.");
    try {
        var form=await request.ReadFormAsync(ct);
        var file=form.Files.GetFile("file");
        if(file is null || file.Length is 0 or > 10_000_000) return Results.BadRequest("Choose a ZIP smaller than 10 MB.");
        using var stream=file.OpenReadStream();
        var scan=Scanner.Read(stream,Path.GetFileNameWithoutExtension(file.FileName));
        var id=Guid.NewGuid().ToString("N");
        using var db=Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO reports(id,scan) VALUES($id,$scan)";
        cmd.Parameters.AddWithValue("$id",id); cmd.Parameters.AddWithValue("$scan",JsonSerializer.Serialize(scan)); cmd.ExecuteNonQuery();
        return Results.Ok(new {id,scan});
    } catch(ScanLimitException ex) {
        return Results.BadRequest(ex.Message);
    } catch(Exception ex) when(ex is InvalidDataException or XmlException or NotSupportedException or ArgumentException) {
        return Results.BadRequest("Unreadable archive or invalid source. Use C# source and valid project XML within the upload limits.");
    }
});
api.MapPost("/scans/{id}/plan",async(string id,Planner planner,CancellationToken ct)=>{
    using var db=Open(); using var cmd=db.CreateCommand();
    cmd.CommandText="SELECT scan,plan FROM reports WHERE id=$id"; cmd.Parameters.AddWithValue("$id",id);
    Scan scan;
    using(var row=cmd.ExecuteReader()){
        if(!row.Read()) return Results.NotFound();
        if(!row.IsDBNull(1) && JsonSerializer.Deserialize<MigrationPlan>(row.GetString(1)) is {Version: 2} cached)
            return Results.Ok(cached);
        scan=JsonSerializer.Deserialize<Scan>(row.GetString(0))!;
    }
    try {
        var plan=await planner.Generate(scan,ct);
        cmd.CommandText="UPDATE reports SET plan=CASE WHEN json_extract(plan,'$.Version')=2 THEN plan ELSE $plan END WHERE id=$id RETURNING plan";
        cmd.Parameters.AddWithValue("$plan",JsonSerializer.Serialize(plan));
        return Results.Ok(JsonSerializer.Deserialize<MigrationPlan>((string)cmd.ExecuteScalar()!));
    } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException or IndexOutOfRangeException){
        return Results.Problem("The provider could not return a complete plan. Check model settings, quotas and connectivity, then retry.",statusCode:503);
    }
});
app.Run();
