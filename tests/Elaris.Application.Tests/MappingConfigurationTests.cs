using AutoMapper;
using Elaris.Application.Mapping;
using Elaris.Infrastructure.Mapping;

namespace Elaris.Application.Tests;

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
