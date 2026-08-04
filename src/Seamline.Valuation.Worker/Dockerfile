FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Seamline.SharedKernel/*.csproj src/Seamline.SharedKernel/
COPY src/Seamline.Valuation.Worker/*.csproj src/Seamline.Valuation.Worker/
COPY src/Modules/Reference/Seamline.Modules.Reference/*.csproj src/Modules/Reference/Seamline.Modules.Reference/
COPY src/Modules/Reference/Seamline.Modules.Reference.Contracts/*.csproj src/Modules/Reference/Seamline.Modules.Reference.Contracts/
COPY src/Modules/MarketData/Seamline.Modules.MarketData/*.csproj src/Modules/MarketData/Seamline.Modules.MarketData/
COPY src/Modules/MarketData/Seamline.Modules.MarketData.Contracts/*.csproj src/Modules/MarketData/Seamline.Modules.MarketData.Contracts/
COPY src/Modules/Risk/Seamline.Modules.Risk/*.csproj src/Modules/Risk/Seamline.Modules.Risk/
COPY src/Modules/Risk/Seamline.Modules.Risk.Contracts/*.csproj src/Modules/Risk/Seamline.Modules.Risk.Contracts/
COPY src/Modules/Trading/Seamline.Modules.Trading.Contracts/*.csproj src/Modules/Trading/Seamline.Modules.Trading.Contracts/
COPY src/Modules/Identity/Seamline.Modules.Identity.Contracts/*.csproj src/Modules/Identity/Seamline.Modules.Identity.Contracts/
RUN dotnet restore src/Seamline.Valuation.Worker/Seamline.Valuation.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Seamline.Valuation.Worker/Seamline.Valuation.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Seamline.Valuation.Worker.dll"]
