FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY Directory.Build.props ./
COPY src/BuildingBlocks/Shared.Contracts/Shared.Contracts.csproj src/BuildingBlocks/Shared.Contracts/
COPY src/BuildingBlocks/Shared.Infrastructure/Shared.Infrastructure.csproj src/BuildingBlocks/Shared.Infrastructure/
COPY src/Services/Portfolio.Service/Portfolio.Service.csproj src/Services/Portfolio.Service/

RUN dotnet restore src/Services/Portfolio.Service/Portfolio.Service.csproj

COPY src/BuildingBlocks/Shared.Contracts/ src/BuildingBlocks/Shared.Contracts/
COPY src/BuildingBlocks/Shared.Infrastructure/ src/BuildingBlocks/Shared.Infrastructure/
COPY src/Services/Portfolio.Service/ src/Services/Portfolio.Service/

WORKDIR /app/src/Services/Portfolio.Service
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Portfolio.Service.dll"]
