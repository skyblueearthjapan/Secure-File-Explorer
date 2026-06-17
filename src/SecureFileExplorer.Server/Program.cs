using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Security;
using SecureFileExplorer.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Windows サービスとして動かせるようにする ----
// コンソール実行（開発）でもサービス実行（本番MTSV）でも同じバイナリで動く。
// サービス時はコンテンツルートが exe のフォルダーに設定される。
builder.Host.UseWindowsService(o => o.ServiceName = "SecureFileExplorer");

// ---- 設定バインド ----
builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection("Catalog"));
var ipOptions = builder.Configuration.GetSection("IpRestriction").Get<IpRestrictionOptions>()
    ?? new IpRestrictionOptions();

// ---- DB (SQLite / EF Core) ----
var conn = builder.Configuration.GetConnectionString("Catalog")
    ?? "Data Source=catalog.db";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(conn));

// ---- 認証: Windows統合認証 (Negotiate/Kerberos/NTLM) ----
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    // 既定で全エンドポイントに認証を要求する。
    options.FallbackPolicy = options.DefaultPolicy;
});

// ---- アプリサービス ----
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IAccessLogger, AccessLogger>();

// ---- アクセスログのExcel出力（ユーザー別シート）----
builder.Services.Configure<ExcelLogOptions>(builder.Configuration.GetSection("ExcelLog"));
builder.Services.AddHostedService<ExcelLogExporter>();

// ---- 大量アクセス検知 → 警告メールをアウトボックスへ ----
builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection("Alert"));
builder.Services.AddHostedService<AccessAlertMonitor>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---- DB初期化（MVP: マイグレーション未使用なので EnsureCreated）＋ ルート登録 ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // オンデマンド方式: 事前スキャンせず、ルートノードだけ登録しておく。
    var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
    await catalog.EnsureRootsAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ---- 社内ネットワーク以外を遮断（認証より前に弾く）----
app.UseIpRestriction(ipOptions);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// テストプロジェクトから参照できるよう公開する。
public partial class Program { }
