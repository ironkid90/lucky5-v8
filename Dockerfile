# Stage 1: Build the backend and assets
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["server/src/Lucky5.Api/Lucky5.Api.csproj", "Lucky5.Api/"]
COPY ["server/src/Lucky5.Domain/Lucky5.Domain.csproj", "Lucky5.Domain/"]
COPY ["server/src/Lucky5.Infrastructure/Lucky5.Infrastructure.csproj", "Lucky5.Infrastructure/"]
COPY ["server/src/Lucky5.Application/Lucky5.Application.csproj", "Lucky5.Application/"]
COPY ["server/src/Lucky5.Realtime/Lucky5.Realtime.csproj", "Lucky5.Realtime/"]
COPY ["nuget.config", "."]

RUN dotnet restore "Lucky5.Api/Lucky5.Api.csproj"
COPY server/src/ .
WORKDIR "/src/Lucky5.Api"
RUN dotnet publish "Lucky5.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime image optimization
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Environment optimizations for containerized environments
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_gcServer=1

EXPOSE 8080
ENTRYPOINT ["dotnet", "Lucky5.Api.dll"]