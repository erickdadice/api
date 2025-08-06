using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastApiProcessor.Utils
{
    public class BearerAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<BearerAuthMiddleware> _logger;
        private readonly string _expectedBase64Token;

        public BearerAuthMiddleware(RequestDelegate next, ILogger<BearerAuthMiddleware> logger, IConfiguration config)
        {
            _next = next;
            _logger = logger;

            var token = config["ApiAuth:BearerToken"];
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("El token de autenticación 'ApiAuth:BearerToken' no está configurado.");
            }

            _expectedBase64Token = token;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Falta el header Authorization.");
                return;
            }

            var token = authHeader.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();

            if (string.IsNullOrWhiteSpace(token) || token != _expectedBase64Token)
            {
                _logger.LogWarning(" Token inválido recibido: {token}", token);
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Token inválido o no autorizado.");
                return;
            }

            await _next(context);
        }
    }
}
