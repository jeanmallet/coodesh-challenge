# Contexto de build = raiz do repo (precisa alcançar spec/FIX44.xml, compartilhado com o OrderAccumulator).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY spec/FIX44.xml spec/FIX44.xml
COPY src/OrderGenerator/OrderGenerator.csproj src/OrderGenerator/
RUN dotnet restore src/OrderGenerator/OrderGenerator.csproj
COPY src/OrderGenerator/ src/OrderGenerator/
RUN dotnet publish src/OrderGenerator/OrderGenerator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# curl só para o HEALTHCHECK: a imagem base não traz cliente HTTP algum.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .

HEALTHCHECK --interval=5s --timeout=3s --start-period=5s --retries=5 \
    CMD curl -f http://localhost:5080/health || exit 1

EXPOSE 5080
ENTRYPOINT ["dotnet", "OrderGenerator.dll"]
