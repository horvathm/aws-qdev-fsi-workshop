FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 6268

ENV ASPNETCORE_URLS=http://+:6268

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Origination.csproj", "./"]
RUN dotnet clean
RUN dotnet restore "Origination.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "Origination.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Origination.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Origination.dll"]
HEALTHCHECK --interval=30s --timeout=15s --start-period=30s \
    CMD wget --no-verbose --tries=1 --spider http://localhost:6268/hc || exit 1