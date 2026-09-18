FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Aynera.Api/Aynera.Api.csproj Aynera.Api/
COPY Aynera.Application/Aynera.Application.csproj Aynera.Application/
COPY Aynera.Domain/Aynera.Domain.csproj Aynera.Domain/
COPY Aynera.Infrastructure/Aynera.Infrastructure.csproj Aynera.Infrastructure/
COPY Aynera.Notifications/Aynera.Notifications.csproj Aynera.Notifications/
COPY Aynera.Persistence/Aynera.Persistence.csproj Aynera.Persistence/
RUN dotnet restore Aynera.Api/Aynera.Api.csproj

COPY . .
RUN dotnet publish Aynera.Api/Aynera.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Aynera.Api.dll"]
