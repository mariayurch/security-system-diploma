using backend.Data;
using backend.Services;
using Microsoft.EntityFrameworkCore;
using backend.Services.Telegram;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));

builder.Services.AddSingleton<TelegramMessageFormatter>();

builder.Services.AddHttpClient<ITelegramNotificationService, TelegramNotificationService>();

builder.Services.AddScoped<EventIngestionService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddHostedService<MqttListenerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();