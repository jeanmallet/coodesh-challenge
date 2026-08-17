# Contexto de build = raiz do repo (precisa alcançar spec/FIX44.xml, compartilhado com o OrderGenerator).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY spec/FIX44.xml spec/FIX44.xml
COPY src/OrderAccumulator/OrderAccumulator.csproj src/OrderAccumulator/
RUN dotnet restore src/OrderAccumulator/OrderAccumulator.csproj
COPY src/OrderAccumulator/ src/OrderAccumulator/
RUN dotnet publish src/OrderAccumulator/OrderAccumulator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Acceptor TCP puro, sem HTTP: sonda a porta FIX diretamente (recurso do bash, já presente
# na imagem base Debian, sem instalar netcat/curl só para isso).
HEALTHCHECK --interval=5s --timeout=3s --start-period=5s --retries=5 \
    CMD ["bash", "-c", "echo > /dev/tcp/127.0.0.1/5001"]

EXPOSE 5001
ENTRYPOINT ["dotnet", "OrderAccumulator.dll"]
