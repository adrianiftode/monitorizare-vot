using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Collections.Generic;
using System.Reflection;
using VoteMonitor.Entities;
using VotingIrregularities.Api;

namespace VotingIrregularities.Tests.Functional.Fixtures
{
    public class WebApiApplicationFactory : WebApplicationFactory<Startup>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "ConnectionStrings:DefaultConnection" , "" }
                });
            });

            builder.ConfigureServices(services =>
            {
                services
                    .AddEntityFrameworkInMemoryDatabase()
                    .AddDbContext<VoteMonitorContext>(options =>
                    {
                        options.UseInMemoryDatabase("InMemoryDbForTesting");

                        // Create a new service provider.
                        var historyMock = new Mock<IHistoryRepository>();
                        historyMock.Setup(c => c.GetAppliedMigrations())
                            .Returns(new[]
                            {
                                new HistoryRow("first-migration", "v1")

                            });

                        var assembliesMock = new Mock<IMigrationsAssembly>();
                        assembliesMock.SetupGet(c => c.Migrations)
                            .Returns(new Dictionary<string, TypeInfo>
                            {
                                { "first-migration", typeof(IMigrationsAssembly).GetTypeInfo()}
                            });

                        var internalDbServices = new ServiceCollection()
                                .AddEntityFrameworkInMemoryDatabase()
                                .AddScoped(typeof(IHistoryRepository), srv => historyMock.Object)
                                .AddScoped(typeof(IMigrationsAssembly), srv => assembliesMock.Object)
                            ;


                        options.UseInternalServiceProvider(internalDbServices
                            .BuildServiceProvider());
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                var sp = services.BuildServiceProvider();

                using (var scope = sp.CreateScope())
                {
                    var scopedServices = scope.ServiceProvider;
                    var db = scopedServices.GetRequiredService<VoteMonitorContext>();

                    db.Database.EnsureCreated(); // this will fire the calls on HasData

                    db.EnsureSeedData();

                    db.Observers.Add(new Observer
                    {
                        Pin = "1234",
                        Phone = "0722222222",
                        Id = 1
                    });

                    db.SaveChanges();
                }
            });
        }
    }
}