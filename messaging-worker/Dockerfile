FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app


COPY *.csproj ./
RUN dotnet restore messaging.worker.csproj


COPY . ./
RUN dotnet publish messaging.worker.csproj -c Release -o /out


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl para o HEALTHCHECK: a imagem de runtime do .NET nao traz curl nem wget.
RUN apt-get update \
  && apt-get install -y --no-install-recommends curl \
  && rm -rf /var/lib/apt/lists/*

COPY --from=build /out .

EXPOSE 8080

# Readiness: /health confirma que a conexao com o RabbitMQ esta aberta.
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
  CMD curl -fsS "http://127.0.0.1:${HEALTH_PORT:-8080}/health" || exit 1

ENTRYPOINT ["dotnet", "messaging.worker.dll"]
