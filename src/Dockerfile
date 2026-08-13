FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Elaris.Api/Elaris.Api.csproj Elaris.Api/
COPY Elaris.Application/Elaris.Application.csproj Elaris.Application/
COPY Elaris.Domain/Elaris.Domain.csproj Elaris.Domain/
COPY Elaris.Infrastructure/Elaris.Infrastructure.csproj Elaris.Infrastructure/
COPY Elaris.Notifications/Elaris.Notifications.csproj Elaris.Notifications/
COPY Elaris.Persistence/Elaris.Persistence.csproj Elaris.Persistence/
RUN dotnet restore Elaris.Api/Elaris.Api.csproj

COPY . .
RUN dotnet publish Elaris.Api/Elaris.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Elaris.Api.dll"]
