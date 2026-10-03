# Just enough to run acer-stub in docker-compose (ADR-0015) — not the
# production multi-stage Dockerfile every real service still needs
# (tracked separately, Phase 3). acer-stub has no project references, so a
# single build stage plus a slim runtime stage is all it needs.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Seamline.AcerStub/Seamline.AcerStub.csproj src/Seamline.AcerStub/
RUN dotnet restore src/Seamline.AcerStub/Seamline.AcerStub.csproj
COPY src/Seamline.AcerStub/ src/Seamline.AcerStub/
RUN dotnet publish src/Seamline.AcerStub/Seamline.AcerStub.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# tini as PID 1 (ADR-0029): the kernel ignores signals without a handler for PID 1,
# so a bare `dotnet` process cannot abort on an unhandled exception and spins forever.
RUN apt-get update && apt-get install -y --no-install-recommends tini && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["tini", "--", "dotnet", "Seamline.AcerStub.dll"]
