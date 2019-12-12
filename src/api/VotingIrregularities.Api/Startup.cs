using AutoMapper;
using MediatR;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Serilog;
using SimpleInjector;
using SimpleInjector.Lifestyles;
using Swashbuckle.AspNetCore.Swagger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using VoteMonitor.Api.Answer.Controllers;
using VoteMonitor.Api.Core;
using VoteMonitor.Api.Core.Handlers;
using VoteMonitor.Api.Core.Options;
using VoteMonitor.Api.Core.Services;
using VoteMonitor.Api.DataExport.Controller;
using VoteMonitor.Api.Form.Controllers;
using VoteMonitor.Api.Location.Controllers;
using VoteMonitor.Api.Location.Services;
using VoteMonitor.Api.Note.Controllers;
using VoteMonitor.Api.Notification.Controllers;
using VoteMonitor.Api.Observer.Controllers;
using VoteMonitor.Api.Statistics.Controllers;
using VoteMonitor.Entities;
using VotingIrregularities.Api.Extensions;
using VotingIrregularities.Api.Extensions.Startup;
using VotingIrregularities.Api.Options;

namespace VotingIrregularities.Api
{
    public class Startup
    {
        private readonly Container _container = new Container
        {
            Options =
            {
                DefaultLifestyle = Lifestyle.Scoped,
                DefaultScopedLifestyle = new AsyncScopedLifestyle(),
                //AllowOverridingRegistrations = true
            }
        };

        public Startup(IConfiguration configuration, IHostingEnvironment env)
        {
            Environment = env;
            Configuration = (IConfigurationRoot)configuration;
        }

        private IConfigurationRoot Configuration { get; }
        private IHostingEnvironment Environment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Get options from app settings
            services.AddOptions();
            services.ConfigureCustomOptions(Configuration);

            services.ConfigureVoteMonitorAuthentication(Configuration);
            services.AddApplicationInsightsTelemetry(Configuration);
            services.AddMvc(config =>
                {
                    config.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder()
                                                                        .RequireAuthenticatedUser()
                                                                        .RequireClaim(ClaimsHelper.IdNgo)
                                                                        .Build()));
                })
                .AddApplicationPart(typeof(PollingStationController).Assembly)
                .AddApplicationPart(typeof(ObserverController).Assembly)
                .AddApplicationPart(typeof(NotificationController).Assembly)
                .AddApplicationPart(typeof(NoteController).Assembly)
                .AddApplicationPart(typeof(FormController).Assembly)
                .AddApplicationPart(typeof(AnswersController).Assembly)
                .AddApplicationPart(typeof(StatisticsController).Assembly)
                .AddApplicationPart(typeof(DataExportController).Assembly)
                .AddControllersAsServices()
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Info
                {
                    Version = "v1",
                    Title = "VoteMonitor ",
                    Description = "API specs for NGO Admin and Observer operations.",
                    TermsOfService = "TBD",
                    Contact =
                        new Contact
                        {
                            Email = "info@monitorizarevot.ro",
                            Name = "Code for Romania",
                            Url = "http://monitorizarevot.ro"
                        },
                });

                options.AddSecurityDefinition("bearer", new ApiKeyScheme
                {
                    Name = "Authorization",
                    In = "header",
                    Type = "apiKey"
                });
                options.AddSecurityRequirement(new Dictionary<string, IEnumerable<string>>{
                    { "bearer", new[] {"readAccess", "writeAccess" } } });

                options.OperationFilter<AddFileUploadParams>();

                var baseDocPath = PlatformServices.Default.Application.ApplicationBasePath;

                foreach (var api in Directory.GetFiles(baseDocPath, "*.xml"))
                {
                    options.IncludeXmlComments(api);
                }
            });

            ConfigureCache(services);

            services.AddCors(options => options.AddPolicy("Permissive", builder =>
            {
                builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }));

            services.AddSimpleInjector(_container, options =>
            {
                options.AddAspNetCore().AddControllerActivation();
                options.AddLogging();
            });

            RegisterAppServices(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IApplicationLifetime appLifetime)
        {
            app.UseSimpleInjector(_container);

            app.UseStaticFiles();

            Log.Logger = new LoggerConfiguration()
                .WriteTo
                .ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
                .CreateLogger();

            appLifetime.ApplicationStopped.Register(Log.CloseAndFlush);

            if (!Environment.IsDevelopment())
            {
                app.UseExceptionHandler(
                    builder =>
                    {
                        builder.Run(context =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                                context.Response.ContentType = "application/json";
                                return Task.FromResult(0);
                            }
                        );
                    }
                );
            }

            app.UseAuthentication();

            // Enable middleware to serve generated Swagger as a JSON endpoint
            app.UseSwagger();

            // Enable middleware to serve swagger-ui assets (HTML, JS, CSS etc.)
            app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "MV API v1"));
            app.UseCors("Permissive");
            app.UseMvc();

            _container.Verify();
            FirebaseConfigurationExtension.ConfigurePrivateKey(Configuration);
            InitializeStorage();
        }

        private void RegisterAppServices(IServiceCollection services)
        {
            _container.RegisterInstance(Configuration);
            _container.Register<IPollingStationService, PollingStationService>(Lifestyle.Scoped);
            _container.RegisterSingleton<IFirebaseService, FirebaseService>();
            _container.RegisterSingleton<IFileLoader, XlsxFileLoader>();


            RegisterFileService(services);
            RegisterHashService(services);
            // RegisterDbContext<VoteMonitorContext>(Configuration.GetConnectionString("DefaultConnection"));
            RegisterAutomapper();
            BuildMediator();
        }

        private void InitializeStorage()
        {
            var fileService = _container.GetInstance<IFileService>();
            fileService.Initialize();
        }

        private void ConfigureCache(IServiceCollection services)
        {
            var cacheOptions = new ApplicationCacheOptions();
            Configuration.GetSection(nameof(ApplicationCacheOptions)).Bind(cacheOptions);

            switch (cacheOptions.Implementation)
            {
                case "NoCache":
                {
                    _container.RegisterInstance<ICacheService>(new NoCacheService());
                    break;
                }
                case "RedisCache":
                {
                    _container.RegisterSingleton<ICacheService, CacheService>();
                    services.AddDistributedRedisCache(options =>
                    {
                        Configuration.GetSection("RedisCacheOptions").Bind(options);
                    });

                    break;
                }
                case "MemoryDistributedCache":
                {
                    _container.RegisterSingleton<ICacheService, CacheService>();
                    services.AddDistributedMemoryCache();
                    break;
                }
            }
        }

        private void RegisterFileService(IServiceCollection services)
        {
            services.Configure<FileServiceOptions>(Configuration.GetSection(nameof(FileServiceOptions)));

            var options = new FileServiceOptions();
            Configuration.GetSection(nameof(FileServiceOptions)).Bind(options);


            if (options.Type == "LocalFileService")
            {
                _container.RegisterSingleton<IFileService, LocalFileService>();
            }
            else
            {
                _container.RegisterSingleton<IFileService, BlobService>();
            }
        }

        private void RegisterHashService(IServiceCollection services)
        {
            services.Configure<HashOptions>(Configuration.GetSection(nameof(HashOptions)));

            var options = new HashOptions();
            Configuration.GetSection(nameof(HashOptions)).Bind(options);

            if (options.ServiceType == nameof(HashServiceType.ClearText))
            {
                _container.RegisterSingleton<IHashService, ClearTextService>();
            }
            else
            {
                _container.RegisterSingleton<IHashService, HashService>();
            }
        }

        private void RegisterDbContext<TDbContext>(string connectionString = null)
            where TDbContext : DbContext
        {

            if (!string.IsNullOrEmpty(connectionString))
            {
                var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
                optionsBuilder.UseSqlServer(connectionString);
                _container.RegisterInstance(optionsBuilder.Options);
            }

            _container.Register<TDbContext>(Lifestyle.Scoped);
        }

        private void BuildMediator()
        {
            var assemblies = GetAssemblies().ToArray();
            _container.RegisterSingleton<IMediator, Mediator>();
            _container.Register(typeof(IRequestHandler<,>), assemblies);
            _container.Collection.Register(typeof(INotificationHandler<>), assemblies);

            // had to add this registration as we were getting the same behavior as described here: https://github.com/jbogard/MediatR/issues/155
            _container.Collection.Register(typeof(IPipelineBehavior<,>), Enumerable.Empty<Type>());

            _container.RegisterInstance(Console.Out);
            _container.RegisterInstance(new ServiceFactory(_container.GetInstance));
        }

        private void RegisterAutomapper()
        {
            Mapper.Initialize(cfg => { cfg.AddProfiles(GetAssemblies()); });

            _container.RegisterInstance(Mapper.Configuration);
            _container.Register<IMapper>(() => new Mapper(Mapper.Configuration), Lifestyle.Scoped);
        }

        private static IEnumerable<Assembly> GetAssemblies()
        {
            yield return typeof(IMediator).GetTypeInfo().Assembly;
            yield return typeof(Startup).GetTypeInfo().Assembly;
            yield return typeof(VoteMonitorContext).GetTypeInfo().Assembly;
            yield return typeof(PollingStationController).GetTypeInfo().Assembly;
            yield return typeof(ObserverController).GetTypeInfo().Assembly;
            yield return typeof(NoteController).GetTypeInfo().Assembly;
            yield return typeof(FormController).GetTypeInfo().Assembly;
            yield return typeof(AnswersController).GetTypeInfo().Assembly;
            yield return typeof(UploadFileHandler).GetTypeInfo().Assembly;
            yield return typeof(NotificationController).GetTypeInfo().Assembly;
            yield return typeof(StatisticsController).GetTypeInfo().Assembly;
            yield return typeof(DataExportController).GetTypeInfo().Assembly;
            // just to identify VotingIrregularities.Domain assembly
        }
    }
}