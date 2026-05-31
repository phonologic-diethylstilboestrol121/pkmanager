using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PkManager.Server.Data;
using PkManager.Server.Helpers;
using PkManager.Server.Middleware;
using PkManager.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// HTTPS — mGBA WASM 需要 SharedArrayBuffer（浏览器仅对 localhost 或 HTTPS 启用）
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000); // HTTP
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("/home/fmangela/pkmanager/server/cert.pfx", "pkmanager123");
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

// ── 应用服务注册 ────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ParseService>();
builder.Services.AddScoped<SaveFileService>();
builder.Services.AddScoped<BankService>();
builder.Services.AddScoped<PokemonEditService>();

// ── 控制器 & Swagger ────────────────────────────────────
builder.Services.AddControllers()
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

    // Swagger 中启用 JWT Bearer 输入
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
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

// ── 中间件管道 ───────────────────────────────────────────

// Cross-Origin Isolation — only for emulator page (mGBA WASM needs SharedArrayBuffer)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/play"))
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
