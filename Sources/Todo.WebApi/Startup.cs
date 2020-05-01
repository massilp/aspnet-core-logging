﻿using System;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Todo.Persistence;
using Todo.Services;
using Todo.WebApi.Authorization;
using Todo.WebApi.ExceptionHandling;
using Todo.WebApi.Logging;
using Todo.WebApi.Models;

namespace Todo.WebApi
{
    /// <summary>
    /// Starts this ASP.NET Core application.
    /// </summary>
    public class Startup
    {
        private readonly bool shouldUseMiniProfiler;

        /// <summary>
        /// Creates a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="configuration">The configuration to be used for setting up this application.</param>
        /// /// <param name="webHostEnvironment">The environment where this application is hosted.</param>
        public Startup(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            WebHostingEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));

            shouldUseMiniProfiler = bool.TryParse(Configuration["MiniProfiler:Enable"], out bool enableMiniProfiler) &&
                                    enableMiniProfiler;
        }

        private IConfiguration Configuration { get; }

        private IWebHostEnvironment WebHostingEnvironment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Configure logging
            services.AddLogging(loggingBuilder =>
            {
                if (WebHostingEnvironment.IsProduction())
                {
                    loggingBuilder.ClearProviders();
                }

                // https://github.com/huorswords/Microsoft.Extensions.Logging.Log4Net.AspNetCore
                var log4NetProviderOptions = Configuration.GetSection("Log4NetCore").Get<Log4NetProviderOptions>();
                loggingBuilder.AddLog4Net(log4NetProviderOptions);

                // https://github.com/huorswords/Microsoft.Extensions.Logging.Log4Net.AspNetCore#net-core-20---logging-debug-level-messages
                loggingBuilder.SetMinimumLevel(LogLevel.Debug);
            });

            // Configure EF Core
            services.AddDbContext<TodoDbContext>((serviceProvider, dbContextOptionsBuilder) =>
            {
                var connectionString = Configuration.GetConnectionString("Todo");
                dbContextOptionsBuilder.UseNpgsql(connectionString)
                    .UseLoggerFactory(serviceProvider.GetRequiredService<ILoggerFactory>());

                if (WebHostingEnvironment.IsDevelopment())
                {
                    dbContextOptionsBuilder.EnableSensitiveDataLogging();
                    dbContextOptionsBuilder.EnableDetailedErrors();
                }
            });

            // Display personally identifiable information only during development
            IdentityModelEventSource.ShowPII = WebHostingEnvironment.IsDevelopment();

            // Configure authentication & authorization using JWT tokens
            IConfigurationSection generateJwtTokensOptions =
                Configuration.GetSection("GenerateJwtTokens");
            string tokenIssuer = generateJwtTokensOptions.GetValue<string>("Issuer");
            string tokenAudience = generateJwtTokensOptions.GetValue<string>("Audience");

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(generateJwtTokensOptions.GetValue<string>("Secret"))),
                    ValidateIssuer = true,
                    ValidIssuer = tokenIssuer,
                    ValidateAudience = true,
                    ValidAudience = tokenAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    // Ensure the User.Identity.Name is set to the user identifier and not to the user name.
                    NameClaimType = ClaimTypes.NameIdentifier
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                        {
                            context.Response.Headers.Add("Token-Expired", "true");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy(Policies.TodoItems.GetTodoItems,
                    policy => policy.Requirements.Add(new HasScopeRequirement("get:todo", tokenIssuer)));
                options.AddPolicy(Policies.TodoItems.CreateTodoItem,
                    policy => policy.Requirements.Add(new HasScopeRequirement("create:todo", tokenIssuer)));
                options.AddPolicy(Policies.TodoItems.UpdateTodoItem,
                    policy => policy.Requirements.Add(new HasScopeRequirement("update:todo", tokenIssuer)));
                options.AddPolicy(Policies.TodoItems.DeleteTodoItem,
                    policy => policy.Requirements.Add(new HasScopeRequirement("delete:todo", tokenIssuer)));
            });
            services.AddSingleton<IAuthorizationHandler, HasScopeHandler>();

            // Configure MiniProfiler for Web API and EF Core only when inside development environment.
            // Based on: https://dotnetthoughts.net/using-miniprofiler-in-aspnetcore-webapi/.
            if (shouldUseMiniProfiler)
            {
                services.AddMemoryCache();
                services.AddMiniProfiler(options =>
                {
                    // MiniProfiler URLs (assuming options.RouteBasePath has been set to '/miniprofiler')
                    // - show all requests:         /miniprofiler/results-index
                    // - show current request:      /miniprofiler/results
                    // - show all requests as JSON: /miniprofiler/results-list
                    options.RouteBasePath = Configuration.GetValue<string>("MiniProfiler:RouteBasePath");
                    options.EnableServerTimingHeader = true;
                }).AddEntityFramework();
            }

            // Configure ASP.NET Web API services
            services.AddControllers();

            // Configure Todo Web API services
            services.AddScoped<ITodoService, TodoService>();

            // Register service with 2 interfaces.
            // See more here: https://andrewlock.net/how-to-register-a-service-with-multiple-interfaces-for-in-asp-net-core-di/.
            services.AddSingleton<LoggingService>();
            services.AddSingleton<IHttpObjectConverter>(serviceProvider =>
                serviceProvider.GetRequiredService<LoggingService>());
            services.AddSingleton<IHttpContextLoggingHandler>(serviceProvider =>
                serviceProvider.GetRequiredService<LoggingService>());

            // Configure options used for customizing generating JWT tokens.
            // Options pattern: https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-3.1.
            services.Configure<GenerateJwtTokensOptions>(generateJwtTokensOptions);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder applicationBuilder)
        {
            // The HTTP logging middleware *must* be the first one inside the ASP.NET Core request pipeline to ensure
            // all requests and their responses are properly logged
            applicationBuilder.UseHttpLogging();

            // The exception handling middleware *must* be the second one inside the ASP.NET Core request pipeline
            // to ensure any unhandled exception is eventually handled
            applicationBuilder.UseExceptionHandler(localApplicationBuilder =>
                localApplicationBuilder.UseCustomExceptionHandler(WebHostingEnvironment));

            if (shouldUseMiniProfiler)
            {
                applicationBuilder.UseMiniProfiler();
            }

            applicationBuilder.UseHttpsRedirection();
            applicationBuilder.UseRouting();
            applicationBuilder.UseAuthentication();
            applicationBuilder.UseAuthorization();
            applicationBuilder.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}