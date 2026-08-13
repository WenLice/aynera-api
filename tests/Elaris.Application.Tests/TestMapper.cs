using AutoMapper;
using Elaris.Application.Mapping;

namespace Elaris.Application.Tests;

internal static class TestMapper
{
    public static IMapper Instance { get; } = new MapperConfiguration(cfg =>
        cfg.AddMaps(typeof(ApplicationMappingProfile).Assembly)).CreateMapper();
}
