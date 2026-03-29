using Pixelrun_Server;
using Pixelrun_Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        c.SwaggerDoc("v1", new() { Title = "PixelRun API", Version = "v1" }));

    builder.Services.AddDbContext<GameDbContext>(opt =>
    {
        opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
        opt.UseLoggerFactory(LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
    });

    builder.Services.AddScoped<PlayerService>();
    builder.Services.AddScoped<TokenService>();
    builder.Services.AddScoped<RecordService>();
    builder.Services.AddScoped<ShopService>();
    builder.Services.AddScoped<QuestService>();

    // MultiplayerHub is Singleton because it holds the static player map
    builder.Services.AddSingleton<MultiplayerHub>();

    builder.Services.AddCors(opt =>
        opt.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        db.Database.EnsureCreated();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "PixelRun API v1"));
    }

    app.UseCors("AllowAll");

    // ── WebSocket middleware ──────────────────────────────────────────────────
    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(30)
    });

    app.Map("/ws/game", async (HttpContext ctx, MultiplayerHub hub) =>
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            return;
        }
        var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        await hub.HandleAsync(ctx, ws);
    });
    // ─────────────────────────────────────────────────────────────────────────

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "startup_error.log");
    File.WriteAllText(logPath, $"[{DateTime.Now}] FATAL:\n{ex}\n\nInner: {ex.InnerException}");
    Console.WriteLine($"FATAL: {ex}");
    Console.ReadKey();
    throw;
}