using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProjectHub.Api.Modules.Identity.Development;

namespace ProjectHub.Api.UnitTests;

public class DevelopmentIdentityTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Refuses_to_register_outside_development(string environmentName)
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddDevelopmentIdentity(new TestEnvironment(environmentName), new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void Registers_in_development()
    {
        var services = new ServiceCollection();

        services.AddDevelopmentIdentity(new TestEnvironment(Environments.Development), new ConfigurationBuilder().Build());

        Assert.NotEmpty(services);
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "ProjectHub.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
