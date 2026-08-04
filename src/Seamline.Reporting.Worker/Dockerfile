FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Seamline.SharedKernel/*.csproj src/Seamline.SharedKernel/
COPY src/Seamline.Reporting.Worker/*.csproj src/Seamline.Reporting.Worker/
COPY src/Modules/Trading/Seamline.Modules.Trading/*.csproj src/Modules/Trading/Seamline.Modules.Trading/
COPY src/Modules/Trading/Seamline.Modules.Trading.Contracts/*.csproj src/Modules/Trading/Seamline.Modules.Trading.Contracts/
COPY src/Modules/Reference/Seamline.Modules.Reference.Contracts/*.csproj src/Modules/Reference/Seamline.Modules.Reference.Contracts/
COPY src/Modules/Identity/Seamline.Modules.Identity.Contracts/*.csproj src/Modules/Identity/Seamline.Modules.Identity.Contracts/
COPY src/Modules/Risk/Seamline.Modules.Risk.Contracts/*.csproj src/Modules/Risk/Seamline.Modules.Risk.Contracts/
RUN dotnet restore src/Seamline.Reporting.Worker/Seamline.Reporting.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Seamline.Reporting.Worker/Seamline.Reporting.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Seamline.Reporting.Worker.dll"]
