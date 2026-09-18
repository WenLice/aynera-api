using AutoMapper;
using Aynera.Application.Mapping;

namespace Aynera.Application.Tests;

internal static class TestMapper
{
    public static IMapper Instance { get; } = new MapperConfiguration(cfg =>
        cfg.AddMaps(typeof(ApplicationMappingProfile).Assembly)).CreateMapper();
}
