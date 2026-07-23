using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Hangfire;
using Hangfire.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ===================== 1. БАЗА ДАННЫХ =====================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (builder.Environment.IsDevelopment())
{
    // Локально: SQLite (для совместимости с Production)
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(connectionString));
}
else
{
    // Продакшен: SQLite, путь к файлу жёстко прописан
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite("Data Source=/opt/investment-tracker-pro/app-data/investmenttracker-pro.db"));
}

// ===================== 2. IDENTITY =====================
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ===================== 3. JWT‑АУТЕНТИФИКАЦИЯ =====================
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.ASCII.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"]
    };
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ===================== 4. SWAGGER =====================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Investment Tracker API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

// ===================== 5. HANGFIRE (только локально) =====================
if (builder.Environment.IsDevelopment())
{
    // Hangfire требует SQL Server, поэтому локально он недоступен при использовании SQLite.
    // Если нужен локально, можно временно подключить SQL Server только для Hangfire.
    // Пока оставляем пустым.
}

// ===================== 6. РЕГИСТРАЦИЯ СЕРВИСОВ =====================
builder.Services.AddHttpClient<MoexService>();
builder.Services.AddScoped<QuoteUpdateService>();

// Фоновые сервисы (только на проде)
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<QuoteBackgroundService>();
}

builder.Services.AddSingleton<SettingsService>();
builder.Services.AddHostedService<DividendUpdateService>();
builder.Services.AddScoped<DividendLoaderService>();
builder.Services.AddScoped<BondLoaderService>();
builder.Services.AddScoped<BondPaymentLoaderService>();
builder.Services.AddSingleton<IEmailService, EmailService>();

var app = builder.Build();

// ===================== 7. ПРИМЕНЕНИЕ МИГРАЦИЙ =====================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Всегда используем Migrate – он совместим с SQLite и корректно ведёт историю
    db.Database.Migrate();
}

// ===================== 8. ЛОГИРОВАНИЕ =====================
app.Logger.LogInformation("Application starting...");

// ===================== 9. SWAGGER (доступен всегда) =====================
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Investment Tracker API v1"));

// ===================== 10. ОБРАБОТКА ОШИБОК И HTTPS =====================
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// ===================== 11. СТАТИЧЕСКИЕ ФАЙЛЫ И Blazor =====================
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// ===================== 12. МАРШРУТИЗАЦИЯ, АУТЕНТИФИКАЦИЯ, АВТОРИЗАЦИЯ =====================
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ===================== 13. HANGFIRE DASHBOARD (только локально) =====================
if (app.Environment.IsDevelopment())
{
    // Локально Hangfire не используется, т.к. база SQLite
}

// ===================== 14. КОНТРОЛЛЕРЫ, RAZOR PAGES, SEO‑MIDDLEWARE =====================
app.MapRazorPages();
app.MapControllers();
app.UseMiddleware<InvestmentTracker.Server.Middleware.SeoMiddleware>();
app.MapFallbackToFile("index.html");

// ===================== 15. НАЧАЛЬНЫЕ ДАННЫЕ (SEED) =====================
using (var innerScope = app.Services.CreateScope())
{
    var serviceProvider = innerScope.ServiceProvider;
    try
    {
        await SeedData.Initialize(serviceProvider);
    }
    catch (Exception ex)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();