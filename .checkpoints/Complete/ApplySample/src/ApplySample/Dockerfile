FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 7789

ENV ASPNETCORE_URLS=http://+:7789

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ApplySample.csproj", "./"]
RUN dotnet clean
RUN dotnet restore "ApplySample.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "ApplySample.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ApplySample.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ApplySample.dll"]
HEALTHCHECK --interval=30s --timeout=15s --start-period=30s \
    CMD wget --no-verbose --tries=1 --spider http://localhost:7789/hc || exit 1