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

# Acceptor TCP puro, sem HTTP: não dá para sondar com uma conexão real (ex.: /dev/tcp) —
# o acceptor QuickFIX trata qualquer conexão que não manda um Logon válido como sessão
# malformada e loga "Broken pipe" a cada probe. Em vez disso lê /proc/net/tcp (procfs,
# sem abrir socket nenhum) e confere se a porta 5001 (hex 1389) está em LISTEN.
HEALTHCHECK --interval=5s --timeout=3s --start-period=5s --retries=5 \
    CMD ["bash", "-c", "grep -q ':1389 ' /proc/net/tcp"]

EXPOSE 5001
ENTRYPOINT ["dotnet", "OrderAccumulator.dll"]
