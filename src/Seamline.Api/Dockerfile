FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Seamline.SharedKernel/*.csproj src/Seamline.SharedKernel/
COPY src/Seamline.Api/*.csproj src/Seamline.Api/
COPY src/Modules/Reference/Seamline.Modules.Reference/*.csproj src/Modules/Reference/Seamline.Modules.Reference/
COPY src/Modules/Reference/Seamline.Modules.Reference.Contracts/*.csproj src/Modules/Reference/Seamline.Modules.Reference.Contracts/
COPY src/Modules/Trading/Seamline.Modules.Trading/*.csproj src/Modules/Trading/Seamline.Modules.Trading/
COPY src/Modules/Trading/Seamline.Modules.Trading.Contracts/*.csproj src/Modules/Trading/Seamline.Modules.Trading.Contracts/
COPY src/Modules/MarketData/Seamline.Modules.MarketData/*.csproj src/Modules/MarketData/Seamline.Modules.MarketData/
COPY src/Modules/MarketData/Seamline.Modules.MarketData.Contracts/*.csproj src/Modules/MarketData/Seamline.Modules.MarketData.Contracts/
COPY src/Modules/Risk/Seamline.Modules.Risk/*.csproj src/Modules/Risk/Seamline.Modules.Risk/
COPY src/Modules/Risk/Seamline.Modules.Risk.Contracts/*.csproj src/Modules/Risk/Seamline.Modules.Risk.Contracts/
COPY src/Modules/Settlement/Seamline.Modules.Settlement/*.csproj src/Modules/Settlement/Seamline.Modules.Settlement/
COPY src/Modules/Settlement/Seamline.Modules.Settlement.Contracts/*.csproj src/Modules/Settlement/Seamline.Modules.Settlement.Contracts/
COPY src/Modules/Identity/Seamline.Modules.Identity/*.csproj src/Modules/Identity/Seamline.Modules.Identity/
COPY src/Modules/Identity/Seamline.Modules.Identity.Contracts/*.csproj src/Modules/Identity/Seamline.Modules.Identity.Contracts/
COPY src/Modules/Audit/Seamline.Modules.Audit/*.csproj src/Modules/Audit/Seamline.Modules.Audit/
COPY src/Modules/Audit/Seamline.Modules.Audit.Contracts/*.csproj src/Modules/Audit/Seamline.Modules.Audit.Contracts/
RUN dotnet restore src/Seamline.Api/Seamline.Api.csproj

COPY src/ src/
RUN dotnet publish src/Seamline.Api/Seamline.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Seamline.Api.dll"]
