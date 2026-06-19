using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PkManager.Server.Data;
using PkManager.Server.Helpers;
using PkManager.Server.Localization;
using PkManager.Server.Middleware;
using PkManager.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ── 加载根目录 config 文件（KEY=VALUE 格式）─────────────────
// 配置值通过 builder.Configuration 注入，兼容 appsettings.json 的嵌套键（如 Jwt:Secret）
var configPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "config");
if (File.Exists(configPath))
{
    foreach (var line in File.ReadAllLines(configPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var eq = trimmed.IndexOf('=');
        if (eq <= 0) continue;
        var configKey = trimmed[..eq].Trim();
        var configValue = trimmed[(eq + 1)..].Trim().Trim('"').Trim('\'');

        switch (configKey)
        {
            case "DB_HOST":
            case "DB_PORT":
            case "DB_NAME":
            case "DB_USER":
            case "DB_PASSWORD":
                // 构建连接字符串（等所有 DB_* 读取完毕后组装）
                break;
            case "JWT_SECRET":
                builder.Configuration["Jwt:Secret"] = configValue;
                break;
            case "JWT_ISSUER":
                builder.Configuration["Jwt:Issuer"] = configValue;
                break;
            case "JWT_AUDIENCE":
                builder.Configuration["Jwt:Audience"] = configValue;
                break;
            case "JWT_EXPIRE_HOURS":
                builder.Configuration["Jwt:ExpireHours"] = configValue;
                break;
            case "JWT_REFRESH_EXPIRE_DAYS":
                builder.Configuration["Jwt:RefreshExpireDays"] = configValue;
                break;
            default:
                // 其他 key 直接作为顶级配置注入
                builder.Configuration[configKey] = configValue;
                break;
        }
    }

    // 用 config 文件值构建连接字符串
    var dbHost = builder.Configuration["DB_HOST"] ?? "localhost";
    var dbPort = builder.Configuration["DB_PORT"] ?? "5432";
    var dbName = builder.Configuration["DB_NAME"] ?? "pkmanager";
    var dbUser = builder.Configuration["DB_USER"] ?? "pkadmin";
    var dbPass = builder.Configuration["DB_PASSWORD"] ?? "";
    builder.Configuration["ConnectionStrings:Default"] =
        $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPass}";
}

// HTTPS — mGBA WASM 需要 SharedArrayBuffer（浏览器仅对 localhost 或 HTTPS 启用）
var httpPort = builder.Configuration.GetValue("SERVER_HTTP_PORT", 5000);
var httpsPort = builder.Configuration.GetValue("SERVER_HTTPS_PORT", 5001);
var certPassword = builder.Configuration["CERT_PFX_PASSWORD"] ?? "change-me";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(httpPort); // HTTP
    options.ListenAnyIP(httpsPort, listenOptions =>
    {
        var certPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "certs", "cert.pfx");
        listenOptions.UseHttps(certPath, certPassword);
    });
});

// ── Dapper 配置：自动映射 snake_case 列名到 PascalCase 属性 ──
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

// ── 数据库连接 ──────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")!;

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddScoped<Npgsql.NpgsqlConnection>(sp =>
{
    var factory = sp.GetRequiredService<DbConnectionFactory>();
    return factory.CreateConnection();
});

// ── JWT 认证 ────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();

// ── 应用服务注册 ────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<ILanguageResolver, LanguageResolver>();
builder.Services.AddScoped<IPkhexStringProvider, PkhexStringProvider>();
builder.Services.AddScoped<IGeoDataProvider, GeoDataProvider>();
builder.Services.AddScoped<IBackendMessageLocalizer, JsonMessageLocalizer>();
builder.Services.AddScoped<BusinessExceptionFilter>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ParseService>();
builder.Services.AddScoped<SaveFileService>();
builder.Services.AddScoped<BankService>();
builder.Services.AddScoped<PokemonEditService>();
builder.Services.AddScoped<EvolutionService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<LegalizationService>();
builder.Services.AddSingleton<LegalityCacheService>();

// ── 控制器 & Swagger ────────────────────────────────────
builder.Services.AddControllers(options =>
    {
        options.Filters.AddService<BusinessExceptionFilter>();
    })
    .AddJsonOptions(options =>
    {
        // 自定义命名策略：强制全小写首字母（IVs→ivs, EVs→evs 而非默认的 iVs, eVs）
        options.JsonSerializerOptions.PropertyNamingPolicy = new ForceLowercaseNamingPolicy();
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // 禁用自动 400 验证响应，交由 Controller 手动处理（返回统一的 ApiResponse 格式）
        options.SuppressModelStateInvalidFilter = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "PkManager API", Version = "v1" });

    // TODO: JWT Bearer security definition — OpenApi v2.x API 变更 (SecuritySchemeType→IOpenApiSecurityScheme)
    // 暂时跳过，Swagger UI 仍可浏览 API 文档
});

// ── CORS ────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)  // 开发阶段允许所有来源（局域网访问）
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ── 一次性启动迁移：将 DB 中过期的绝对 save_path 重写为当前规范路径 ──
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Npgsql.NpgsqlConnection>();
    await db.ExecuteAsync("""
        ALTER TABLE users
        ADD COLUMN IF NOT EXISTS preferred_lang VARCHAR(10) NOT NULL DEFAULT 'zh-Hans';

        COMMENT ON COLUMN users.preferred_lang IS '账号级界面语言偏好';
        """);

    var saveFileService = scope.ServiceProvider.GetRequiredService<SaveFileService>();
    await saveFileService.MigrateSavePaths();
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] Schema/bootstrap migration skipped: {ex.Message}");
}

// ── 中间件管道 ───────────────────────────────────────────

// Exception logging — must be first to catch all unhandled exceptions
app.UseMiddleware<PkManager.Server.Middleware.ExceptionLoggingMiddleware>();

// Cross-Origin Isolation — emulator pages need SharedArrayBuffer (mGBA + melonDS pthreads)
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/play") || path.StartsWithSegments("/play-nds"))
    {
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowClient");
app.UseAuthentication();
app.UseMiddleware<LanguageMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// JSON 序列化命名策略：强制首字母+后续大写字母全小写，避免 IVs→iVs 问题
/// </summary>
public class ForceLowercaseNamingPolicy : System.Text.Json.JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // 将首字母及紧随的大写字母段全转为小写
        // IVs → ivs, EVs → evs, AVs → avs, GVs → gvs, EXP → exp
        int i = 0;
        while (i < name.Length && char.IsUpper(name[i]))
            i++;
        if (i == 0) return char.ToLowerInvariant(name[0]) + name.Substring(1);
        return name.Substring(0, i).ToLowerInvariant() + name.Substring(i);
    }
}
