FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY sync-contracts/SoftRestaurant.Sync.Contracts.csproj sync-contracts/
COPY central-api/SoftRestaurant.CentralApi.csproj central-api/
RUN dotnet restore central-api/SoftRestaurant.CentralApi.csproj

COPY sync-contracts/ sync-contracts/
COPY central-api/ central-api/
RUN dotnet publish central-api/SoftRestaurant.CentralApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SoftRestaurant.CentralApi.dll"]
