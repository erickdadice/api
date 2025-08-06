using FastApiProcessor.Services;
using FastApiProcessor.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// -------------------- FORZAR ENTORNO SI FALTA --------------------
if (string.IsNullOrEmpty(builder.Environment.EnvironmentName))
{
    builder.Environment.EnvironmentName = Environments.Development;
}

// -------------------- CONFIGURACIÓN --------------------
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// -------------------- LOGGING --------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// -------------------- SERVICIOS --------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    //  Añadimos esta parte para que Swagger tenga botón "Authorize"
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Fast-api",
        Version = "v1"
    });

    //  Definición del esquema de autenticación Bearer
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Introduce el token en este formato: Bearer {tu_token}"
    });

    // Aplicación global del esquema
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

    // 🧼 Mantiene tu lógica para ocultar métodos con IgnoreApi
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (apiDesc.ActionDescriptor is ControllerActionDescriptor actionDescriptor)
        {
            var ignoreApiAttr = actionDescriptor.MethodInfo
                .GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), true)
                .OfType<ApiExplorerSettingsAttribute>()
                .FirstOrDefault();

            return ignoreApiAttr == null || !ignoreApiAttr.IgnoreApi;
        }

        return true;
    });
});

builder.Services.AddScoped<IProcessFiles, ProcessFiles>();

// -------------------- CORS --------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// -------------------- LOG DE ARRANQUE --------------------
var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
    loggingBuilder.AddConfiguration(builder.Configuration.GetSection("Logging"));
});
var startupLogger = loggerFactory.CreateLogger("Startup");
startupLogger.LogInformation(" Aplicación iniciando en entorno: {Environment}", builder.Environment.EnvironmentName);

var app = builder.Build();

// -------------------- MIDDLEWARES --------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI();

// Redirección automática desde "/" hacia Swagger UI
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.Redirect("/swagger");
        return;
    }
    await next();
});

// Middleware de trazabilidad
app.Use(async (context, next) =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RequestTrace");
    logger.LogInformation(" [Middleware] Solicitud entrante: {method} {url}", context.Request.Method, context.Request.Path);

    await next();

    logger.LogInformation(" [Middleware] Solicitud completada: {statusCode}", context.Response.StatusCode);
});

app.UseHttpsRedirection();

//  AUTENTICACIÓN BEARER
app.UseMiddleware<BearerAuthMiddleware>();

app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();

app.Run();
