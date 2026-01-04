# ABOUTME: Builds the Todo Backend API as a container image for AWS ECS deployments.
# ABOUTME: Uses a multi-stage .NET publish to produce a lean runtime image listening on port 8080.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["TodoBackend.sln", "./"]
COPY ["src/TodoBackend.Api/TodoBackend.Api.csproj", "src/TodoBackend.Api/"]
COPY ["tests/TodoBackend.Tests/TodoBackend.Tests.csproj", "tests/TodoBackend.Tests/"]

RUN dotnet restore "src/TodoBackend.Api/TodoBackend.Api.csproj"

COPY . .
RUN dotnet publish "src/TodoBackend.Api/TodoBackend.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TodoBackend.Api.dll"]
