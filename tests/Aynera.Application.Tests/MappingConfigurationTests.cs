using AutoMapper;
using Aynera.Application.Mapping;
using Aynera.Infrastructure.Mapping;

namespace Aynera.Application.Tests;

public class MappingConfigurationTests
{
    [Fact]
    public void ApplicationAndInfrastructureProfiles_AreValid()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(
                typeof(ApplicationMappingProfile).Assembly,
                typeof(InfrastructureMappingProfile).Assembly);
        });

        config.AssertConfigurationIsValid();
    }
}
